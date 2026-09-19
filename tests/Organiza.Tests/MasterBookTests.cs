using System.Text.Json;
using Organiza.Application.Abstractions;
using Organiza.Application.Common;
using Organiza.Application.Services;
using Organiza.Domain.MasterBook;
using Organiza.Domain.Files;
using Organiza.Domain.Operations;
using Organiza.Domain.Organization;
using Organiza.Infrastructure.Files;
using Organiza.Infrastructure.History;
using Organiza.Tests.Support;

namespace Organiza.Tests;

public sealed class MasterBookTests
{
    [Fact]
    public async Task Generate_CreatesMarkdownAndJsonInProtectedRoot_WithTwelveSections()
    {
        using var parent = new TemporaryDirectory();
        var root = Directory.CreateDirectory(parent.PathFor("P 42 - Cliente - Local Personalizado")).FullName;
        var pdf = Path.Combine(root, "peticao.pdf");
        await File.WriteAllBytesAsync(pdf, [10, 20, 30, 40]);
        var fs = new PhysicalFileSystem();
        var analyzer = new DocumentAnalysisService(new Sha256HashCalculator(fs), new LegalTextExtractor());
        var progressMessages = new List<MasterBookProgress>();
        var service = new MasterBookService(fs, analyzer, new PathGuard(), new JsonHistoryStore(root));

        var result = await service.GenerateAsync(root, ExplicitApproval.Grant("teste"),
            new CaptureProgress(progressMessages));

        Assert.Equal(Path.Combine(root, MasterBookFileNames.Markdown), result.MarkdownPath);
        Assert.Equal(Path.Combine(root, MasterBookFileNames.Json), result.JsonPath);
        Assert.True(Directory.Exists(root));
        Assert.Equal("P 42 - Cliente - Local Personalizado", Path.GetFileName(root));
        var markdown = await File.ReadAllTextAsync(result.MarkdownPath);
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, (await File.ReadAllBytesAsync(result.MarkdownPath))[..3]);
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, (await File.ReadAllBytesAsync(result.JsonPath))[..3]);
        Assert.Contains("## 1 IDENTIFICAÇÃO, OBJETO E ESCOPO", markdown);
        Assert.Contains("### 6.12 Resumo executivo para o cliente", markdown);
        Assert.Contains("### 2.1 Bem, leilão e arrematação", markdown);
        Assert.Contains("## APÊNDICE A — INVENTÁRIO TÉCNICO DAS FONTES", markdown);
        Assert.Contains("validação humana", markdown, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("| Referência | Documento | Tamanho | Método | Cobertura | OCR | Integridade |", markdown);
        Assert.Contains("### 6.2 Fase de conhecimento e evolução processual", markdown);
        Assert.Contains("### 6.5 Recursos, razões e contrarrazões", markdown);
        Assert.Contains("### 6.6 Atuação dos patronos anteriores", markdown);
        Assert.Contains("### 6.8 Inventário financeiro detalhado", markdown);
        Assert.Contains("## 4 CONCILIAÇÃO FINANCEIRA DE PARCELAS E RECIBOS", markdown);
        Assert.Contains("### 7.1 Matriz de teses jurídicas", markdown);
        Assert.Contains("### 7.2 Matriz de riscos processuais", markdown);
        Assert.Contains("## 8 PLANO DE AÇÃO E OBRIGAÇÕES", markdown);
        Assert.Contains("## APÊNDICE B — REGISTRO DE INTEGRIDADE", markdown);
        Assert.Contains("**0000001-00.2026.5.00.0001**", markdown);
        Assert.Contains("**R$ 1.234,56**", markdown);
        Assert.Contains("**03/08/2026**", markdown);
        Assert.Matches(@"DOC-001  [A-F0-9]{64}\r?\n", markdown);
        Assert.DoesNotContain("| Arquivo | Tamanho | Extração | SHA-256 |", markdown);
        Assert.DoesNotContain("Identifica-\nção", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("IdentificaÃ", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain('\uFFFD', markdown);
        Assert.DoesNotMatch("(?<!\\r)\\n", markdown);
        Assert.Equal(markdown, markdown.Normalize(System.Text.NormalizationForm.FormC));
        var appendixIndex = markdown.IndexOf("## APÊNDICE A — INVENTÁRIO TÉCNICO DAS FONTES", StringComparison.Ordinal);
        var sectionTwelveIndex = markdown.IndexOf("### 6.12 Resumo executivo para o cliente", StringComparison.Ordinal);
        Assert.True(appendixIndex > sectionTwelveIndex);
        var narrativeBody = markdown[..appendixIndex];
        Assert.DoesNotMatch(@"\b[A-F0-9]{64}\b", narrativeBody);
        Assert.DoesNotContain(root, narrativeBody, StringComparison.OrdinalIgnoreCase);
        var jsonText = await File.ReadAllTextAsync(result.JsonPath);
        Assert.Contains("Leilão, arrematação, parcelamentos e cancelamentos", jsonText);
        Assert.DoesNotContain("ArremataÃ", jsonText, StringComparison.Ordinal);
        using var json = JsonDocument.Parse(jsonText);
        Assert.Equal(12, json.RootElement.GetProperty("Sections").GetArrayLength());
        Assert.Equal("3.0", json.RootElement.GetProperty("SchemaVersion").GetString());
        Assert.NotEmpty(json.RootElement.GetProperty("FinancialInventory").EnumerateArray());
        Assert.True(json.RootElement.TryGetProperty("ExecutiveSynthesis", out _));
        Assert.NotEmpty(json.RootElement.GetProperty("FinancialConsolidation").EnumerateArray());
        Assert.NotEmpty(json.RootElement.GetProperty("LegalThesisMatrix").EnumerateArray());
        Assert.NotEmpty(json.RootElement.GetProperty("ProceduralRiskMatrix").EnumerateArray());
        Assert.NotEmpty(json.RootElement.GetProperty("StrategicActionPlan").EnumerateArray());
        Assert.True(json.RootElement.TryGetProperty("RegistryAuctionChronology", out _));
        Assert.True(json.RootElement.TryGetProperty("PaymentReconciliation", out _));
        Assert.True(json.RootElement.TryGetProperty("BailiffDiligences", out _));
        Assert.Contains("ExtractedText", json.RootElement.GetProperty("SourceDocuments")[0].EnumerateObject()
            .Select(property => property.Name));
        Assert.Equal("0000001-00.2026.5.00.0001",
            json.RootElement.GetProperty("IdentifiedProcesses")[0].GetString());
        Assert.True(json.RootElement.GetProperty("RootNameProtected").GetBoolean());
        Assert.Contains(progressMessages, item => item.Message.Contains("concluído", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Generate_WithoutApproval_WritesNothing()
    {
        using var directory = new TemporaryDirectory();
        var fs = new PhysicalFileSystem();
        var service = new MasterBookService(fs,
            new DocumentAnalysisService(new Sha256HashCalculator(fs), new LegalTextExtractor()),
            new PathGuard(), new JsonHistoryStore(directory.FullPath));

        await Assert.ThrowsAsync<ApprovalRequiredException>(() => service.GenerateAsync(
            directory.FullPath, ExplicitApproval.Denied("teste")));

        Assert.False(File.Exists(directory.PathFor(MasterBookFileNames.Markdown)));
        Assert.False(File.Exists(directory.PathFor(MasterBookFileNames.Json)));
    }

    [Fact]
    public async Task Generate_WhenExistingMarkdownIsOpen_SavesUpdatedReportWithoutLosingAnalysis()
    {
        using var directory = new TemporaryDirectory();
        await File.WriteAllTextAsync(directory.PathFor("documento.txt"), "Processo 0000001-00.2026.5.00.0001");
        var fs = new PhysicalFileSystem();
        var service = new MasterBookService(fs,
            new DocumentAnalysisService(new Sha256HashCalculator(fs), new LegalTextExtractor()),
            new PathGuard(), new JsonHistoryStore(directory.FullPath));
        var first = await service.GenerateAsync(directory.FullPath, ExplicitApproval.Grant("teste"));
        await using var locked = new FileStream(first.MarkdownPath, FileMode.Open, FileAccess.Read, FileShare.Read);

        var second = await service.GenerateAsync(directory.FullPath, ExplicitApproval.Grant("teste"));

        Assert.Equal(directory.PathFor(MasterBookFileNames.UpdatedMarkdown), second.MarkdownPath);
        Assert.True(File.Exists(second.MarkdownPath));
        Assert.Contains("RELATÓRIO JURÍDICO-DOCUMENTAL", await File.ReadAllTextAsync(second.MarkdownPath));
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(second.JsonPath));
        Assert.Equal("3.0", json.RootElement.GetProperty("SchemaVersion").GetString());
    }

    [Fact]
    public async Task Generate_WhenStructuredBaseIsLocked_PreservesPreviousJsonAndCleansTemporaryFile()
    {
        using var directory = new TemporaryDirectory();
        await File.WriteAllTextAsync(directory.PathFor("documento.txt"),
            "Processo 0000001-00.2026.5.00.0001");
        var fs = new PhysicalFileSystem();
        var service = new MasterBookService(fs,
            new DocumentAnalysisService(new Sha256HashCalculator(fs), new LegalTextExtractor()),
            new PathGuard(), new JsonHistoryStore(directory.FullPath));
        var first = await service.GenerateAsync(directory.FullPath, ExplicitApproval.Grant("teste"));
        var originalJson = await File.ReadAllBytesAsync(first.JsonPath);
        await using var locked = new FileStream(first.JsonPath, FileMode.Open, FileAccess.Read, FileShare.Read);

        await Assert.ThrowsAnyAsync<Exception>(() =>
            service.GenerateAsync(directory.FullPath, ExplicitApproval.Grant("teste")));

        Assert.Equal(originalJson, await File.ReadAllBytesAsync(first.JsonPath));
        var internalDirectory = directory.PathFor(WorkspaceFilePolicy.InternalDirectoryName);
        Assert.Empty(Directory.EnumerateFiles(internalDirectory, "write-*.tmp", SearchOption.TopDirectoryOnly));
    }

    [Fact]
    public async Task AnalysisCache_ReusesVerifiedFullTextFromMasterBook_AndRejectsChangedContent()
    {
        using var directory = new TemporaryDirectory();
        var source = directory.PathFor("documento.txt");
        await File.WriteAllTextAsync(source, "conteúdo original para o livro mestre");
        var fs = new PhysicalFileSystem();
        var hash = new Sha256HashCalculator(fs);
        var analyzer = new DocumentAnalysisService(hash, new LegalTextExtractor());
        await new MasterBookService(fs, analyzer, new PathGuard(), new JsonHistoryStore(directory.FullPath))
            .GenerateAsync(directory.FullPath, ExplicitApproval.Grant("teste"));
        var cache = new MasterBookAnalysisCache(fs, hash);
        var file = new FileItem(source, fs.GetFileLength(source), ".txt");

        var hit = await cache.LoadValidAsync(directory.FullPath, [file]);

        Assert.Equal(1, hit.CacheHits);
        Assert.Empty(hit.FilesRequiringAnalysis);
        Assert.Contains("Processo 0000001", hit.CachedAnalyses[0].FullText);

        await File.WriteAllTextAsync(source, "conteúdo alterado depois do livro mestre");
        var changed = new FileItem(source, fs.GetFileLength(source), ".txt");
        var miss = await cache.LoadValidAsync(directory.FullPath, [changed]);
        Assert.Equal(0, miss.CacheHits);
        Assert.Single(miss.FilesRequiringAnalysis);
    }

    [Fact]
    public async Task Generate_UsesPreservedOriginalOnce_AndExcludesDuplicatesPartsAndInternalFiles()
    {
        using var directory = new TemporaryDirectory();
        var originals = Directory.CreateDirectory(directory.PathFor("99 ORIGINAIS")).FullName;
        var duplicates = Directory.CreateDirectory(directory.PathFor("98 DUPLICADOS")).FullName;
        var internalFolder = Directory.CreateDirectory(directory.PathFor(".organiza")).FullName;
        await File.WriteAllTextAsync(directory.PathFor("documento.txt"), "principal");
        await File.WriteAllTextAsync(Path.Combine(originals, "processo.pdf"), "original");
        await File.WriteAllTextAsync(Path.Combine(duplicates, "processo-copia.pdf"), "original");
        await File.WriteAllTextAsync(directory.PathFor("processo_parte-01.pdf"), "parte");
        await File.WriteAllTextAsync(Path.Combine(internalFolder, "estado.json"), "interno");
        var fs = new PhysicalFileSystem();
        var service = new MasterBookService(fs,
            new DocumentAnalysisService(new Sha256HashCalculator(fs), new LegalTextExtractor()),
            new PathGuard(), new JsonHistoryStore(directory.FullPath));

        var result = await service.GenerateAsync(directory.FullPath, ExplicitApproval.Grant("teste"));
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(result.JsonPath));
        var paths = json.RootElement.GetProperty("SourceDocuments").EnumerateArray()
            .Select(item => item.GetProperty("RelativePath").GetString()).ToArray();

        Assert.Equal(2, paths.Length);
        Assert.Contains("documento.txt", paths);
        Assert.Contains(Path.Combine("99 ORIGINAIS", "processo.pdf"), paths);
        Assert.DoesNotContain(paths, path => path!.Contains("98 DUPLICADOS", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(paths, path => path!.Contains("_parte-", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(paths, path => path!.Contains(".organiza", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Generate_SeparatesDeclaredDossierProcessFromNumbersMerelyCitedInDocument()
    {
        using var directory = new TemporaryDirectory();
        var source = directory.PathFor("ATOrd_0000002-00.2026.5.00.0002.pdf");
        await File.WriteAllTextAsync(source, "peça");
        var fs = new PhysicalFileSystem();
        var service = new MasterBookService(fs,
            new DocumentAnalysisService(new Sha256HashCalculator(fs), new ReferenceHeavyTextExtractor()),
            new PathGuard(), new JsonHistoryStore(directory.FullPath));

        var result = await service.GenerateAsync(directory.FullPath, ExplicitApproval.Grant("teste"));
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(result.JsonPath));
        var processes = json.RootElement.GetProperty("IdentifiedProcesses").EnumerateArray()
            .Select(item => item.GetString()).ToArray();

        Assert.Equal("0000002-00.2026.5.00.0002", Assert.Single(processes));
        var sourceProcesses = json.RootElement.GetProperty("SourceDocuments")[0]
            .GetProperty("ProcessIdentifiers").EnumerateArray().Select(item => item.GetString()).ToArray();
        Assert.Contains("0000008-00.2026.8.00.0008", sourceProcesses);
    }

    [Fact]
    public async Task Generate_ReportsRegistryRestrictions_CancelledEntries_AndOutstandingEntries()
    {
        using var directory = new TemporaryDirectory();
        var source = directory.PathFor("Matricula_20208 atualizada.pdf");
        await File.WriteAllTextAsync(source, "matrícula digitalizada");
        var fs = new PhysicalFileSystem();
        var service = new MasterBookService(fs,
            new DocumentAnalysisService(new Sha256HashCalculator(fs), new RegistryTextExtractor()),
            new PathGuard(), new JsonHistoryStore(directory.FullPath));

        var result = await service.GenerateAsync(directory.FullPath, ExplicitApproval.Grant("teste"));

        var markdown = await File.ReadAllTextAsync(result.MarkdownPath);
        Assert.Contains("### 3.2 Consolidação de gravames e restrições", markdown);
        Assert.Contains("Penhora", markdown);
        Assert.Contains("Indisponibilidade", markdown);
        Assert.Contains("Baixada/cancelada", markdown);
        Assert.Contains("**Penhora**", markdown);
        Assert.Contains("**R.5**", markdown);
        Assert.Contains("**DOC-001**", markdown);
        Assert.DoesNotContain("registrÃ", markdown, StringComparison.OrdinalIgnoreCase);
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(result.JsonPath));
        var registry = json.RootElement.GetProperty("PropertyRegistryAnalysis");
        Assert.True(registry.GetProperty("RegistryDocumentFound").GetBoolean());
        Assert.Equal("20 208", registry.GetProperty("RegistrationNumber").GetString());
        Assert.Contains(registry.GetProperty("Restrictions").EnumerateArray(), item =>
            item.GetProperty("Type").GetString() == "Indisponibilidade" &&
            item.GetProperty("Status").GetString()!.StartsWith("Pendente", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Generate_CrossesRegistryAuction_ReconcilesInstallmentReceipt_AndBuildsBailiffHistory()
    {
        using var directory = new TemporaryDirectory();
        foreach (var name in new[] { "matricula.pdf", "carta.pdf", "parcelamento.pdf", "recibo.pdf", "mandado.pdf" })
            await File.WriteAllTextAsync(directory.PathFor(name), name);
        var fs = new PhysicalFileSystem();
        var service = new MasterBookService(fs,
            new DocumentAnalysisService(new Sha256HashCalculator(fs), new DeepLegalTextExtractor()),
            new PathGuard(), new JsonHistoryStore(directory.FullPath));

        var result = await service.GenerateAsync(directory.FullPath, ExplicitApproval.Grant("teste"));

        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(result.JsonPath));
        var chronology = json.RootElement.GetProperty("RegistryAuctionChronology").EnumerateArray().ToArray();
        Assert.Contains(chronology, item => item.GetProperty("EventType").GetString() == "Carta de arrematação" &&
            item.GetProperty("Status").GetString()!.Contains("ALERTA", StringComparison.Ordinal));
        var reconciliation = json.RootElement.GetProperty("PaymentReconciliation").EnumerateArray().ToArray();
        Assert.Contains(reconciliation, item => item.GetProperty("InstallmentNumber").GetInt32() == 3 &&
            item.GetProperty("Status").GetString() == "CONCILIADA DOCUMENTALMENTE" &&
            item.GetProperty("ExpectedAmount").GetDecimal() == 1500m &&
            item.GetProperty("PaidAmount").GetDecimal() == 1500m);
        var diligences = json.RootElement.GetProperty("BailiffDiligences").EnumerateArray().ToArray();
        Assert.Contains(diligences, item => item.GetProperty("Outcome").GetString() == "POSITIVA/CUMPRIDA" &&
            item.GetProperty("Act").GetString() == "Citação");
        var markdown = await File.ReadAllTextAsync(result.MarkdownPath);
        Assert.Contains("### 3.3 Cruzamento cronológico com carta ou auto de arrematação", markdown);
        Assert.Contains("### 4.2 Quadro de conciliação parcela × recibo", markdown);
        Assert.Contains("## 5 HISTÓRICO DE DILIGÊNCIAS DOS OFICIAIS DE JUSTIÇA", markdown);
        Assert.Contains("**R$ 1.500,00**", markdown);
        Assert.Contains("**20/04/2022**", markdown);
    }

    private sealed class LegalTextExtractor : IFirstPageTextExtractor
    {
        public Task<ExtractedText> ExtractAsync(string filePath, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ExtractedText(
                "Processo 0000001-00.2026.5.00.0001. Petição inicial e contestação. " +
                "Arrematação cancelada. Recurso com razões e contrarrazões. " +
                "IPTU no valor de R$ 1.234,56. PPI de R$ 800,00 e condomínio de R$ 450,00. " +
                "Intimação e risco de preclusão. Prazo para manifestação em 03/08/2026.",
                TextExtractionMethod.PdfText, 3, 3));
    }

    private sealed class CaptureProgress(List<MasterBookProgress> messages) : IProgress<MasterBookProgress>
    {
        public void Report(MasterBookProgress value) => messages.Add(value);
    }

    private sealed class ReferenceHeavyTextExtractor : IDocumentTextExtractor
    {
        public Task<ExtractedText> ExtractAsync(string filePath, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ExtractedText(
                "Processo principal 0000002-00.2026.5.00.0002. Precedentes citados: " +
                "0000008-00.2026.8.00.0008 e 0000009-00.2026.8.00.0009.",
                TextExtractionMethod.PdfText, 1, 1));
    }

    private sealed class RegistryTextExtractor : IDocumentTextExtractor
    {
        public Task<ExtractedText> ExtractAsync(string filePath, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ExtractedText("""
                1º Oficial de Registro de Imóveis de Diadema
                MATRÍCULA 20 208
                R.5 PENHORA em favor do processo trabalhista, registrada em 19/11/2014.
                AV.15 CANCELAMENTO DA PENHORA R.5, averbado em 28/03/2022.
                AV.18 INDISPONIBILIDADE DE BENS decretada em 10/01/2023, sem baixa identificada.
                """, TextExtractionMethod.Ocr, 3, 3, 3));
    }

    private sealed class DeepLegalTextExtractor : IDocumentTextExtractor
    {
        public Task<ExtractedText> ExtractAsync(string filePath, CancellationToken cancellationToken = default)
        {
            var text = Path.GetFileName(filePath).ToLowerInvariant() switch
            {
                "matricula.pdf" => "1º Oficial de Registro de Imóveis. Matrícula 20.208. R.5 PENHORA registrada em 19/11/2019, sem baixa identificada.",
                "carta.pdf" => "CARTA DE ARREMATAÇÃO expedida em 20/04/2022. Arrematação do imóvel pelo valor de R$ 150.000,00.",
                "parcelamento.pdf" => "Parcela nº 3 no valor de R$ 1.500,00, com vencimento em 10/05/2022. O arrematante deverá pagar a obrigação.",
                "recibo.pdf" => "Comprovante de pagamento efetuado. Parcela nº 3 paga em 09/05/2022 no valor de R$ 1.500,00, com autenticação bancária.",
                "mandado.pdf" => "Certifico e dou fé que, em 12/06/2022, eu, Oficial de Justiça, citei João da Silva na Rua das Flores, 72, Vila Carmosina. Mandado cumprido.",
                _ => string.Empty
            };
            return Task.FromResult(new ExtractedText(text, TextExtractionMethod.PdfText, 1, 1));
        }
    }
}
