using Organiza.Application.Abstractions;
using Organiza.Application.Services;
using Organiza.Domain.Files;
using Organiza.Domain.Operations;
using Organiza.Domain.Organization;
using Organiza.Infrastructure.Files;
using Organiza.Infrastructure.History;
using Organiza.Infrastructure.Pdf;
using Organiza.Tests.Support;
using System.Text;
using System.Text.RegularExpressions;
using System.IO.Compression;

namespace Organiza.Tests;

public sealed class ContentAnalysisTests
{
    [Fact]
    public async Task RealPdfExtractor_ReadsTheFirstPageWithPdfToText()
    {
        using var directory = new TemporaryDirectory();
        var path = directory.PathFor("texto-real.pdf");
        await File.WriteAllBytesAsync(path, CreateMinimalTextPdf(
            "Processo 0000001-00.2026.5.00.0001 Data 03/08/2026"));

        var extracted = await new PdfFirstPageTextExtractor().ExtractAsync(path);

        Assert.Equal(TextExtractionMethod.PdfText, extracted.Method);
        Assert.Contains("0000001-00.2026.5.00.0001", extracted.Text);
        Assert.Contains("03/08/2026", extracted.Text);
    }

    [Fact]
    public async Task Analyzer_ExtractsRealProcessNumberAndDateFromFirstPageText()
    {
        using var directory = new TemporaryDirectory();
        var path = directory.PathFor("entrada.pdf");
        await File.WriteAllBytesAsync(path, [1, 2, 3]);
        var fs = new PhysicalFileSystem();
        var analyzer = new DocumentAnalysisService(new Sha256HashCalculator(fs),
            new FixedTextExtractor("TRT Processo 0000001-00.2026.5.00.0001 distribuído em 03/08/2026"));

        var result = Assert.Single(await analyzer.AnalyzeAsync(
            [new FileItem(path, 3, ".pdf")]));

        Assert.Contains("0000001-00.2026.5.00.0001", result.ProcessIdentifiers);
        Assert.Contains("03/08/2026", result.DocumentDates);
        Assert.Equal(TextExtractionMethod.PdfText, result.ExtractionMethod);
    }

    [Fact]
    public async Task Analyzer_UsesPhysicalPdfExtension_WhenListedMetadataIsMissing()
    {
        using var directory = new TemporaryDirectory();
        var path = directory.PathFor("conteudo-juridico.pdf");
        await File.WriteAllBytesAsync(path, CreateMinimalTextPdf(
            "Partes Maria e João Processo 0000001-00.2026.5.00.0001 em 03/08/2026"));
        var fs = new PhysicalFileSystem();
        var analyzer = new DocumentAnalysisService(
            new Sha256HashCalculator(fs), new PdfFirstPageTextExtractor());

        var analysis = Assert.Single(await analyzer.AnalyzeAsync(
            [new FileItem(path, fs.GetFileLength(path), string.Empty)]));

        Assert.Equal(TextExtractionMethod.PdfText, analysis.ExtractionMethod);
        Assert.Contains("Maria", analysis.FirstPageText, StringComparison.Ordinal);
        Assert.Contains("0000001-00.2026.5.00.0001", analysis.ProcessIdentifiers);
        Assert.Contains("03/08/2026", analysis.DocumentDates);
    }

