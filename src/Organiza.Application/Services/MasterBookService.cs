using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Encodings.Web;
using Organiza.Application.Abstractions;
using Organiza.Application.Common;
using Organiza.Domain.Files;
using Organiza.Domain.MasterBook;
using Organiza.Domain.Operations;
using Organiza.Domain.Organization;

namespace Organiza.Application.Services;

public sealed partial class MasterBookService(
    IFileSystem fileSystem,
    DocumentAnalysisService documentAnalysis,
    PathGuard pathGuard,
    IHistoryStore history)
{
    private static readonly UTF8Encoding Utf8WithBom = new(
        encoderShouldEmitUTF8Identifier: true,
        throwOnInvalidBytes: true);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task<MasterBookResult> GenerateAsync(
        string selectedRoot,
        ExplicitApproval approval,
        IProgress<MasterBookProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!approval.Granted) throw new ApprovalRequiredException("gerar Livro Mestre 360°");
        var root = Path.GetFullPath(selectedRoot);
        if (!fileSystem.DirectoryExists(root)) throw new DirectoryNotFoundException(root);

        var markdownPath = Path.Combine(root, MasterBookFileNames.Markdown);
        var jsonPath = Path.Combine(root, MasterBookFileNames.Json);
        pathGuard.EnsureAllowed(markdownPath);
        pathGuard.EnsureAllowed(jsonPath);

        var files = fileSystem.EnumerateFiles(root, SearchOption.AllDirectories)
            .Where(path => !IsGeneratedOrganizaFile(path) &&
                           !WorkspaceFilePolicy.IsGoogleWorkspacePointer(path) &&
                           !WorkspaceFilePolicy.IsInsideInternalFolder(root, path) &&
                           !WorkspaceFilePolicy.IsInsideDuplicatesFolder(root, path) &&
                           !WorkspaceFilePolicy.IsGeneratedPdfPart(path))
            .Select(path => new FileItem(path, fileSystem.GetFileLength(path), Path.GetExtension(path)))
            .OrderBy(file => file.FullPath, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        progress?.Report(new(0, files.Length, "Iniciando varredura documental 360°..."));
        var analyses = await documentAnalysis.AnalyzeAsync(files, cancellationToken, progress);
        progress?.Report(new(files.Length, files.Length, "Consolidando matrizes, riscos e plano de ação..."));

        var sources = analyses.Select(analysis => new MasterSourceDocument(
            Path.GetRelativePath(root, analysis.File.FullPath),
            analysis.File.SizeBytes,
            analysis.ContentSha256,
            analysis.ExtractionMethod,
            analysis.ProcessIdentifiers,
            analysis.DocumentDates,
            analysis.FullText,
            analysis.PageCount,
            analysis.PagesWithText,
            analysis.OcrPages,
            analysis.ExtractionComplete,
            analysis.ExtractionWarnings ?? [])).ToArray();
        var processes = SelectDossierProcesses(root, analyses);
        var financialInventory = ExtractFinancialInventory(root, analyses);
        var sections = BuildSections(root, analyses, sources, processes, financialInventory);
        var financialConsolidation = BuildFinancialConsolidation(financialInventory);
        var thesisMatrix = BuildThesisMatrix(root, analyses);
        var riskMatrix = BuildRiskMatrix(root, analyses, financialInventory);
        var actionPlan = BuildStrategicActionPlan(root, analyses, financialInventory);
        var propertyRegistry = BuildPropertyRegistryAnalysis(root, analyses);
        var registryAuctionChronology = BuildRegistryAuctionChronology(root, analyses, propertyRegistry);
        var paymentReconciliation = BuildPaymentReconciliation(root, analyses);
        var bailiffDiligences = BuildBailiffDiligences(root, analyses);
        var report = new MasterBookReport(
            "3.0",
            DateTimeOffset.Now,
            Path.GetFileName(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
            root,
            true,
            sources.Length,
            processes,
            sources,
            financialInventory,
            BuildExecutiveSynthesis(root, analyses, sources.Length, processes.Length,
                financialConsolidation, actionPlan),
            financialConsolidation,
            thesisMatrix,
            riskMatrix,
            actionPlan,
            propertyRegistry,
            registryAuctionChronology,
            paymentReconciliation,
            bailiffDiligences,
            sections,
            "Relatório estruturado baseado na leitura integral disponível. Inferências jurídicas, falhas profissionais, causalidade, prazos, responsabilidades e estratégias exigem validação humana qualificada.");

        var json = JsonSerializer.Serialize(report, JsonOptions);
        var markdown = RenderAbntMarkdown(report);
        var jsonBytes = EncodeUtf8WithBom(json);
        WriteAtomically(root, jsonPath, jsonBytes);
        MasterBookAnalysisCache.SaveLocalCopy(fileSystem, root, jsonBytes);
        var actualMarkdownPath = WriteMarkdownWithFallback(root,
            EncodeUtf8WithBom(NormalizeMarkdownForEditors(markdown)));

        await history.AppendAsync(
        [
            new(DateTimeOffset.Now, "Gerar Livro Mestre 360°", root, actualMarkdownPath, OperationStatus.Completed,
                $"{sources.Length} documentos; {processes.Length} processos; {registryAuctionChronology.Count} eventos registrais/arrematação; {paymentReconciliation.Count} conciliações; {bailiffDiligences.Count} diligências"),
            new(DateTimeOffset.Now, "Gerar base mestre estruturada", root, jsonPath, OperationStatus.Completed,
                "JSON local para consultas futuras de IA")
        ], cancellationToken);
        progress?.Report(new(files.Length, files.Length, "Livro Mestre 360° concluído."));
        return new(actualMarkdownPath, jsonPath, sources.Length, processes.Length);
    }

    public async Task<string> RenderFromStructuredBaseAsync(
        string selectedRoot,
        ExplicitApproval approval,
        CancellationToken cancellationToken = default)
    {
        if (!approval.Granted) throw new ApprovalRequiredException("recriar o relatório do Livro Mestre 360°");
        var root = Path.GetFullPath(selectedRoot);
        var jsonPath = Path.Combine(root, MasterBookFileNames.Json);
        if (!fileSystem.FileExists(jsonPath)) throw new FileNotFoundException("Base estruturada não encontrada.", jsonPath);
        MasterBookReport report;
        await using (var stream = fileSystem.OpenRead(jsonPath))
            report = await JsonSerializer.DeserializeAsync<MasterBookReport>(stream, JsonOptions, cancellationToken)
                     ?? throw new InvalidDataException("A base estruturada do Livro Mestre está vazia ou inválida.");
        var bytes = EncodeUtf8WithBom(NormalizeMarkdownForEditors(RenderAbntMarkdown(report)));
        var destination = WriteMarkdownWithFallback(root, bytes);
        await history.AppendAsync([
            new(DateTimeOffset.Now, "Renderizar Livro Mestre 360°", jsonPath, destination,
                OperationStatus.Completed, "Relatório jurídico-documental reconstruído da base JSON 3.0")
        ], cancellationToken);
        return destination;
    }

    public async Task<MasterBookResult> RefreshDerivedAnalysisFromStructuredBaseAsync(
        string selectedRoot,
        ExplicitApproval approval,
        CancellationToken cancellationToken = default)
    {
        if (!approval.Granted) throw new ApprovalRequiredException("atualizar as correlações do Livro Mestre 360°");
        var root = Path.GetFullPath(selectedRoot);
        var jsonPath = Path.Combine(root, MasterBookFileNames.Json);
        if (!fileSystem.FileExists(jsonPath)) throw new FileNotFoundException("Base estruturada não encontrada.", jsonPath);
        MasterBookReport report;
        await using (var stream = fileSystem.OpenRead(jsonPath))
            report = await JsonSerializer.DeserializeAsync<MasterBookReport>(stream, JsonOptions, cancellationToken)
                     ?? throw new InvalidDataException("A base estruturada do Livro Mestre está vazia ou inválida.");
        var analyses = report.SourceDocuments.Select(source => new DocumentAnalysis(
            new FileItem(Path.Combine(root, source.RelativePath), source.SizeBytes, Path.GetExtension(source.RelativePath)),
            source.Sha256,
            source.ExtractedText,
            source.ExtractionMethod,
            source.ProcessIdentifiers,
            source.DocumentDates,
            source.PageCount,
            source.PagesWithText,
            source.OcrPages,
            source.ExtractionComplete,
            source.ExtractionWarnings)).ToArray();
        var registry = BuildPropertyRegistryAnalysis(root, analyses);
        report = report with
        {
            SchemaVersion = "3.0",
            GeneratedAt = DateTimeOffset.Now,
            PropertyRegistryAnalysis = registry,
            RegistryAuctionChronology = BuildRegistryAuctionChronology(root, analyses, registry),
            PaymentReconciliation = BuildPaymentReconciliation(root, analyses),
            BailiffDiligences = BuildBailiffDiligences(root, analyses)
        };
        var jsonBytes = EncodeUtf8WithBom(JsonSerializer.Serialize(report, JsonOptions));
        WriteAtomically(root, jsonPath, jsonBytes);
        MasterBookAnalysisCache.SaveLocalCopy(fileSystem, root, jsonBytes);
        var markdownPath = WriteMarkdownWithFallback(root,
            EncodeUtf8WithBom(NormalizeMarkdownForEditors(RenderAbntMarkdown(report))));
        await history.AppendAsync([
            new(DateTimeOffset.Now, "Atualizar correlações do Livro Mestre 360°", jsonPath, markdownPath,
                OperationStatus.Completed,
                $"{report.RegistryAuctionChronology.Count} eventos; {report.PaymentReconciliation.Count} conciliações; {report.BailiffDiligences.Count} diligências")
        ], cancellationToken);
        return new(markdownPath, jsonPath, report.DocumentsAnalyzed, report.IdentifiedProcesses.Count);
    }

    private string WriteMarkdownWithFallback(string root, byte[] content)
    {
        var primary = Path.Combine(root, MasterBookFileNames.Markdown);
        try
        {
            WriteAtomically(root, primary, content);
            var previousFallback = Path.Combine(root, MasterBookFileNames.UpdatedMarkdown);
            if (fileSystem.FileExists(previousFallback))
            {
                try { WriteAtomically(root, previousFallback, content); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            return primary;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            var updated = Path.Combine(root, MasterBookFileNames.UpdatedMarkdown);
            pathGuard.EnsureAllowed(updated);
            WriteAtomically(root, updated, content);
            return updated;
        }
    }

    private void WriteAtomically(string root, string destination, byte[] content)
    {
        var internalDirectory = Path.Combine(root, WorkspaceFilePolicy.InternalDirectoryName);
        fileSystem.CreateDirectory(internalDirectory);
        var temporary = Path.Combine(internalDirectory, $"write-{Guid.NewGuid():N}.tmp");
        try
        {
            fileSystem.WriteAllBytes(temporary, content);
            fileSystem.ReplaceFile(temporary, destination);
        }
        finally
        {
            if (fileSystem.FileExists(temporary))
            {
                try { fileSystem.DeleteFile(temporary); } catch { }
            }
        }
    }

    private static IReadOnlyList<MasterBookSection> BuildSections(
        string root,
        IReadOnlyList<DocumentAnalysis> analyses,
        IReadOnlyList<MasterSourceDocument> sources,
        IReadOnlyList<string> processes,
        IReadOnlyList<MasterFinancialEntry> financialInventory)
    {
        var identification = processes.Select(process => new MasterBookFinding(
            $"Processo identificado: {process}",
            sources.Where(source => source.ProcessIdentifiers.Contains(process, StringComparer.OrdinalIgnoreCase))
                .Select(source => source.RelativePath).ToArray(), "alta",
            sources.Where(source => source.ProcessIdentifiers.Contains(process, StringComparer.OrdinalIgnoreCase))
                .Select(source => new MasterEvidence(source.RelativePath, process)).ToArray())).ToArray();

        MasterBookSection Section(int number, string title, string[] keywords, params string[] pending)
        {
            var evidence = FindEvidence(root, analyses, keywords);
            var findings = evidence.GroupBy(item => item.RelativePath, StringComparer.OrdinalIgnoreCase)
                .Select(group => new MasterBookFinding(
                    BuildAnalyticalDescription(title, group),
                    [group.Key], "documental - requer interpretação humana", group.ToArray()))
                .ToArray();
            return new(number, title,
                evidence.Count == 0 ? "Nenhuma evidência textual localizada; revisão necessária" : "Evidências extraídas do conteúdo integral",
                findings, pending);
        }

        var formats = sources.Select(source => Path.GetExtension(source.RelativePath))
            .Where(extension => !string.IsNullOrWhiteSpace(extension))
            .GroupBy(extension => extension, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count())
            .Select(group => $"{group.Key.ToLowerInvariant()} ({group.Count()})")
            .ToArray();
        var documentaryFindings = new[]
        {
            new MasterBookFinding(
                $"Foram inventariados {sources.Count} documentos. Formatos encontrados: {(formats.Length == 0 ? "não identificados" : string.Join(", ", formats))}.",
                [], "alta")
        };
        var financialFindings = financialInventory.GroupBy(entry => entry.Category, StringComparer.OrdinalIgnoreCase)
            .Select(group => new MasterBookFinding(
                $"{group.Count()} lançamento(s) de {group.Key}; total monetário reconhecido: " +
                $"{group.Where(item => item.Amount.HasValue).Sum(item => item.Amount ?? 0):C}.",
                group.Select(item => item.RelativePath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                "extração financeira - conferir valor, competência e duplicidade",
                group.Select(item => new MasterEvidence(item.RelativePath, item.Evidence)).ToArray()))
            .ToArray();

        return
        [
            new(1, "Identificação dos processos analisados",
                identification.Length == 0 ? "Nenhum número processual reconhecido" : "Identificação estruturada concluída",
                identification, ["Confirmar área, situação e papel do cliente em cada processo."]),
            Section(2, "Fase de conhecimento e evolução processual", ["petição inicial", "contestação", "réplica", "instrução", "audiência", "fase de conhecimento", "sentença"],
                "Confirmar cronologia, pedidos, defesas, provas e situação atual de cada processo."),
            Section(3, "Leilão, arrematação, parcelamentos e cancelamentos", ["arrematação", "leilão", "edital", "auto de arrematação", "parcelamento", "preço vil", "cancelamento"],
                "Validar bem, datas, preço, parcelas, pagamentos, nulidades e efeitos patrimoniais."),
            Section(4, "Decisões interlocutórias, sentenças e cumprimento", ["decisão interlocutória", "decisão", "sentença", "despacho", "cumprimento de sentença", "execução"],
                "Conferir dispositivo, fundamentos, intimação, trânsito e obrigações resultantes."),
            Section(5, "Recursos, razões e contrarrazões", ["recurso", "apelação", "agravo", "embargos", "razões", "contrarrazões", "contraminuta"],
                "Mapear tempestividade, pedido recursal, resposta, julgamento e efeitos."),
            Section(6, "Atuação dos patronos anteriores", ["advogado", "patrono", "perda de prazo", "intempest", "revelia", "preclusão", "não apresentou", "ausência de prova"],
                "Tratar ocorrências como indícios; não atribuir erro profissional ou responsabilidade sem auditoria humana e contraditório."),
            Section(7, "Prejuízos financeiros e processuais", ["prejuízo", "dano", "multa", "juros", "honorários", "custas", "bloqueio", "penhora", "perda", "condenação"],
                "Distinguir valor alegado, comprovado, atualizado e nexo causal."),
            new(8, "Inventário financeiro detalhado",
                financialInventory.Count == 0 ? "Nenhum lançamento financeiro estruturado reconhecido" : "Lançamentos extraídos do conteúdo integral",
                financialFindings, ["Conferir duplicidades, competências, vencimentos, pagamentos, saldos, juros e atualização monetária."]),
            Section(9, "Matriz de tese jurídica", ["tese", "fundamento", "nulidade", "prescrição", "decadência", "ilegitimidade", "incompetência"],
                "Submeter teses, fundamentos, contraprovas e probabilidade de sucesso à revisão jurídica humana."),
            Section(10, "Matriz de risco processual", ["risco", "revelia", "preclusão", "penhora", "bloqueio", "prazo", "sucumbência", "improcedência"],
                "Classificar probabilidade, impacto, urgência e estratégia de mitigação."),
            new(11, "Plano de ação, lacunas e controle documental", "Inventário documental concluído",
                documentaryFindings.Concat(FindingsFor(root, analyses,
                    ["providência", "próximo passo", "prazo", "protocolar", "solicitar", "pendente"])).ToArray(),
                ["Definir ações imediatas, de médio prazo e estratégicas, com responsáveis, documentos faltantes e prazos."]),
            Section(12, "Resumo executivo para o cliente", ["resumo", "estratégia", "recomendação", "conclusão"],
                "Validar linguagem, riscos, valores e estratégia antes de apresentar ao cliente.")
        ];
    }

    private static string BuildAnalyticalDescription(string title, IEnumerable<MasterEvidence> evidence)
    {
        var items = evidence.ToArray();
        var synthesis = SummarizeExcerpt(items[0].Excerpt, 460);
        return $"Na análise de {title.ToLowerInvariant()}, o documento registra o seguinte elemento relevante: {synthesis} " +
               $"A ocorrência possui {items.Length} trecho(s) correlato(s) e deve ser confrontada com a cronologia e o inteiro teor.";
    }

    private static MasterExecutiveSynthesis BuildExecutiveSynthesis(
        string root,
        IReadOnlyList<DocumentAnalysis> analyses,
        int documentCount,
        int processCount,
        IReadOnlyList<MasterFinancialConsolidation> financial,
        IReadOnlyList<MasterStrategicAction> actions)
    {
        var auction = FindEvidence(root, analyses,
            ["arrematação", "leilão", "edital", "carta de arrematação", "veículo", "imóvel", "registro"]);
        var procedure = FindEvidence(root, analyses,
            ["petição inicial", "contestação", "sentença", "decisão", "recurso", "cumprimento", "execução"]);
        var counsel = FindEvidence(root, analyses,
            ["perda de prazo", "intempest", "revelia", "preclusão", "não apresentou", "ausência de prova", "patrono"]);
        var recognizedTotal = financial.Sum(item => item.RecognizedTotal);
        var financialCategories = financial.Count == 0
            ? "nenhuma categoria monetária consolidada automaticamente"
            : string.Join(", ", financial.Select(item => item.Category));

        return new(
            $"O dossiê reúne {documentCount} documento(s) e {processCount} identificador(es) processual(is). A leitura integral foi organizada por tema, cronologia, evidência financeira e repercussão estratégica; números de processo e caminhos completos foram deslocados para o apêndice técnico.",
            auction.Count == 0
                ? "Não houve evidência textual suficiente para descrever com segurança o bem e o leilão. É necessário conferir edital, auto/carta de arrematação, registro e comprovantes."
                : $"Foram encontrados {auction.Count} trecho(s) sobre bem, leilão ou arrematação. Síntese documental: {SummarizeExcerpt(auction[0].Excerpt, 520)}",
            procedure.Count == 0
                ? "A fase processual atual não pôde ser determinada automaticamente; deve-se conferir decisões mais recentes, intimações e eventuais recursos."
                : $"A documentação contém atos de conhecimento, decisão ou recurso. Elemento central localizado: {SummarizeExcerpt(procedure[0].Excerpt, 520)}",
            counsel.Count == 0
                ? "Nenhuma falha profissional pode ser afirmada automaticamente. A auditoria deve comparar prazos, intimações, peças apresentadas e provas disponíveis."
                : $"Há {counsel.Count} indício(s) textual(is) que exigem auditoria da atuação anterior, sem conclusão automática de responsabilidade. Exemplo: {SummarizeExcerpt(counsel[0].Excerpt, 460)}",
            financial.Count == 0
                ? "Nenhum valor foi consolidado com segurança. Conferir IPTU, PPI, condomínio, despesas mensais, custas e parcelas de arrematação."
                : $"Foram consolidadas {financial.Count} categoria(s): {financialCategories}. Soma nominal reconhecida nos documentos: {recognizedTotal:C}; o total não representa saldo atualizado e pode conter competências ou documentos sobrepostos.",
            actions.OrderBy(item => item.Priority).Take(5).Select(item => item.Action).ToArray());
    }

    private static IReadOnlyList<MasterFinancialConsolidation> BuildFinancialConsolidation(
        IReadOnlyList<MasterFinancialEntry> inventory) =>
        inventory.GroupBy(item => item.Category, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => new MasterFinancialConsolidation(
                group.Key,
                group.Count(),
                group.Select(item => item.RelativePath).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                group.Where(item => item.Amount.HasValue).Sum(item => item.Amount ?? 0),
                "Conferir competência, vencimento, pagamento, duplicidade e atualização monetária"))
            .ToArray();

    private static IReadOnlyList<MasterLegalThesis> BuildThesisMatrix(
        string root,
        IReadOnlyList<DocumentAnalysis> analyses)
    {
        var definitions = new[]
        {
            new { Name = "Regularidade do leilão e da arrematação", Keywords = new[] { "nulidade", "preço vil", "edital", "intimação", "cancelamento", "arrematação" }, Gap = "Conferir edital, intimações, preço, pagamento, auto/carta e decisão posterior." },
            new { Name = "Posse, propriedade, registro e legitimidade", Keywords = new[] { "propriedade", "posse", "usucapião", "registro", "transferência", "legitimidade" }, Gap = "Confrontar cadeia dominial, tradição, registro, restrições e posição processual das partes." },
            new { Name = "Prescrição, decadência e marcos temporais", Keywords = new[] { "prescrição", "decadência", "prazo prescricional", "termo inicial" }, Gap = "Montar linha do tempo e validar causas de interrupção, suspensão e ciência inequívoca." },
            new { Name = "Contraditório, defesa e validade das intimações", Keywords = new[] { "contraditório", "ampla defesa", "citação", "intimação", "revelia", "preclusão" }, Gap = "Conferir destinatários, datas, meios de publicação e oportunidade efetiva de manifestação." }
        };

        return definitions.Select(definition =>
            {
                var evidence = FindEvidence(root, analyses, definition.Keywords);
                if (evidence.Count == 0) return null;
                return new MasterLegalThesis(
                    definition.Name,
                    SummarizeExcerpt(evidence[0].Excerpt, 560),
                    definition.Gap,
                    evidence.Count >= 3 ? "documental recorrente — validação jurídica obrigatória" : "indiciária — documentação complementar necessária",
                    evidence.Select(item => item.RelativePath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
            })
            .Where(item => item is not null)
            .Cast<MasterLegalThesis>()
            .ToArray();
    }

    private static IReadOnlyList<MasterProceduralRisk> BuildRiskMatrix(
        string root,
        IReadOnlyList<DocumentAnalysis> analyses,
        IReadOnlyList<MasterFinancialEntry> financialInventory)
    {
        var result = new List<MasterProceduralRisk>();
        Add("Perda de prazo, revelia ou preclusão", ["prazo", "intempest", "revelia", "preclusão"],
            "alto", "imediata", "Auditar intimações, publicações, contagem e peças protocoladas.");
        Add("Constrição patrimonial, bloqueio ou penhora", ["penhora", "bloqueio", "restrição", "indisponibilidade"],
            "alto", "imediata", "Levantar ordens vigentes, bens atingidos, garantias e medidas de desbloqueio/substituição.");
        Add("Invalidação ou dificuldade de transferência do bem arrematado", ["cancelamento", "nulidade", "transferência", "registro", "restrição administrativa"],
            "alto", "alta", "Conferir título aquisitivo, decisão, carta/auto, registro e restrições administrativas ou judiciais.");
        if (financialInventory.Count > 0)
        {
            result.Add(new("Crescimento ou duplicidade do passivo financeiro", "a apurar", "médio/alto",
                "alta", "Conciliar cada lançamento por competência, credor, pagamento e atualização, separando principal, multa, juros e despesas.",
                financialInventory.Select(item => item.RelativePath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()));
        }

        return result;

        void Add(string risk, string[] keywords, string impact, string urgency, string mitigation)
        {
            var evidence = FindEvidence(root, analyses, keywords);
            if (evidence.Count == 0) return;
            result.Add(new(risk, evidence.Count >= 3 ? "média — sinais recorrentes" : "indiciária", impact,
                urgency, mitigation,
                evidence.Select(item => item.RelativePath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()));
        }
    }

    private static IReadOnlyList<MasterStrategicAction> BuildStrategicActionPlan(
        string root,
        IReadOnlyList<DocumentAnalysis> analyses,
        IReadOnlyList<MasterFinancialEntry> financialInventory)
    {
        var allDocuments = analyses.Select(item => Path.GetRelativePath(root, item.File.FullPath)).ToArray();
        var auction = FindEvidence(root, analyses, ["arrematação", "leilão", "edital", "carta de arrematação"]);
        var counsel = FindEvidence(root, analyses, ["prazo", "intempest", "revelia", "preclusão", "patrono"]);
        var actions = new List<MasterStrategicAction>
        {
            new(1, "Confirmar a situação processual atual e os próximos prazos",
                "O relatório localiza atos e referências, mas a situação vigente depende das decisões e intimações mais recentes.",
                "Cronologia validada, prazo crítico identificado e responsável definido.", allDocuments)
        };
        if (auction.Count > 0)
            actions.Add(new(2, "Auditar integralmente o leilão, a arrematação e a transferência do bem",
                "Há evidências documentais sobre arrematação/leilão que precisam ser confrontadas entre edital, pagamento, carta/auto e decisões.",
                "Mapa de validade do ato, obrigações pendentes e medidas corretivas possíveis.",
                auction.Select(item => item.RelativePath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()));
        if (financialInventory.Count > 0)
            actions.Add(new(3, "Conciliar o inventário financeiro por documento e competência",
                "Valores extraídos podem representar cobranças repetidas, parcelas, pagamentos ou saldos de datas diferentes.",
                "Planilha de principal, encargos, pagamentos, saldo e documento comprobatório.",
                financialInventory.Select(item => item.RelativePath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()));
        if (counsel.Count > 0)
            actions.Add(new(4, "Realizar auditoria documentada da atuação dos patronos anteriores",
                "Foram localizados termos ligados a prazos, preclusão, revelia ou atuação profissional; isso é indício, não conclusão.",
                "Quadro ato esperado × ato praticado × prazo × prova disponível × impacto possível.",
                counsel.Select(item => item.RelativePath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()));
        actions.Add(new(5, "Submeter teses e riscos à revisão jurídica humana",
            "A extração automatizada organiza evidências, mas não substitui interpretação jurídica, atualização processual nem contraditório.",
            "Estratégia aprovada com tese principal, alternativas, riscos, responsáveis e prazos.", allDocuments));
        return actions.OrderBy(item => item.Priority).ToArray();
    }

    private static string SummarizeExcerpt(string excerpt, int maximumLength)
    {
        var clean = CleanProse(excerpt.ReplaceLineEndings(" "));
        if (clean.Length > 160 && char.IsLower(clean[0]))
        {
            var sentenceStart = clean.IndexOfAny(['.', ';', ':'], 0, Math.Min(160, clean.Length));
            if (sentenceStart >= 0 && sentenceStart + 1 < clean.Length)
                clean = clean[(sentenceStart + 1)..].TrimStart();
        }
        if (clean.Length <= maximumLength) return clean;
        var cut = clean.LastIndexOfAny(['.', ';', ':'], maximumLength - 1, maximumLength);
        if (cut < maximumLength / 2)
        {
            cut = clean.LastIndexOf(' ', maximumLength - 1, maximumLength);
            if (cut < maximumLength / 2) cut = maximumLength;
        }
        return clean[..cut].TrimEnd(' ', ',', ';', ':') + "…";
    }

    private static IReadOnlyList<MasterEvidence> FindEvidence(
        string root,
        IReadOnlyList<DocumentAnalysis> analyses,
        IReadOnlyList<string> keywords)
    {
        var result = new List<MasterEvidence>();
        foreach (var analysis in analyses)
        {
            var relativePath = Path.GetRelativePath(root, analysis.File.FullPath);
            foreach (var keyword in keywords)
            {
                var start = 0;
                while (start < analysis.FullText.Length)
                {
                    var index = analysis.FullText.IndexOf(keyword, start, StringComparison.OrdinalIgnoreCase);
                    if (index < 0) break;
                    var excerptStart = Math.Max(0, index - 140);
                    var excerptLength = Math.Min(420, analysis.FullText.Length - excerptStart);
                    var excerpt = analysis.FullText.Substring(excerptStart, excerptLength).Trim();
                    result.Add(new(relativePath, excerpt));
                    start = index + keyword.Length;
                }
            }
        }

        return result.DistinctBy(item => $"{item.RelativePath}\0{item.Excerpt}", StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<MasterBookFinding> FindingsFor(
        string root,
        IReadOnlyList<DocumentAnalysis> analyses,
        IReadOnlyList<string> keywords)
    {
        return FindEvidence(root, analyses, keywords)
            .GroupBy(item => item.RelativePath, StringComparer.OrdinalIgnoreCase)
            .Select(group => new MasterBookFinding(
                $"{group.Count()} providência(s), prazo(s) ou pendência(s) documental(is) localizada(s).",
                [group.Key], "documental - confirmar atualidade", group.ToArray()))
            .ToArray();
    }

    private static IReadOnlyList<MasterFinancialEntry> ExtractFinancialInventory(
        string root,
        IReadOnlyList<DocumentAnalysis> analyses)
    {
        var amountRegex = new System.Text.RegularExpressions.Regex(
            @"R\$\s*(?<amount>\d{1,3}(?:\.\d{3})*,\d{2}|\d+,\d{2})",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase |
            System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        var dateRegex = new System.Text.RegularExpressions.Regex(
            @"\b(?:0?[1-9]|[12]\d|3[01])[/.-](?:0?[1-9]|1[0-2])[/.-](?:19|20)\d{2}\b");
        var result = new List<MasterFinancialEntry>();
        foreach (var analysis in analyses)
        {
            var relativePath = Path.GetRelativePath(root, analysis.File.FullPath);
            foreach (System.Text.RegularExpressions.Match match in amountRegex.Matches(analysis.FullText))
            {
                var start = Math.Max(0, match.Index - 180);
                var length = Math.Min(520, analysis.FullText.Length - start);
                var evidence = analysis.FullText.Substring(start, length).Trim();
                var amountText = match.Groups["amount"].Value;
                decimal? amount = decimal.TryParse(amountText,
                    System.Globalization.NumberStyles.Number,
                    System.Globalization.CultureInfo.GetCultureInfo("pt-BR"), out var parsed)
                    ? parsed
                    : null;
                var category = CategorizeFinancialEvidence(evidence);
                var date = dateRegex.Match(evidence).Value;
                result.Add(new(category, $"Valor reconhecido no conteúdo: {match.Value}", amount,
                    string.IsNullOrWhiteSpace(date) ? null : date, relativePath, evidence));
            }
        }

        return result.DistinctBy(item => $"{item.RelativePath}\0{item.Amount}\0{item.Evidence}",
            StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static MasterPropertyRegistryAnalysis BuildPropertyRegistryAnalysis(
        string root,
        IReadOnlyList<DocumentAnalysis> analyses)
    {
        var uniqueAnalyses = analyses
            .GroupBy(item => item.ContentSha256, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();
        var registryDocuments = uniqueAnalyses.Where(item =>
                ContainsAny(Path.GetFileNameWithoutExtension(item.File.Name), "matrícula", "matricula") ||
                (ContainsAny(item.FullText, "matrícula", "matricula") &&
                 ContainsAny(item.FullText, "registro de imóveis", "registro de imoveis", "oficial de registro")))
            .ToArray();
        if (registryDocuments.Length == 0)
        {
            return new(false,
                "Nenhuma matrícula imobiliária foi identificada automaticamente. Sem uma certidão registrária atualizada não é possível afirmar se existem penhoras, indisponibilidades ou outros ônus vigentes.",
                null, null, null, [],
                ["Localizar ou solicitar matrícula/certidão de inteiro teor atualizada e repetir a análise."]);
        }

        var latest = registryDocuments
            .OrderByDescending(item => ContainsAny(item.File.Name, "atualizada", "atual", "inteiro teor"))
            .ThenByDescending(item => LatestRecognizedDate(item.DocumentDates))
            .First();
        var combinedRegistryText = string.Join("\n", registryDocuments.Select(item => item.FullText));
        var registrationMatch = System.Text.RegularExpressions.Regex.Match(combinedRegistryText,
            @"matr[ií]cula\s*(?:n[º°o.]*)?\s*(?<number>\d[\d .-]{2,15})",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        var registrationNumber = registrationMatch.Success
            ? System.Text.RegularExpressions.Regex.Replace(registrationMatch.Groups["number"].Value, @"\s+", " ").Trim(' ', '.', '-')
            : null;
        var officeMatch = System.Text.RegularExpressions.Regex.Match(combinedRegistryText,
            @"(?<office>\d{1,2}[º°o.]?\s*(?:cart[oó]rio|oficial|registro)\s+(?:de\s+)?registro\s+de\s+im[oó]veis[^\r\n.;]{0,80})",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        var registryOffice = officeMatch.Success ? CleanProse(officeMatch.Groups["office"].Value) : null;

        var candidateDocuments = uniqueAnalyses.Where(item =>
                registryDocuments.Contains(item) ||
                ContainsAny(item.File.Name, "penhora", "indispon", "restri", "averba", "baixa", "cancelamento") ||
                ContainsAny(item.FullText, "cancelamento da penhora", "baixa da averbação", "levantamento da penhora"))
            .ToArray();
        var definitions = new (string Type, string[] Terms)[]
        {
            ("Penhora", ["penhora", "penhorado"]),
            ("Indisponibilidade", ["indisponibilidade", "indisponível", "indisponivel"]),
            ("Hipoteca", ["hipoteca"]),
            ("Alienação fiduciária", ["alienação fiduciária", "alienacao fiduciaria"]),
            ("Arresto", ["arresto"]),
            ("Usufruto", ["usufruto"]),
            ("Ônus/restrição registrária", ["ônus", "onus", "restrição registrária", "restricao registraria"])
        };
        var restrictions = new List<MasterRegistryRestriction>();
        foreach (var analysis in candidateDocuments)
        {
            var relativePath = Path.GetRelativePath(root, analysis.File.FullPath);
            foreach (var definition in definitions)
            foreach (var term in definition.Terms)
            {
                var offset = 0;
                while (offset < analysis.FullText.Length)
                {
                    var index = analysis.FullText.IndexOf(term, offset, StringComparison.OrdinalIgnoreCase);
                    if (index < 0) break;
                    var start = Math.Max(0, index - 220);
                    var length = Math.Min(720, analysis.FullText.Length - start);
                    var evidence = CleanProse(analysis.FullText.Substring(start, length));
                    var registryReference = FindNearestRegistryReference(analysis.FullText, index);
                    var dateMatch = System.Text.RegularExpressions.Regex.Match(evidence,
                        @"\b(?:0?[1-9]|[12]\d|3[01])[/.-](?:0?[1-9]|1[0-2])[/.-](?:19|20)\d{2}\b");
                    var statusStart = Math.Max(0, index - 35);
                    var statusContext = analysis.FullText.Substring(statusStart,
                        Math.Min(220, analysis.FullText.Length - statusStart));
                    var cancellation = ContainsAny(statusContext, "cancelamento", "cancelada", "cancelado",
                        "baixa da averbação", "baixa de averbação", "levantamento", "sem efeito");
                    restrictions.Add(new(definition.Type,
                        cancellation ? "Baixada/cancelada - evidência localizada" : "Pendente de confirmação ou baixa",
                        registryReference,
                        dateMatch.Success ? dateMatch.Value : null,
                        evidence, relativePath, evidence));
                    offset = index + term.Length;
                }
            }
        }

        var distinct = restrictions
            .GroupBy(item => $"{item.Type}\0{item.RegistryReference ?? "sem referência"}\0{item.Status}",
                StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(item => item.RegistryReference is not null)
                .ThenByDescending(item => item.Evidence.Length)
                .First())
            .ToArray();
        var cancelledReferences = distinct.Where(item => item.Status.StartsWith("Baixada", StringComparison.OrdinalIgnoreCase) &&
                                                          item.RegistryReference is not null)
            .Select(item => item.RegistryReference!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var reconciled = distinct.Select(item => item.RegistryReference is not null &&
                                                 cancelledReferences.Contains(item.RegistryReference)
                ? item with { Status = "Baixada/cancelada - referência correlacionada" }
                : item)
            .GroupBy(item => $"{item.Type}\0{item.RegistryReference ?? "sem referência"}\0{item.Status}",
                StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(item => item.Date is not null)
                .ThenByDescending(item => item.Evidence.Length)
                .First())
            .OrderBy(item => item.Status.StartsWith("Pendente", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(item => item.Type)
            .Take(60)
            .ToArray();
        var pending = reconciled.Count(item => item.Status.StartsWith("Pendente", StringComparison.OrdinalIgnoreCase));
        var cancelled = reconciled.Length - pending;
        var summary = $"Foi identificada documentação de matrícula. A varredura localizou {reconciled.Length} ocorrência(s) de restrição/ato registrário: {pending} pendente(s) de confirmação ou baixa e {cancelled} com evidência de cancelamento/baixa. A classificação é documental e deve ser confrontada com certidão atualizada do cartório.";
        return new(true, summary, registrationNumber, registryOffice,
            Path.GetRelativePath(root, latest.File.FullPath), reconciled,
            [
                "Confirmar na matrícula mais recente se cada R./Av. permanece vigente ou foi efetivamente cancelada.",
                "Verificar penhoras, indisponibilidades, hipotecas, alienações fiduciárias, usufrutos e arrestos não abrangidos por ordem de baixa.",
                "Conferir se decisões judiciais de cancelamento foram prenotadas e averbadas pelo Registro de Imóveis.",
                "Obter nova certidão de inteiro teor quando o documento arquivado não refletir a situação registrária atual."
            ]);

        static DateTime LatestRecognizedDate(IReadOnlyList<string> dates) => dates
            .Select(value => DateTime.TryParse(value,
                System.Globalization.CultureInfo.GetCultureInfo("pt-BR"),
                System.Globalization.DateTimeStyles.None, out var parsed) ? parsed : DateTime.MinValue)
            .DefaultIfEmpty(DateTime.MinValue)
            .Max();
    }

    private static string? FindNearestRegistryReference(string text, int termIndex)
    {
        var start = Math.Max(0, termIndex - 140);
        var length = Math.Min(280, text.Length - start);
        var matches = System.Text.RegularExpressions.Regex.Matches(text.Substring(start, length),
            @"\b(?<reference>(?:R|AV)\.?\s*\d+)\b",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        var rawReference = matches.Cast<System.Text.RegularExpressions.Match>()
            .OrderBy(match => Math.Abs((start + match.Index) - termIndex))
            .Select(match => match.Groups["reference"].Value)
            .FirstOrDefault();
        if (rawReference is null) return null;
        var compact = rawReference.ToUpperInvariant().Replace(".", string.Empty).Replace(" ", string.Empty);
        var normalized = System.Text.RegularExpressions.Regex.Match(compact, @"^(?<prefix>R|AV)(?<number>\d+)$");
        return normalized.Success
            ? $"{normalized.Groups["prefix"].Value}.{normalized.Groups["number"].Value}"
            : compact;
    }

    private static string CategorizeFinancialEvidence(string evidence)
    {
        if (ContainsAny(evidence, "IPTU", "imposto predial")) return "IPTU";
        if (ContainsAny(evidence, "PPI", "programa de parcelamento incentivado")) return "PPI";
        if (ContainsAny(evidence, "condomínio", "condominial")) return "Condomínio";
        if (ContainsAny(evidence, "arrematação", "leilão", "parcela", "parcelamento")) return "Arrematação e parcelamentos";
        if (ContainsAny(evidence, "IPVA", "licenciamento", "multa de trânsito")) return "IPVA, multas e licenciamento";
        if (ContainsAny(evidence, "mensal", "aluguel", "energia", "água", "despesa")) return "Despesas mensais";
        if (ContainsAny(evidence, "custas", "honorários", "DARE", "guia")) return "Custas e despesas processuais";
        return "Outros valores documentais";
    }

    private static bool ContainsAny(string value, params string[] terms) =>
        terms.Any(term => value.Contains(term, StringComparison.OrdinalIgnoreCase));

    private static string EscapeTable(string value) => CleanProse(value)
        .Replace("|", "\\|", StringComparison.Ordinal);

    private static string CleanProse(string value)
    {
        value = RepairCommonMojibake(value).Normalize(NormalizationForm.FormC);
        var withoutLineWrapHyphenation = System.Text.RegularExpressions.Regex.Replace(
            value,
            @"(?<=\p{L})-[ \t]*(?:\r\n|\n|\r)[ \t]*(?=\p{Ll})",
            string.Empty);
        return System.Text.RegularExpressions.Regex.Replace(withoutLineWrapHyphenation, @"[ \t]+", " ").Trim();
    }

    private static string RepairCommonMojibake(string value)
    {
        (string Bad, string Good)[] replacements =
        [
            ("\u00C3\u00A1", "á"), ("\u00C3\u00A0", "à"), ("\u00C3\u00A2", "â"),
            ("\u00C3\u00A3", "ã"), ("\u00C3\u00A4", "ä"), ("\u00C3\u00A9", "é"),
            ("\u00C3\u00AA", "ê"), ("\u00C3\u00AD", "í"), ("\u00C3\u00B3", "ó"),
            ("\u00C3\u00B4", "ô"), ("\u00C3\u00B5", "õ"), ("\u00C3\u00BA", "ú"),
            ("\u00C3\u00BC", "ü"), ("\u00C3\u00A7", "ç"), ("\u00C3\u0081", "Á"),
            ("\u00C3\u0080", "À"), ("\u00C3\u0082", "Â"), ("\u00C3\u0083", "Ã"),
            ("\u00C3\u0089", "É"), ("\u00C3\u008A", "Ê"), ("\u00C3\u008D", "Í"),
            ("\u00C3\u0093", "Ó"), ("\u00C3\u0094", "Ô"), ("\u00C3\u0095", "Õ"),
            ("\u00C3\u009A", "Ú"), ("\u00C3\u0087", "Ç"), ("\u00C2\u00B0", "°"),
            ("\u00C2\u00BA", "º"), ("\u00C2\u00AA", "ª"), ("\u00C2\u00A7", "§"),
            ("\u00E2\u20AC\u201D", "—"), ("\u00E2\u20AC\u201C", "–"),
            ("\u00E2\u20AC\u2122", "’"), ("\u00E2\u20AC\u0153", "“"),
            ("\u00E2\u20AC\u009D", "”")
        ];
        foreach (var (bad, good) in replacements)
            value = value.Replace(bad, good, StringComparison.Ordinal);
        return value;
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024L * 1024) return $"{bytes / 1024d:N1} KiB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / 1024d / 1024d:N1} MiB";
        return $"{bytes / 1024d / 1024d / 1024d:N1} GiB";
    }

    private static string InlineCode(string value)
    {
        var longestRun = System.Text.RegularExpressions.Regex.Matches(value, "`+").Cast<System.Text.RegularExpressions.Match>()
            .Select(match => match.Length).DefaultIfEmpty(0).Max();
        var delimiter = new string('`', longestRun + 1);
        return $"{delimiter}{CleanProse(value)}{delimiter}";
    }

    private static byte[] EncodeUtf8WithBom(string content)
    {
        var preamble = Utf8WithBom.GetPreamble();
        var payload = Utf8WithBom.GetBytes(content);
        var result = new byte[preamble.Length + payload.Length];
        Buffer.BlockCopy(preamble, 0, result, 0, preamble.Length);
        Buffer.BlockCopy(payload, 0, result, preamble.Length, payload.Length);
        return result;
    }

    private static string NormalizeMarkdownForEditors(string content)
    {
        var normalized = content.TrimStart('\uFEFF')
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Normalize(NormalizationForm.FormC);
        var clean = new string(normalized
            .Where(character => character is '\n' or '\t' || !char.IsControl(character))
            .ToArray());
        return clean.Replace("\n", "\r\n", StringComparison.Ordinal);
    }

    private static bool IsGeneratedOrganizaFile(string path)
    {
        var name = Path.GetFileName(path);
        return MasterBookFileNames.IsMarkdown(name) ||
               string.Equals(name, MasterBookFileNames.Json, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(name, ".organiza_log.json", StringComparison.OrdinalIgnoreCase) ||
               WorkspaceFilePolicy.IsTemporaryOrLockFileName(name);
    }

    private static string[] SelectDossierProcesses(
        string root,
        IReadOnlyList<DocumentAnalysis> analyses)
    {
        var declared = ExtractProcessNumbers(Path.GetFileName(root))
            .Concat(analyses.SelectMany(analysis => ExtractProcessNumbers(Path.GetFileName(analysis.File.FullPath))))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var independentEvidenceCounts = analyses
            .SelectMany(analysis => analysis.ProcessIdentifiers.Distinct(StringComparer.OrdinalIgnoreCase))
            .GroupBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
        declared.UnionWith(independentEvidenceCounts.Where(pair => pair.Value >= 2).Select(pair => pair.Key));

        if (declared.Count == 0)
        {
            var onlyIdentifier = independentEvidenceCounts.Keys.Take(2).ToArray();
            if (onlyIdentifier.Length == 1) declared.Add(onlyIdentifier[0]);
        }

        return declared.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static IEnumerable<string> ExtractProcessNumbers(string value) =>
        ProcessNumberRegex().Matches(value).Cast<System.Text.RegularExpressions.Match>()
            .Select(match => NormalizeProcessNumber(match.Value));

    private static string NormalizeProcessNumber(string value)
    {
        var digits = new string(value.Where(char.IsDigit).ToArray());
        return digits.Length == 20
            ? $"{digits[..7]}-{digits.Substring(7, 2)}.{digits.Substring(9, 4)}.{digits.Substring(13, 1)}.{digits.Substring(14, 2)}.{digits.Substring(16, 4)}"
            : value;
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"(?<!\d)(?:\d{7}-\d{2}\.\d{4}\.\d\.\d{2}\.\d{4}|\d{20})(?!\d)")]
    private static partial System.Text.RegularExpressions.Regex ProcessNumberRegex();
}
