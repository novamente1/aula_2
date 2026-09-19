using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Organiza.Domain.MasterBook;

namespace Organiza.Application.Services;

public sealed partial class MasterBookService
{
    private static readonly Regex CriticalMoneyRegex = new(
        @"(?<!\*)R\$\s*\d{1,3}(?:\.\d{3})*,\d{2}(?!\*)",
        RegexOptions.CultureInvariant);
    private static readonly Regex CriticalProcessRegex = new(
        @"(?<!\*)\b\d{7}-\d{2}\.\d{4}\.\d\.\d{2}\.\d{4}\b(?!\*)",
        RegexOptions.CultureInvariant);
    private static readonly Regex CriticalDateRegex = new(
        @"(?<!\*)\b(?:0?[1-9]|[12]\d|3[01])[/.-](?:0?[1-9]|1[0-2])[/.-](?:19|20)\d{2}\b(?!\*)",
        RegexOptions.CultureInvariant);
    private static readonly Regex CriticalLegalTermRegex = new(
        @"(?<!\*)(?<![\p{L}])(?<term>penhora|indisponibilidade|hipoteca|alienação fiduciária|arresto|usufruto|ônus registrário|obrigação|prazo fatal|vencimento|saldo devedor)(?![\p{L}])(?!\*)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static string RenderAbntMarkdown(MasterBookReport report)
    {
        var builder = new StringBuilder();
        var references = report.SourceDocuments.Select((source, index) =>
                new { source.RelativePath, Id = $"DOC-{index + 1:000}" })
            .ToDictionary(item => item.RelativePath, item => item.Id, StringComparer.OrdinalIgnoreCase);
        string ReferenceFor(string path) => references.TryGetValue(path, out var id) ? id : "fonte não indexada";
        string ReferenceList(IEnumerable<string> paths) => string.Join(", ", paths
            .Select(ReferenceFor).Distinct(StringComparer.OrdinalIgnoreCase));
        string Money(decimal? amount) => amount.HasValue
            ? $"**{amount.Value.ToString("C", PtBr)}**"
            : "não reconhecido";
        string Date(string? value) => string.IsNullOrWhiteSpace(value)
            ? "**não reconhecida**"
            : $"**{CleanProse(value)}**";
        string BoldCell(string value) => $"**{EscapeTable(value)}**";

        var consolidatedRestrictions = report.PropertyRegistryAnalysis.Restrictions
            .GroupBy(item => $"{CleanProse(item.Type)}\0{CleanProse(item.RegistryReference ?? "sem referência")}\0{CleanProse(item.Status)}",
                StringComparer.OrdinalIgnoreCase)
            .Select(group => new
            {
                Item = group.OrderByDescending(item => !string.IsNullOrWhiteSpace(item.Date))
                    .ThenByDescending(item => item.Evidence.Length).First(),
                Sources = group.Select(item => item.RelativePath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
            })
            .OrderBy(item => item.Item.Status.StartsWith("Pendente", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(item => item.Item.Type, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Item.RegistryReference, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var conciseReconciliations = report.PaymentReconciliation
            .GroupBy(item => $"{item.ReconciliationKey}\0{item.InstallmentNumber}\0{item.ExpectedAmount}\0{item.PaidAmount}\0{item.DueDate}\0{item.PaymentDate}\0{item.Status}",
                StringComparer.OrdinalIgnoreCase)
            .Select(group => new
            {
                Item = group.First(),
                Sources = group.SelectMany(item => item.SourceDocuments).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
            })
            .OrderBy(item => item.Item.InstallmentNumber ?? int.MaxValue)
            .ThenBy(item => item.Item.ReconciliationKey, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var conciseDiligences = report.BailiffDiligences
            .GroupBy(item => $"{item.Date ?? "sem data"}\0{item.Act}\0{item.Outcome}\0{item.Recipient ?? "sem destinatário"}\0{item.Address ?? "sem endereço"}",
                StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(item => item.Summary.Length).First())
            .OrderBy(item => item.Date, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Act, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        builder.AppendLine("# LIVRO MESTRE 360°")
            .AppendLine()
            .AppendLine("## RELATÓRIO JURÍDICO-DOCUMENTAL")
            .AppendLine()
            .AppendLine($"**Dossiê:** {EmphasizeCritical(report.SelectedRootName)}")
            .AppendLine()
            .AppendLine($"**Data de emissão:** **{report.GeneratedAt:dd/MM/yyyy}**, às {report.GeneratedAt:HH:mm:ss} ({report.GeneratedAt:zzz})")
            .AppendLine()
            .AppendLine($"**Universo documental:** {report.DocumentsAnalyzed} documento(s) submetido(s) à leitura integral e/ou OCR, conforme disponibilidade técnica.")
            .AppendLine()
            .AppendLine("---")
            .AppendLine()
            .AppendLine("## 1 IDENTIFICAÇÃO, OBJETO E ESCOPO")
            .AppendLine()
            .AppendLine("### 1.1 Identificação do dossiê")
            .AppendLine()
            .AppendLine(EmphasizeCritical(report.ExecutiveSynthesis.DossierOverview))
            .AppendLine()
            .AppendLine("### 1.2 Objeto do relatório")
            .AppendLine()
            .AppendLine("O presente relatório consolida os elementos jurídicos, registrais, financeiros e operacionais localizados no acervo, com rastreabilidade por referência documental e separação entre fato extraído, correlação automatizada e pendência de validação humana.")
            .AppendLine()
            .AppendLine("### 1.3 Método, limites e revisão profissional")
            .AppendLine()
            .AppendLine("A numeração progressiva, a hierarquia formal dos títulos e a organização dos apêndices seguem apresentação técnico-jurídica orientada pelas normas ABNT. O arquivo Markdown preserva a estrutura lógica; margens, fonte e paginação dependem do editor utilizado para impressão.")
            .AppendLine()
            .AppendLine($"**Ressalva obrigatória:** {EmphasizeCritical(report.ReviewNotice)}")
            .AppendLine()
            .AppendLine("## 2 SÍNTESE EXECUTIVA")
            .AppendLine()
            .AppendLine("### 2.1 Bem, leilão e arrematação")
            .AppendLine()
            .AppendLine(EmphasizeCritical(report.ExecutiveSynthesis.PropertyAndAuctionOverview))
            .AppendLine()
            .AppendLine("### 2.2 Situação jurídica e processual")
            .AppendLine()
            .AppendLine(EmphasizeCritical(report.ExecutiveSynthesis.ProceduralOverview))
            .AppendLine()
            .AppendLine("### 2.3 Exposição financeira")
            .AppendLine()
            .AppendLine(EmphasizeCritical(report.ExecutiveSynthesis.FinancialOverview))
            .AppendLine()
            .AppendLine("### 2.4 Atuação profissional anteriormente documentada")
            .AppendLine()
            .AppendLine(EmphasizeCritical(report.ExecutiveSynthesis.PriorCounselOverview))
            .AppendLine()
            .AppendLine("### 2.5 Prioridades imediatas")
            .AppendLine();
        if (report.ExecutiveSynthesis.ImmediatePriorities.Count == 0)
            builder.AppendLine("Não foram estruturadas prioridades automáticas.");
        else
            for (var index = 0; index < report.ExecutiveSynthesis.ImmediatePriorities.Count; index++)
                builder.AppendLine($"{index + 1}. {EmphasizeCritical(report.ExecutiveSynthesis.ImmediatePriorities[index])}");

        builder.AppendLine()
            .AppendLine("### 2.6 Processos identificados")
            .AppendLine();
        if (report.IdentifiedProcesses.Count == 0)
            builder.AppendLine("Nenhum número processual foi reconhecido automaticamente.");
        else
            foreach (var process in report.IdentifiedProcesses)
                builder.AppendLine($"- **{CleanProse(process)}**");

        builder.AppendLine()
            .AppendLine("## 3 CRONOLOGIA REGISTRAL E ARREMATAÇÃO")
            .AppendLine()
            .AppendLine("### 3.1 Identificação registral")
            .AppendLine()
            .AppendLine(EmphasizeCritical(report.PropertyRegistryAnalysis.Summary));
        if (report.PropertyRegistryAnalysis.RegistryDocumentFound)
        {
            builder.AppendLine()
                .AppendLine($"**Matrícula:** {EmphasizeCritical(report.PropertyRegistryAnalysis.RegistrationNumber ?? "não reconhecida automaticamente")}")
                .AppendLine()
                .AppendLine($"**Serventia:** {EmphasizeCritical(report.PropertyRegistryAnalysis.RegistryOffice ?? "não reconhecida automaticamente")}")
                .AppendLine()
                .AppendLine($"**Documento registrário mais recente:** {ReferenceFor(report.PropertyRegistryAnalysis.LatestRegistryDocument ?? string.Empty)}");
        }

        builder.AppendLine()
            .AppendLine("### 3.2 Consolidação de gravames e restrições")
            .AppendLine()
            .AppendLine("| Gravame ou ato | Situação documental | Referência registral | Data | Fonte |")
            .AppendLine("|---|---|---|---|---|");
        if (consolidatedRestrictions.Length == 0)
            builder.AppendLine("| Nenhuma restrição reconhecida com segurança | Revisão da matrícula atualizada necessária | — | — | — |");
        else
            foreach (var consolidated in consolidatedRestrictions.Take(20))
            {
                var restriction = consolidated.Item;
                builder.AppendLine($"| {BoldCell(restriction.Type)} | {BoldCell(restriction.Status)} | {BoldCell(restriction.RegistryReference ?? "não reconhecida")} | {Date(restriction.Date)} | {BoldCell(ReferenceList(consolidated.Sources))} |");
            }
        if (consolidatedRestrictions.Length > 20)
            builder.AppendLine($"\nOutros {consolidatedRestrictions.Length - 20} grupo(s) consolidados permanecem na base JSON para auditoria, sem repetição no corpo do relatório.");

        builder.AppendLine()
            .AppendLine("### 3.3 Cruzamento cronológico com carta ou auto de arrematação")
            .AppendLine()
            .AppendLine("| Data | Evento | Referência | Resultado do cruzamento | Relação cronológica | Fontes |")
            .AppendLine("|---|---|---|---|---|---|");
        if (report.RegistryAuctionChronology.Count == 0)
            builder.AppendLine("| — | Nenhuma carta, auto ou restrição correlacionável foi reconhecida | — | Conciliação pendente | Obter documentos registrais e de arrematação | — |");
        else
            foreach (var item in report.RegistryAuctionChronology.Take(25))
                builder.AppendLine($"| {Date(item.Date)} | {EscapeTable(EmphasizeCritical(item.EventType))} | {EscapeTable(item.RegistryReference ?? "—")} | {EscapeTable(EmphasizeCritical(item.Status))} | {EscapeTable(EmphasizeCritical(item.ChronologicalRelation))} | {ReferenceList(item.SourceDocuments)} |");
        if (report.RegistryAuctionChronology.Count > 25)
            builder.AppendLine($"\nOutros {report.RegistryAuctionChronology.Count - 25} evento(s) permanecem na cronologia estruturada do JSON.");

        builder.AppendLine()
            .AppendLine("### 3.4 Pendências registrais")
            .AppendLine();
        foreach (var pending in report.PropertyRegistryAnalysis.PendingReview)
            builder.AppendLine($"- [ ] {EmphasizeCritical(pending)}");

        builder.AppendLine()
            .AppendLine("## 4 CONCILIAÇÃO FINANCEIRA DE PARCELAS E RECIBOS")
            .AppendLine()
            .AppendLine("### 4.1 Consolidação por categoria")
            .AppendLine()
            .AppendLine("| Categoria | Lançamentos | Documentos | Total nominal reconhecido | Validação necessária |")
            .AppendLine("|---|---:|---:|---:|---|");
        if (report.FinancialConsolidation.Count == 0)
            builder.AppendLine("| Nenhum valor monetário reconhecido | 0 | 0 | — | Conferir documentos financeiros manualmente |");
        else
            foreach (var summary in report.FinancialConsolidation)
                builder.AppendLine($"| {EscapeTable(summary.Category)} | {summary.Entries} | {summary.Documents} | **{summary.RecognizedTotal.ToString("C", PtBr)}** | {EscapeTable(summary.ReviewStatus)} |");

        builder.AppendLine()
            .AppendLine("### 4.2 Quadro de conciliação parcela × recibo")
            .AppendLine()
            .AppendLine("| Chave | Parcela | Valor devido | Valor pago | Vencimento | Pagamento | Situação | Fontes |")
            .AppendLine("|---|---:|---:|---:|---|---|---|---|");
        if (report.PaymentReconciliation.Count == 0)
            builder.AppendLine("| — | — | — | — | — | — | Nenhuma parcela/recibo conciliável foi reconhecido | — |");
        else
            foreach (var consolidated in conciseReconciliations.Take(30))
            {
                var item = consolidated.Item;
                builder.AppendLine($"| {EscapeTable(item.ReconciliationKey)} | {(item.InstallmentNumber?.ToString(PtBr) ?? "—")} | {Money(item.ExpectedAmount)} | {Money(item.PaidAmount)} | {Date(item.DueDate)} | {Date(item.PaymentDate)} | **{EscapeTable(item.Status)}** | {ReferenceList(consolidated.Sources)} |");
            }
        if (conciseReconciliations.Length > 30)
            builder.AppendLine($"\nOutros {conciseReconciliations.Length - 30} grupo(s) financeiros permanecem na base JSON para conferência, sem poluir o corpo do relatório.");

        builder.AppendLine()
            .AppendLine("### 4.3 Lançamentos monetários rastreados")
            .AppendLine();
        builder.AppendLine(report.FinancialInventory.Count == 0
            ? "Nenhum lançamento monetário foi reconhecido automaticamente."
            : $"Foram preservados {report.FinancialInventory.Count} lançamento(s) individualizado(s) na base JSON. O corpo do relatório apresenta apenas a consolidação e a conciliação parcela × recibo, evitando repetição visual.");

        builder.AppendLine()
            .AppendLine("## 5 HISTÓRICO DE DILIGÊNCIAS DOS OFICIAIS DE JUSTIÇA")
            .AppendLine()
            .AppendLine("| Data | Ato | Resultado | Destinatário | Endereço | Síntese | Fonte |")
            .AppendLine("|---|---|---|---|---|---|---|");
        if (report.BailiffDiligences.Count == 0)
            builder.AppendLine("| — | Nenhuma diligência estruturada automaticamente | Revisão de mandados e certidões necessária | — | — | — | — |");
        else
            foreach (var diligence in conciseDiligences.Take(20))
                builder.AppendLine($"| {Date(diligence.Date)} | {EscapeTable(diligence.Act)} | **{EscapeTable(diligence.Outcome)}** | {EscapeTable(diligence.Recipient ?? "não reconhecido")} | {EscapeTable(diligence.Address ?? "não reconhecido")} | {EscapeTable(EmphasizeCritical(diligence.Summary))} | {ReferenceFor(diligence.RelativePath)} |");
        if (conciseDiligences.Length > 20)
            builder.AppendLine($"\nOutras {conciseDiligences.Length - 20} diligência(s) consolidadas permanecem na base JSON para auditoria.");

        builder.AppendLine()
            .AppendLine("## 6 ANÁLISE JURÍDICO-DOCUMENTAL")
            .AppendLine()
            .AppendLine("Os achados abaixo são apresentados de forma sintética. Os trechos integrais e os metadados permanecem na base JSON para auditoria.");
        foreach (var section in report.Sections)
        {
            builder.AppendLine()
                .AppendLine($"### 6.{section.Number} {CleanProse(section.Title)}")
                .AppendLine()
                .AppendLine($"**Situação da análise:** {EmphasizeCritical(section.Status)}")
                .AppendLine();
            if (section.Findings.Count == 0)
                builder.AppendLine("Nenhuma evidência textual suficiente foi localizada; isso não equivale à inexistência jurídica do fato.");
            else
                for (var index = 0; index < Math.Min(3, section.Findings.Count); index++)
                {
                    var finding = section.Findings[index];
                    builder.AppendLine($"{index + 1}. {EmphasizeCritical(finding.Description)}")
                        .AppendLine($"   - **Suporte:** {CleanProse(finding.Confidence)}.")
                        .AppendLine($"   - **Fontes:** {ReferenceList(finding.EvidenceFiles)}.");
                }
            if (section.Findings.Count > 3)
                builder.AppendLine($"\nForam omitidos do corpo {section.Findings.Count - 3} achado(s) repetitivos; todos permanecem no JSON.");
            builder.AppendLine()
                .AppendLine("**Providências de revisão:**");
            foreach (var pending in section.PendingHumanReview)
                builder.AppendLine($"- [ ] {EmphasizeCritical(pending)}");
        }

        builder.AppendLine()
            .AppendLine("## 7 MATRIZES JURÍDICAS E DE RISCO")
            .AppendLine()
            .AppendLine("### 7.1 Matriz de teses jurídicas")
            .AppendLine()
            .AppendLine("| Tese potencial | Base favorável | Contraponto ou lacuna | Suporte | Fontes |")
            .AppendLine("|---|---|---|---|---|");
        foreach (var thesis in report.LegalThesisMatrix)
            builder.AppendLine($"| {EscapeTable(EmphasizeCritical(thesis.Thesis))} | {EscapeTable(EmphasizeCritical(SummarizeExcerpt(thesis.SupportingBasis, 300)))} | {EscapeTable(EmphasizeCritical(thesis.CounterpointOrGap))} | {EscapeTable(thesis.Confidence)} | {ReferenceList(thesis.EvidenceFiles)} |");
        if (report.LegalThesisMatrix.Count == 0)
            builder.AppendLine("| Nenhuma tese estruturada automaticamente | Revisar o inteiro teor | Documentação insuficiente | pendente | — |");

        builder.AppendLine()
            .AppendLine("### 7.2 Matriz de riscos processuais")
            .AppendLine()
            .AppendLine("| Risco | Probabilidade | Impacto | Urgência | Mitigação | Fontes |")
            .AppendLine("|---|---|---|---|---|---|");
        foreach (var risk in report.ProceduralRiskMatrix)
            builder.AppendLine($"| {EscapeTable(EmphasizeCritical(risk.Risk))} | {EscapeTable(risk.Probability)} | **{EscapeTable(risk.Impact)}** | **{EscapeTable(risk.Urgency)}** | {EscapeTable(EmphasizeCritical(risk.Mitigation))} | {ReferenceList(risk.EvidenceFiles)} |");
        if (report.ProceduralRiskMatrix.Count == 0)
            builder.AppendLine("| Situação não classificada | a apurar | potencialmente alto | imediata | Atualizar andamento e prazos | — |");

        builder.AppendLine()
            .AppendLine("## 8 PLANO DE AÇÃO E OBRIGAÇÕES")
            .AppendLine()
            .AppendLine("| Prioridade | Providência ou obrigação | Fundamento | Resultado esperado | Fontes |")
            .AppendLine("|---:|---|---|---|---|");
        foreach (var action in report.StrategicActionPlan)
            builder.AppendLine($"| **{action.Priority}** | {EscapeTable(EmphasizeCritical(action.Action))} | {EscapeTable(EmphasizeCritical(action.Rationale))} | {EscapeTable(EmphasizeCritical(action.ExpectedResult))} | {ReferenceList(action.EvidenceFiles)} |");

        builder.AppendLine()
            .AppendLine("## APÊNDICE A — INVENTÁRIO TÉCNICO DAS FONTES")
            .AppendLine()
            .AppendLine($"**Raiz analisada:** {InlineCode(report.SelectedRootPath)}")
            .AppendLine()
            .AppendLine("| Referência | Documento | Tamanho | Método | Cobertura | OCR | Integridade |")
            .AppendLine("|---|---|---:|---|---:|---:|---|");
        for (var index = 0; index < report.SourceDocuments.Count; index++)
        {
            var source = report.SourceDocuments[index];
            builder.AppendLine($"| DOC-{index + 1:000} | {EscapeTable(source.RelativePath)} | {FormatBytes(source.SizeBytes)} | {source.ExtractionMethod} | {source.PagesWithText}/{source.PageCount} | {source.OcrPages} | {(source.ExtractionComplete ? "completa" : "com ressalvas")} |");
        }

        builder.AppendLine()
            .AppendLine("## APÊNDICE B — REGISTRO DE INTEGRIDADE")
            .AppendLine()
            .AppendLine("```text");
        for (var index = 0; index < report.SourceDocuments.Count; index++)
            builder.AppendLine($"DOC-{index + 1:000}  {report.SourceDocuments[index].Sha256}");
        builder.AppendLine("```")
            .AppendLine()
            .AppendLine("Base estruturada correspondente: `.organiza_livro_mestre_360.json`.");
        return builder.ToString();
    }

    private static string EmphasizeCritical(string value)
    {
        var clean = CleanProse(value);
        clean = CriticalProcessRegex.Replace(clean, "**$0**");
        clean = CriticalMoneyRegex.Replace(clean, "**$0**");
        clean = CriticalDateRegex.Replace(clean, "**$0**");
        return CriticalLegalTermRegex.Replace(clean, "**${term}**");
    }
}