    [Fact]
    public async Task DifferentContentAndSizes_NeverKeepTheSameGenericSuggestion()
    {
        using var directory = new TemporaryDirectory();
        var small = directory.PathFor("pequeno.pdf");
        var large = directory.PathFor("grande.pdf");
        await File.WriteAllBytesAsync(small, new byte[7 * 1024]);
        await File.WriteAllBytesAsync(large, new byte[833 * 1024]);
        var fs = new PhysicalFileSystem();
        var analyzer = new DocumentAnalysisService(new Sha256HashCalculator(fs), new PathBasedTextExtractor());
        var service = new RenameService(fs, new SameGenericNameGateway(), analyzer, new PathGuard(),
            new JsonHistoryStore(directory.FullPath), new EmptyFolderCleaner(fs));
        var files = new[]
        {
            new FileItem(small, fs.GetFileLength(small), ".pdf"),
            new FileItem(large, fs.GetFileLength(large), ".pdf")
        };

        var suggestions = await service.GenerateNamesAsync(files, explicitGenerationRequest: true);

        Assert.Equal(2, suggestions.Select(item => item.SuggestedName)
            .Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Contains(suggestions, item => item.SuggestedName.StartsWith("01 - ", StringComparison.Ordinal));
        Assert.Contains(suggestions, item => item.SuggestedName.StartsWith("02 - ", StringComparison.Ordinal));
        Assert.All(suggestions, item => Assert.Contains("bytes", item.Reason!, StringComparison.Ordinal));
        Assert.All(suggestions, item => Assert.True(item.IsSelected));
    }

    [Fact]
    public async Task DifferentContentWithSameNaturalDiscriminator_UsesStableTechnicalReferenceInsteadOfFailing()
    {
        using var directory = new TemporaryDirectory();
        var first = directory.PathFor("primeiro.pdf");
        var second = directory.PathFor("segundo.pdf");
        await File.WriteAllTextAsync(first, "mesmas palavras iniciais conteúdo A");
        await File.WriteAllTextAsync(second, "mesmas palavras iniciais conteúdo B");
        var fs = new PhysicalFileSystem();
        var analyzer = new DocumentAnalysisService(new Sha256HashCalculator(fs), new SameOpeningTextExtractor());
        var service = new RenameService(fs, new SameGenericNameGateway(), analyzer, new PathGuard(),
            new JsonHistoryStore(directory.FullPath), new EmptyFolderCleaner(fs));

        var suggestions = await service.GenerateNamesAsync([
            new FileItem(first, fs.GetFileLength(first), ".pdf"),
            new FileItem(second, fs.GetFileLength(second), ".pdf")], true);

        Assert.Equal(2, suggestions.Select(item => item.SuggestedName)
            .Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Contains(suggestions, item => item.SuggestedName.StartsWith("01 - ", StringComparison.Ordinal));
        Assert.Contains(suggestions, item => item.SuggestedName.StartsWith("02 - ", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Analyzer_PreservesContentBeyondTheFormerTwelveThousandCharacterLimit()
    {
        using var directory = new TemporaryDirectory();
        var path = directory.PathFor("integral.txt");
        var text = new string('A', 15_000) + " CONTRARRAZÕES FINAIS R$ 9.876,54";
        await File.WriteAllTextAsync(path, text);
        var fs = new PhysicalFileSystem();
        var analyzer = new DocumentAnalysisService(new Sha256HashCalculator(fs),
            new FixedTextExtractor(text));

        var result = Assert.Single(await analyzer.AnalyzeAsync(
            [new FileItem(path, fs.GetFileLength(path), ".txt")]));

        Assert.True(result.FullText.Length > 15_000);
        Assert.Contains("CONTRARRAZÕES FINAIS", result.FullText);
    }

    [Fact]
    public async Task Analyzer_UsesBoundedParallelismAndPreservesInputOrder()
    {
        using var directory = new TemporaryDirectory();
        var fs = new PhysicalFileSystem();
        var paths = Enumerable.Range(1, 8)
            .Select(index => directory.PathFor($"{index:00}.txt"))
            .ToArray();
        foreach (var path in paths) await File.WriteAllTextAsync(path, Path.GetFileName(path));
        var extractor = new ConcurrencyTrackingExtractor();
        var analyzer = new DocumentAnalysisService(new Sha256HashCalculator(fs), extractor);
        var files = paths.Select(path => new FileItem(path, fs.GetFileLength(path), ".txt")).ToArray();

        var result = await analyzer.AnalyzeAsync(files);

        Assert.Equal(paths, result.Select(item => item.File.FullPath));
        Assert.InRange(extractor.MaximumConcurrency, 2, 2);
    }

    [Fact]
    public async Task LocalSuggestion_UsesDocumentTypeProcessPlateAndDate()
    {
        var document = new DocumentAnalysis(
            new FileItem("C:\\dossie\\arquivo.pdf", 100, ".pdf"),
            "HASH",
            "Carta de arrematação do veículo placa ABC1D23, processo 0000002-00.2026.5.00.0002, em 03/08/2026.",
            TextExtractionMethod.PdfText,
            ["0000002-00.2026.5.00.0002"], ["03/08/2026"]);

        var suggestion = Assert.Single(await new ContentAwareLocalSuggestionGateway().GenerateAsync([document]));

        Assert.Contains("proc 0000002-00", suggestion.SuggestedName);
        Assert.DoesNotContain("0000002-00.2026", suggestion.SuggestedName);
        Assert.Contains("Carta de arrematação", suggestion.SuggestedName);
        Assert.Contains("ABC1D23", suggestion.SuggestedName);
        Assert.StartsWith("2026-08-03 - ", suggestion.SuggestedName);
        Assert.Equal(StandardFolders.AuctionAndJudicial, suggestion.DestinationFolder);
    }

    [Fact]
    public async Task LocalSuggestion_RenamesTimestampedImageUsingDossierContext()
    {
        var document = new DocumentAnalysis(
            new FileItem("G:\\Meu Drive\\V051 Exemplo 235\\Fotos\\2017-07-26 16.34.44 HDR.jpg", 100, ".jpg"),
            "HASH", string.Empty, TextExtractionMethod.NotApplicable, [], []);

        var suggestion = Assert.Single(await new ContentAwareLocalSuggestionGateway().GenerateAsync([document]));

        Assert.Equal("2017-07-26 - Foto imóvel Avenida Exemplo 100 - 16-34-44 - HDR.jpg", suggestion.SuggestedName);
        Assert.NotEqual(document.File.Name, suggestion.SuggestedName);
        Assert.Equal(StandardFolders.Photos, suggestion.DestinationFolder);
    }

    [Fact]
    public async Task LocalSuggestion_ClassifiesRegistryAndFinancialDocumentsByContent()
    {
        var registry = new DocumentAnalysis(
            new FileItem("C:\\dossie\\documento-a.pdf", 100, ".pdf"), "A",
            "Matrícula 41.833 do Registro de Imóveis", TextExtractionMethod.PdfText, [], []);
        var financial = new DocumentAnalysis(
            new FileItem("C:\\dossie\\documento-b.pdf", 100, ".pdf"), "B",
            "Relatório de débitos de IPTU e condomínio", TextExtractionMethod.PdfText, [], []);

        var suggestions = await new ContentAwareLocalSuggestionGateway().GenerateAsync([registry, financial]);

        Assert.Equal(StandardFolders.Registry, suggestions[0].DestinationFolder);
        Assert.Equal(StandardFolders.Financial, suggestions[1].DestinationFolder);
    }

    [Fact]
    public async Task LocalSuggestion_PrioritizesJudicialHeaderOverAuctionMention_AndUsesSignatureDate()
    {
        var text = """
            PODER JUDICIÁRIO PROCESSO 0000004-00.2026.5.00.0004
            SENTENÇA
            Conforme Carta de Arrematação, o imóvel MATRÍCULA n° 20 208 foi arrematado em 23/07/2015.
            Determino o imediato cancelamento da penhora e baixa da averbação.
            Assinado eletronicamente por: MAGISTRADA - Juntado em: 21/03/2022 13:01:15
            """;
        var document = new DocumentAnalysis(
            new FileItem("C:\\dossie\\5PROVA nao e deste proc.PDF", 100, ".PDF"), "HASH", text,
            TextExtractionMethod.PdfText, ["0000004-00.2026.5.00.0004"], ["23/07/2015", "21/03/2022"]);

        var suggestion = Assert.Single(await new ContentAwareLocalSuggestionGateway().GenerateAsync([document]));

        Assert.Equal(StandardFolders.Litigation, suggestion.DestinationFolder);
        Assert.Contains("Sentença", suggestion.SuggestedName);
        Assert.Contains("Cancelamento de penhora - matrícula 20208", suggestion.SuggestedName);
        Assert.StartsWith("2022-03-21 - ", suggestion.SuggestedName);
        Assert.DoesNotContain("Carta de arrematação", suggestion.SuggestedName);
    }

    [Theory]
    [InlineData("Expedição carta de arrematação.pdf", "Débitos de IPTU citados em manifestação posterior", "Expedição de carta de arrematação", "02 ARREMATAÇÃO")]
    [InlineData("petição de baixa averbação - AV.pdf", "Requer a baixa da averbação. Carta de arrematação expedida em 06/12/2018", "Petição de baixa de averbação", "07 ACOES")]
    [InlineData("Depósito judicial - arrematação.pdf", "Guia de depósito judicial referente à arrematação", "Depósito judicial da arrematação", "02 ARREMATAÇÃO")]
    public async Task LocalSuggestion_PrioritizesExplicitDocumentIdentityOverReferencedSubjects(
        string fileName, string text, string expectedType, string expectedFolder)
    {
        var document = new DocumentAnalysis(
            new FileItem(Path.Combine("C:\\dossie", fileName), 100, ".pdf"), "HASH", text,
            TextExtractionMethod.PdfText, [], ["06/12/2018"]);

        var suggestion = Assert.Single(await new ContentAwareLocalSuggestionGateway().GenerateAsync([document]));

        Assert.Contains(expectedType, suggestion.SuggestedName, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(expectedFolder, suggestion.DestinationFolder);
        if (fileName.StartsWith("Expedição", StringComparison.OrdinalIgnoreCase))
            Assert.DoesNotContain("Débitos de IPTU", suggestion.SuggestedName, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LocalSuggestion_ContractUsesDateAndAbbreviatedParties()
    {
        var text = """
            CONTRATO PARTICULAR DE COMPRA E VENDA firmado em 24/02/2023.
            VENDEDOR: ALFA EXEMPLO, brasileiro, CPF 000.
            COMPRADOR: BETA EXEMPLO, brasileiro, CPF 111.
            """;
        var document = new DocumentAnalysis(
            new FileItem("C:\\dossie\\contrato compra e venda.pdf", 100, ".pdf"), "HASH", text,
            TextExtractionMethod.PdfText, [], ["24/02/2023"]);

        var suggestion = Assert.Single(await new ContentAwareLocalSuggestionGateway().GenerateAsync([document]));

        Assert.Contains("Contrato de compra e venda", suggestion.SuggestedName);
        Assert.Contains("Alfa x Beta", suggestion.SuggestedName);
        Assert.StartsWith("2023-02-24 - ", suggestion.SuggestedName);
        Assert.Equal(StandardFolders.Contracts, suggestion.DestinationFolder);
    }

    [Theory]
    [InlineData("Comprovante de pagamento", "00190500954014481606906809350314337370000000100", "000100")]
    [InlineData("Guia de recolhimento", "836600000015667800481000180975657313001589636081", "636081")]
    public async Task LocalSuggestion_PaymentAndGuideUseComparableTrackingId(
        string type, string barcode, string expectedId)
    {
        var document = new DocumentAnalysis(
            new FileItem($"C:\\dossie\\{type}.pdf", 100, ".pdf"), "HASH",
            $"{type}\nCódigo de barras: {barcode}\nData 15/02/2023",
            TextExtractionMethod.PdfText, [], ["15/02/2023"]);

        var suggestion = Assert.Single(await new ContentAwareLocalSuggestionGateway().GenerateAsync([document]));

        Assert.Contains($"ID {expectedId}", suggestion.SuggestedName);
        Assert.DoesNotContain(barcode, suggestion.SuggestedName);
    }

    [Fact]
    public async Task LocalSuggestion_DoesNotRepeatAlreadyNormalizedPhotoPrefix()
    {
        var document = new DocumentAnalysis(
            new FileItem("C:\\dossie\\Foto do dossiê - 018.jpg", 100, ".jpg"), "HASH", string.Empty,
            TextExtractionMethod.NotApplicable, [], []);

        var suggestion = Assert.Single(await new ContentAwareLocalSuggestionGateway().GenerateAsync([document]));

        Assert.Equal("Foto do dossiê - 018.jpg", suggestion.SuggestedName);
    }

    [Fact]
    public async Task LocalSuggestion_ContentHeaderRepairsLegacyWrongRegistryName()
    {
        var text = """
            PODER JUDICIÁRIO PROCESSO 0000004-00.2026.5.00.0004
            SENTENÇA
            Determino o cancelamento da penhora da matrícula 20208.
            """;
        var document = new DocumentAnalysis(
            new FileItem("C:\\dossie\\Matrícula do imóvel - proc 0001571-37.pdf", 100, ".pdf"),
            "HASH", text, TextExtractionMethod.PdfText, ["0000004-00.2026.5.00.0004"], []);

        var suggestion = Assert.Single(await new ContentAwareLocalSuggestionGateway().GenerateAsync([document]));

        Assert.Contains("Sentença", suggestion.SuggestedName);
        Assert.DoesNotContain("Matrícula do imóvel", suggestion.SuggestedName);
        Assert.Equal(StandardFolders.Litigation, suggestion.DestinationFolder);
    }

    [Fact]
    public async Task LocalSuggestion_ContentRepairsLegacyContractMisclassifiedAsRegistry()
    {
        var text = """
            CONTRATO DE COMPROMISSO DE VENDA E COMPRA firmado em 27/02/2023
            PROMITENTE VENDEDOR (ES): WILSON GUIMARAES DA SILVA
            PROMITENTE COMPRADOR (ES) SONIA MARIA STABELIN
            """;
        var document = new DocumentAnalysis(
            new FileItem("C:\\dossie\\Matrícula do imóvel - 27-02-2023.pdf", 100, ".pdf"),
            "HASH", text, TextExtractionMethod.PdfText, [], ["27/02/2023"]);

        var suggestion = Assert.Single(await new ContentAwareLocalSuggestionGateway().GenerateAsync([document]));

        Assert.Contains("Contrato de compra e venda", suggestion.SuggestedName);
        Assert.Contains("Wilson x Sonia", suggestion.SuggestedName);
        Assert.Equal(StandardFolders.Contracts, suggestion.DestinationFolder);
    }

    [Fact]
    public async Task LocalSuggestion_IptuReceiptReplacesLegacyHashReferenceWithBarcodeId()
    {
        var text = """
            Comprovante de Pagamento Boleto de Cobrança Data: 03/03/2023
            Número de Identificação: 23790.27200 90155.733703 32015.810008 8 92920000058377
            Descrição do Pagamento: Honorários ADV do IPTU Diadema
            """;
        var document = new DocumentAnalysis(
            new FileItem("C:\\dossie\\Comprovante de iptu - ref 48816dae.pdf", 100, ".pdf"),
            "HASH", text, TextExtractionMethod.PdfText, [], ["03/03/2023"]);

        var suggestion = Assert.Single(await new ContentAwareLocalSuggestionGateway().GenerateAsync([document]));

        Assert.Contains("ID 058377", suggestion.SuggestedName);
        Assert.DoesNotContain("ref ", suggestion.SuggestedName, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LocalSuggestion_RemovesLegacyCopyMarkersAndRepeatedDate()
    {
        var document = new DocumentAnalysis(
            new FileItem("C:\\dossie\\Cópia de Documento do arrematante - 24-06-2013 (2).pdf", 100, ".pdf"),
            "HASH", "Informações particulares emitidas em 24/06/2013.", TextExtractionMethod.PdfText,
            [], ["24/06/2013"]);

        var suggestion = Assert.Single(await new ContentAwareLocalSuggestionGateway().GenerateAsync([document]));

        Assert.DoesNotContain("Cópia", suggestion.SuggestedName, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("(2)", suggestion.SuggestedName, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith("2013-06-24 - ", suggestion.SuggestedName);
    }

    [Fact]
    public async Task LocalSuggestion_RecognizesRgInsteadOfKeepingGenericDocumentName()
    {
        var document = new DocumentAnalysis(
            new FileItem("C:\\dossie\\Cópia de Documento do arrematante.pdf", 100, ".pdf"),
            "HASH", "REPÚBLICA FEDERATIVA DO BRASIL CARTEIRA DE IDENTIDADE REGISTRO GERAL 12.345.678-9 SECRETARIA DE SEGURANÇA PÚBLICA",
            TextExtractionMethod.Ocr, [], []);

        var suggestion = Assert.Single(await new ContentAwareLocalSuggestionGateway().GenerateAsync([document]));

        Assert.StartsWith("RG do arrematante", suggestion.SuggestedName, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Cópia", suggestion.SuggestedName, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(StandardFolders.Research, suggestion.DestinationFolder);
    }

    [Fact]
    public async Task LocalSuggestion_QuarantinesUnreliableBlackAndWhiteOcr_InsteadOfGuessingFromWrongName()
    {
        var document = new DocumentAnalysis(
            new FileItem("C:\\dossie\\Documento do caminhão.pdf", 8_192, ".pdf"),
            "HASH", "w1l s0n xx 11 ll imagem manchada sem cabecalho reconhecivel",
            TextExtractionMethod.Ocr, [], []);

        var suggestion = Assert.Single(await new ContentAwareLocalSuggestionGateway().GenerateAsync([document]));

        Assert.Equal(StandardFolders.QualityReview, suggestion.DestinationFolder);
        Assert.Equal("REVISAO-QUALIDADE-OCR", suggestion.ClassificationRule);
        Assert.Equal("Documento do caminhão.pdf", suggestion.SuggestedName);
        Assert.Contains("não forneceu evidência suficiente", suggestion.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("revisão obrigatória", suggestion.QualityStatus, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LocalSuggestion_DoesNotTurnPleadingIntoRgBecausePartyQualificationMentionsRg()
    {
        var text = """
            EXCELENTÍSSIMO SENHOR DOUTOR JUIZ DE DIREITO
            PROCESSO 0000005-00.2026.8.00.0005
            A parte autora, portadora do RG 12.345.678-9 e CPF 123.456.789-00,
            ajuíza AÇÃO DE OBRIGAÇÃO DE FAZER e requer a tutela jurisdicional.
            O histórico menciona débitos de IPTU apenas como fundamento do pedido.
            """;
        var document = new DocumentAnalysis(
            new FileItem("C:\\dossie\\2 OBRIGAÇÃO DE FAZER PLACA CTB 6041 - Copia.docx", 100, ".docx"),
            "HASH", text, TextExtractionMethod.OfficeOpenXml,
            ["0000005-00.2026.8.00.0005"], []);

        var suggestion = Assert.Single(await new ContentAwareLocalSuggestionGateway().GenerateAsync([document]));

        Assert.Contains("Petição - obrigação de fazer", suggestion.SuggestedName, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("RG", suggestion.SuggestedName, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(StandardFolders.Litigation, suggestion.DestinationFolder);
    }

    [Fact]
    public async Task LocalSuggestion_ReportsOfficialNamingProtocolWithoutInventingMissingDate()
    {
        var document = new DocumentAnalysis(
            new FileItem("C:\\dossie\\entrada.pdf", 100, ".pdf"), "HASH",
            "PODER JUDICIÁRIO SENTENÇA processo 0000002-00.2026.5.00.0002",
            TextExtractionMethod.PdfText, ["0000002-00.2026.5.00.0002"], []);

        var suggestion = Assert.Single(await new ContentAwareLocalSuggestionGateway().GenerateAsync([document]));

        Assert.Contains("Sentença", suggestion.SuggestedName);
        Assert.Contains("proc 0000002-00", suggestion.SuggestedName);
        Assert.DoesNotMatch(@"^(?:19|20)\d{2}-\d{2}-\d{2}", suggestion.SuggestedName);
        Assert.Contains("data não localizada e não inventada", suggestion.NamingProtocolStatus,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LocalSuggestion_PrioritizesExplicitReportOverIncidentalAuctionText()
    {
        var document = new DocumentAnalysis(
            new FileItem("C:\\dossie\\Relatório.docx", 100, ".docx"), "HASH",
            "Relatório consolidado sobre a carta de arrematação e o histórico do bem.",
            TextExtractionMethod.OfficeOpenXml, [], []);

        var suggestion = Assert.Single(await new ContentAwareLocalSuggestionGateway().GenerateAsync([document]));

        Assert.Equal(StandardFolders.Reports, suggestion.DestinationFolder);
    }

    [Fact]
    public async Task LocalSuggestion_CartaDeArrematacao_IsNotTurnedIntoSentenceByNarrativeReference()
    {
        var text = """
            PODER JUDICIÁRIO FEDERAL
            JUSTIÇA DO TRABALHO
            Carta de Arrematação 14/2015
            Passada em favor do arrematante no processo 0000006-00.2026.5.00.0006.
            O reclamado foi condenado por sentença anterior, tendo sido levado à praça o veículo.
            """;
        var document = new DocumentAnalysis(
            new FileItem("C:\\dossie\\Carta de arrematação + Auto de arrematação.pdf", 100, ".pdf"),
            "HASH", text, TextExtractionMethod.PdfText, ["0000006-00.2026.5.00.0006"], ["21/05/2015"]);

        var suggestion = Assert.Single(await new ContentAwareLocalSuggestionGateway().GenerateAsync([document]));

        Assert.Contains("Carta de arrematação", suggestion.SuggestedName, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Sentença", suggestion.SuggestedName, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(StandardFolders.AuctionAndJudicial, suggestion.DestinationFolder);
    }

    [Fact]
    public async Task LocalSuggestion_SenatranConsultation_IsNotTurnedIntoRegistryCertificateByDisclaimer()
    {
        var text = """
            Portal de Serviços SENATRAN
            Consultar Veículo
            Atenção: as informações desta consulta não servem como certidão de regularidade.
            Código Renavam 00000000000 Placa DEF4G56
            """;
        var document = new DocumentAnalysis(
            new FileItem("C:\\dossie\\PLACA CTB 6041 Portal de Serviços SENATRAN.pdf", 100, ".pdf"),
            "HASH", text, TextExtractionMethod.PdfText, [], []);

        var suggestion = Assert.Single(await new ContentAwareLocalSuggestionGateway().GenerateAsync([document]));

        Assert.Contains("Consulta de veículo", suggestion.SuggestedName, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Certidão", suggestion.SuggestedName, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LocalSuggestion_UsucapiaoRequest_IsNotTurnedIntoAttachedAuctionDocument()
    {
        var text = """
            REQUERIMENTO PEDIDO DE USUCAPIÃO EXTRAJUDICIAL DE BEM MÓVEL
            O requerente adquiriu o caminhão e informa que a carta e o auto de arrematação seguem anexos.
            Requer seja declarado o usucapião extrajudicial do veículo placa DEF4G56.
            """;
        var document = new DocumentAnalysis(
            new FileItem("C:\\dossie\\Requerimento pedido de usucapição extrajudicial.docx", 100, ".docx"),
            "HASH", text, TextExtractionMethod.OfficeOpenXml, [], []);

        var suggestion = Assert.Single(await new ContentAwareLocalSuggestionGateway().GenerateAsync([document]));

        Assert.Contains("Requerimento de usucapião extrajudicial", suggestion.SuggestedName,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Auto de arrematação", suggestion.SuggestedName, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(StandardFolders.Litigation, suggestion.DestinationFolder);
    }

    [Fact]
    public void OfficialNaming_DoesNotRewritePetitionAsRenajudOfficeFromIncidentalWords()
    {
        var text = """
            EXCELENTÍSSIMO SENHOR JUIZ DE DIREITO
            PETIÇÃO
            Requer a baixa da alienação fiduciária. O histórico menciona restrição RENAJUD.
            """;
        var analysis = new DocumentAnalysis(
            new FileItem("C:\\dossie\\petição baixa alienação.docx", 100, ".docx"), "HASH", text,
            TextExtractionMethod.OfficeOpenXml, ["0000007-00.2026.8.00.0007"], []);
        var suggestion = new RenameSuggestion(analysis.File.FullPath, "Petição judicial.docx", true,
            "teste", StandardFolders.Litigation, "JUR-PECA-CONTEUDO", "Petição judicial");

        var result = Assert.Single(OfficialFileNamingProtocol.Apply([suggestion], [analysis]));

        Assert.Contains("Petição judicial", result.SuggestedName, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Ofício de baixa Renajud", result.SuggestedName, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("DÉBITOS VINCULADOS AO VEÍCULO Data da consulta. Renavam 00000000000 Placa DEF4G56. O texto informa que não é certidão.", "Demonstrativo de débitos")]
    [InlineData("OBSERVAÇÕES IMPORTANTES IPVA Apurado R$ 0,00 Débito do exercício atual. DADOS DO VEÍCULO Renavam 00000000000", "Demonstrativo de IPVA")]
    [InlineData("PODER JUDICIÁRIO INT/CIT.Nº 1008/2015. Fica V. Sa. NOTIFICADO quanto aos termos da decisão proferida.", "Notificação judicial")]
    [InlineData("Ofício nº 1521/2014. Diante do ofício recebido, vem informar que o contrato está em dia.", "Resposta a ofício judicial")]
    public async Task LocalSuggestion_UsesPrimaryHeaderInsteadOfIncidentalLegalWords(
        string text, string expectedType)
    {
        var document = new DocumentAnalysis(
            new FileItem("C:\\dossie\\entrada.pdf", 100, ".pdf"), "HASH", text,
            TextExtractionMethod.PdfText, [], []);

        var suggestion = Assert.Single(await new ContentAwareLocalSuggestionGateway().GenerateAsync([document]));

        Assert.Contains(expectedType, suggestion.SuggestedName, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RenameService_RepairsNonCanonicalFolderFromAnySuggestionGateway()
    {
        var document = new DocumentAnalysis(
            new FileItem("C:\\dossie\\arquivo.pdf", 100, ".pdf"), "HASH", "texto",
            TextExtractionMethod.PdfText, [], []);
        var service = new RenameService(new PhysicalFileSystem(), new NonCanonicalFolderGateway(),
            new DocumentAnalysisService(new Sha256HashCalculator(new PhysicalFileSystem()), new FixedTextExtractor("texto")),
            new PathGuard(), new JsonHistoryStore(Path.GetTempPath()), new EmptyFolderCleaner(new PhysicalFileSystem()));

        var suggestion = Assert.Single(await service.GenerateNamesFromAnalysesAsync([document], true));

        Assert.Equal(StandardFolders.Research, suggestion.DestinationFolder);
        Assert.Contains("Pasta não padronizada corrigida", suggestion.Reason);
    }

    [Fact]
    public async Task DeepExtractor_ReadsDocxParagraphsTextToText()
    {
        using var directory = new TemporaryDirectory();
        var path = directory.PathFor("peticao.docx");
        await using (var stream = new FileStream(path, FileMode.CreateNew))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("word/document.xml");
            await using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
            await writer.WriteAsync("""
                <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                  <w:body><w:p><w:r><w:t>Contestação integral</w:t></w:r></w:p>
                  <w:p><w:r><w:t>Contrarrazões e prejuízo de R$ 2.500,00</w:t></w:r></w:p></w:body>
                </w:document>
                """);
        }

        var extracted = await new DeepDocumentTextExtractor(new PdfFirstPageTextExtractor())
            .ExtractAsync(path);

        Assert.Equal(TextExtractionMethod.OfficeOpenXml, extracted.Method);
        Assert.Contains("Contestação integral", extracted.Text);
        Assert.Contains("R$ 2.500,00", extracted.Text);
        Assert.True(extracted.IsComplete);
    }

    private sealed class FixedTextExtractor(string text) : IFirstPageTextExtractor
    {
        public Task<ExtractedText> ExtractAsync(string filePath, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ExtractedText(text, TextExtractionMethod.PdfText));
    }

    private sealed class PathBasedTextExtractor : IFirstPageTextExtractor
    {
        public Task<ExtractedText> ExtractAsync(string filePath, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ExtractedText($"Conteúdo interno exclusivo de {Path.GetFileName(filePath)}", TextExtractionMethod.PdfText));
    }

    private sealed class SameOpeningTextExtractor : IFirstPageTextExtractor
    {
        public Task<ExtractedText> ExtractAsync(string filePath, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ExtractedText("mesmas palavras iniciais para todos", TextExtractionMethod.PdfText));
    }

    private sealed class ConcurrencyTrackingExtractor : IFirstPageTextExtractor
    {
        private int _active;
        private int _maximum;
        public int MaximumConcurrency => _maximum;

        public async Task<ExtractedText> ExtractAsync(string filePath, CancellationToken cancellationToken = default)
        {
            var active = Interlocked.Increment(ref _active);
            int observed;
            do
            {
                observed = _maximum;
                if (active <= observed) break;
            } while (Interlocked.CompareExchange(ref _maximum, active, observed) != observed);
            try
            {
                await Task.Delay(40, cancellationToken);
                return new(Path.GetFileName(filePath), TextExtractionMethod.PlainText);
            }
            finally
            {
                Interlocked.Decrement(ref _active);
            }
        }
    }

    private sealed class SameGenericNameGateway : IContentSuggestionGateway
    {
        public Task<IReadOnlyList<RenameSuggestion>> GenerateAsync(
            IReadOnlyList<DocumentAnalysis> documents,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<RenameSuggestion>>(documents.Select(document =>
                new RenameSuggestion(document.File.FullPath, "Relatório.pdf", false,
                    "Sugestão repetida de teste.", DocumentType: "Relatório")).ToArray());
    }

    private sealed class NonCanonicalFolderGateway : IContentSuggestionGateway
    {
        public Task<IReadOnlyList<RenameSuggestion>> GenerateAsync(
            IReadOnlyList<DocumentAnalysis> documents,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<RenameSuggestion>>(documents.Select(document =>
                new RenameSuggestion(document.File.FullPath, "Relatório.pdf", true,
                    "Sugestão externa.", "Pasta qualquer", DocumentType: "Relatório")).ToArray());
    }

    private static byte[] CreateMinimalTextPdf(string text)
    {
        var escaped = text.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("(", "\\(", StringComparison.Ordinal)
            .Replace(")", "\\)", StringComparison.Ordinal);
        var content = $"BT /F1 12 Tf 72 720 Td ({escaped}) Tj ET";
        var objects = new[]
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 5 0 R >> >> /Contents 4 0 R >>",
            $"<< /Length {Encoding.ASCII.GetByteCount(content)} >>\nstream\n{content}\nendstream",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>"
        };
        using var stream = new MemoryStream();
        void Write(string value)
        {
            var bytes = Encoding.ASCII.GetBytes(value);
            stream.Write(bytes, 0, bytes.Length);
        }

        Write("%PDF-1.4\n");
        var offsets = new List<long> { 0 };
        for (var index = 0; index < objects.Length; index++)
        {
            offsets.Add(stream.Position);
            Write($"{index + 1} 0 obj\n{objects[index]}\nendobj\n");
        }

        var xrefPosition = stream.Position;
        Write($"xref\n0 {objects.Length + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets.Skip(1)) Write($"{offset:0000000000} 00000 n \n");
        Write($"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R >>\nstartxref\n{xrefPosition}\n%%EOF\n");
        return stream.ToArray();
    }
}
