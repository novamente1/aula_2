using System.Globalization;
using System.Text.RegularExpressions;
using Organiza.Domain.Files;
using Organiza.Domain.MasterBook;
using Organiza.Domain.Organization;

namespace Organiza.Application.Services;

public sealed partial class MasterBookService
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");
    private static readonly Regex DeepDateRegex = new(
        @"\b(?:0?[1-9]|[12]\d|3[01])[/.-](?:0?[1-9]|1[0-2])[/.-](?:19|20)\d{2}\b",
        RegexOptions.CultureInvariant);
    private static readonly Regex DeepMoneyRegex = new(
        @"R\$\s*(?<amount>\d{1,3}(?:\.\d{3})*,\d{2}|\d+,\d{2})",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex InstallmentRegex = new(
        @"\b(?:parcela|presta[cç][aã]o)\s*(?:n[º°o.]*)?\s*(?<number>\d{1,3})\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex AddressRegex = new(
        @"\b(?<address>(?:Rua|R\.|Avenida|Av\.|Alameda|Travessa|Estrada|Rodovia)\s+[^\r\n.;]{4,140})",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex RecipientRegex = new(
        @"\b(?:citei|intimei|notifiquei|procurei|deixei de (?:citar|intimar))\s+(?:o|a|os|as)?\s*(?<recipient>[A-ZÁÉÍÓÚÂÊÔÃÕÇ][\p{L}\s.'-]{2,100})",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private sealed record PaymentCandidate(
        int? InstallmentNumber,
        decimal Amount,
        string? Date,
        bool IsObligation,
        bool IsPayment,
        string RelativePath,
        string Evidence);

    private static IReadOnlyList<MasterRegistryAuctionEvent> BuildRegistryAuctionChronology(
        string root,
        IReadOnlyList<DocumentAnalysis> analyses,
        MasterPropertyRegistryAnalysis registry)
    {
        var result = new List<MasterRegistryAuctionEvent>();
        foreach (var restriction in registry.Restrictions)
        {
            result.Add(new(
                $"Restrição registral — {restriction.Type}",
                restriction.Date,
                restriction.RegistryReference,
                restriction.Status,
                "Marco registral a confrontar com a expedição e o registro da carta de arrematação.",
                [restriction.RelativePath],
                SummarizeExcerpt(restriction.Evidence, 520)));
        }

        var auctionDocuments = analyses
            .GroupBy(item => item.ContentSha256, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Where(item => ContainsAny(Path.GetFileNameWithoutExtension(item.File.Name),
                               "carta de arrematação", "carta arrematação", "auto de arrematação") ||
                           ContainsAny(item.FullText,
                               "carta de arrematação", "carta de arrematacao", "auto de arrematação",
                               "auto de arrematacao", "expedição da carta", "expedicao da carta"))
            .ToArray();

        foreach (var analysis in auctionDocuments)
        {
            var relativePath = Path.GetRelativePath(root, analysis.File.FullPath);
            var index = IndexOfAny(analysis.FullText,
                "carta de arrematação", "carta de arrematacao", "auto de arrematação",
                "auto de arrematacao", "expedição da carta", "expedicao da carta");
            var evidence = Window(analysis.FullText, Math.Max(0, index), 300, 950);
            var date = NearestDate(evidence) ?? analysis.DocumentDates.FirstOrDefault();
            var auctionDate = ParseRecognizedDate(date);
            var previousPending = registry.Restrictions.Count(item =>
                item.Status.StartsWith("Pendente", StringComparison.OrdinalIgnoreCase) &&
                IsOnOrBefore(item.Date, auctionDate));
            var previousCancelled = registry.Restrictions.Count(item =>
                item.Status.StartsWith("Baixada", StringComparison.OrdinalIgnoreCase) &&
                IsOnOrBefore(item.Date, auctionDate));
            var laterRestrictions = registry.Restrictions.Count(item =>
                IsAfter(item.Date, auctionDate));

            var relation = auctionDate is null
                ? "A data da carta/auto não foi reconhecida; o cruzamento cronológico permanece pendente."
                : $"Na data documental da carta/auto, havia {previousPending} restrição(ões) anterior(es) sem baixa confirmada, " +
                  $"{previousCancelled} com baixa/cancelamento correlacionado e {laterRestrictions} ato(s) registral(is) posterior(es).";
            var status = previousPending > 0
                ? "ALERTA — restrição anterior sem baixa documental confirmada"
                : auctionDate is null
                    ? "Pendente de data para conciliação"
                    : "Sem conflito anterior pendente reconhecido automaticamente";
            result.Add(new(
                ContainsAny(evidence, "carta") ? "Carta de arrematação" : "Auto de arrematação",
                date,
                null,
                status,
                relation,
                [relativePath],
                SummarizeExcerpt(evidence, 640)));
        }

        return result
            .DistinctBy(item => $"{item.EventType}\0{item.Date}\0{string.Join('|', item.SourceDocuments)}\0{item.Evidence}",
                StringComparer.OrdinalIgnoreCase)
            .OrderBy(item => ParseRecognizedDate(item.Date) ?? DateTime.MaxValue)
            .ThenBy(item => item.EventType, StringComparer.OrdinalIgnoreCase)
            .Take(100)
            .ToArray();
    }

    private static IReadOnlyList<MasterPaymentReconciliation> BuildPaymentReconciliation(
        string root,
        IReadOnlyList<DocumentAnalysis> analyses)
    {
        var candidates = new List<PaymentCandidate>();
        foreach (var analysis in analyses.GroupBy(item => item.ContentSha256, StringComparer.OrdinalIgnoreCase)
                     .Select(group => group.First()))
        {
            if (!ContainsAny(analysis.FullText, "parcela", "prestação", "prestacao", "recibo",
                    "comprovante de pagamento", "pagamento", "quitado", "quitação", "quitacao"))
                continue;

            var relativePath = Path.GetRelativePath(root, analysis.File.FullPath);
            foreach (Match amountMatch in DeepMoneyRegex.Matches(analysis.FullText))
            {
                var evidence = Window(analysis.FullText, amountMatch.Index, 260, 780);
                var isPayment = ContainsAny(evidence, "recibo", "comprovante", "pagamento efetuado",
                    "pago", "quitado", "quitação", "autenticação bancária", "autenticacao bancaria");
                var isObligation = ContainsAny(evidence, "vencimento", "saldo devedor", "deverá pagar",
                                       "devera pagar", "parcelamento", "plano de pagamento") ||
                                   (!isPayment && ContainsAny(evidence, "parcela", "prestação", "prestacao"));
                if (!isPayment && !isObligation) continue;
                if (!decimal.TryParse(amountMatch.Groups["amount"].Value, NumberStyles.Number, PtBr,
                        out var amount)) continue;
                var installmentMatch = InstallmentRegex.Match(evidence);
                candidates.Add(new(
                    installmentMatch.Success ? int.Parse(installmentMatch.Groups["number"].Value, PtBr) : null,
                    amount,
                    NearestDate(evidence),
                    isObligation,
                    isPayment,
                    relativePath,
                    SummarizeExcerpt(evidence, 560)));
            }
        }

        return candidates
            .DistinctBy(item => $"{item.RelativePath}\0{item.InstallmentNumber}\0{item.Amount}\0{item.Date}\0{item.IsPayment}\0{item.Evidence}",
                StringComparer.OrdinalIgnoreCase)
            .GroupBy(item => item.InstallmentNumber.HasValue
                    ? $"PARCELA-{item.InstallmentNumber:000}"
                    : $"VALOR-{item.Amount:0.00}",
                StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var obligations = group.Where(item => item.IsObligation).ToArray();
                var payments = group.Where(item => item.IsPayment).ToArray();
                var expected = obligations.Select(item => (decimal?)item.Amount).FirstOrDefault();
                var paid = payments.Select(item => (decimal?)item.Amount).FirstOrDefault();
                var independentPair = obligations.Any(obligation => payments.Any(payment =>
                    !string.Equals(obligation.RelativePath, payment.RelativePath, StringComparison.OrdinalIgnoreCase)));
                var status = obligations.Length == 0
                    ? "RECIBO SEM OBRIGAÇÃO/PARCELA VINCULADA"
                    : payments.Length == 0
                        ? "PARCELA SEM RECIBO LOCALIZADO"
                        : group.First().InstallmentNumber is null
                            ? "AGRUPAMENTO POR VALOR — REVISÃO MANUAL"
                            : !independentPair
                                ? "EVIDÊNCIA ÚNICA — EXIGE COMPROVAÇÃO INDEPENDENTE"
                        : expected == paid
                            ? "CONCILIADA DOCUMENTALMENTE"
                            : "DIVERGÊNCIA DE VALOR";
                return new MasterPaymentReconciliation(
                    group.Key,
                    group.First().InstallmentNumber,
                    expected,
                    paid,
                    obligations.Select(item => item.Date).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)),
                    payments.Select(item => item.Date).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)),
                    status,
                    group.Select(item => item.RelativePath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                    string.Join(" | ", group.Select(item => item.Evidence).Distinct(StringComparer.OrdinalIgnoreCase).Take(3)));
            })
            .OrderBy(item => item.InstallmentNumber ?? int.MaxValue)
            .ThenBy(item => item.ReconciliationKey, StringComparer.OrdinalIgnoreCase)
            .Take(150)
            .ToArray();
    }

    private static IReadOnlyList<MasterBailiffDiligence> BuildBailiffDiligences(
        string root,
        IReadOnlyList<DocumentAnalysis> analyses)
    {
        var result = new List<MasterBailiffDiligence>();
        foreach (var analysis in analyses.GroupBy(item => item.ContentSha256, StringComparer.OrdinalIgnoreCase)
                     .Select(group => group.First()))
        {
            var relativePath = Path.GetRelativePath(root, analysis.File.FullPath);
            var searchTerms = new[]
            {
                "oficial de justiça", "oficial de justica", "certifico e dou fé", "certifico e dou fe",
                "mandado", "diligência", "diligencia"
            };
            var offsets = FindTermOffsets(analysis.FullText, searchTerms).Take(20).ToArray();
            foreach (var offset in offsets)
            {
                var evidence = Window(analysis.FullText, offset, 260, 900);
                var executionMarker = ContainsAny(evidence, "citei", "intimei", "notifiquei", "penhorei",
                    "constatei", "procedi à", "procedi a", "dirigi-me", "compareci", "deixei de",
                    "certifico e dou fé", "certifico e dou fe", "certidão negativa", "certidao negativa",
                    "não foi possível", "nao foi possivel");
                var issuedMarker = ContainsAny(evidence, "mandado de citação", "mandado de citacao",
                    "mandado de intimação", "mandado de intimacao", "mandado de penhora",
                    "manda ao oficial de justiça", "manda ao oficial de justica",
                    "deverá o sr. oficial", "devera o sr. oficial");
                if (!executionMarker && !issuedMarker)
                    continue;
                var outcome = ContainsAny(evidence, "deixei de", "não localizado", "nao localizado",
                        "não encontrado", "nao encontrado", "mudou-se", "endereço insuficiente", "endereco insuficiente")
                        || ContainsAny(evidence, "certidão negativa", "certidao negativa", "não foi possível", "nao foi possivel")
                    ? "NEGATIVA/INFRUTÍFERA"
                    : ContainsAny(evidence, "citei", "intimei", "notifiquei", "penhorei", "cumpri o mandado",
                        "procedi à", "procedi a")
                        ? "POSITIVA/CUMPRIDA"
                        : issuedMarker && !executionMarker
                            ? "MANDADO/ORDEM EXPEDIDA — CUMPRIMENTO A CONFIRMAR"
                            : "RESULTADO A CONFIRMAR";
                var act = ContainsAny(evidence, "penhorei", "penhora") ? "Penhora"
                    : ContainsAny(evidence, "intimei", "intimação", "intimacao") ? "Intimação"
                    : ContainsAny(evidence, "citei", "citação", "citacao") ? "Citação"
                    : ContainsAny(evidence, "constatei", "constatação", "constatacao") ? "Constatação"
                    : "Diligência/mandado";
                var address = AddressRegex.Match(evidence);
                var recipient = RecipientRegex.Match(evidence);
                result.Add(new(
                    NearestDate(evidence) ?? analysis.DocumentDates.FirstOrDefault(),
                    act,
                    outcome,
                    address.Success ? CleanProse(address.Groups["address"].Value) : null,
                    recipient.Success ? CleanProse(recipient.Groups["recipient"].Value) : null,
                    SummarizeExcerpt(evidence, 430),
                    relativePath,
                    SummarizeExcerpt(evidence, 760)));
            }
        }

        return result
            .GroupBy(item => $"{item.RelativePath}\0{item.Date ?? "sem data"}\0{item.Act}\0{item.Outcome}\0{item.Address ?? "sem endereço"}",
                StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(item => item.Recipient is not null)
                .ThenByDescending(item => item.Evidence.Length)
                .First())
            .OrderBy(item => ParseRecognizedDate(item.Date) ?? DateTime.MaxValue)
            .ThenBy(item => item.RelativePath, StringComparer.OrdinalIgnoreCase)
            .Take(120)
            .ToArray();
    }

    private static IEnumerable<int> FindTermOffsets(string text, IEnumerable<string> terms)
    {
        var offsets = new SortedSet<int>();
        foreach (var term in terms)
        {
            for (var start = 0; start < text.Length;)
            {
                var index = text.IndexOf(term, start, StringComparison.OrdinalIgnoreCase);
                if (index < 0) break;
                offsets.Add(index);
                start = index + term.Length;
            }
        }
        return offsets;
    }

    private static int IndexOfAny(string text, params string[] terms) => terms
        .Select(term => text.IndexOf(term, StringComparison.OrdinalIgnoreCase))
        .Where(index => index >= 0)
        .DefaultIfEmpty(0)
        .Min();

    private static string Window(string text, int index, int before, int totalLength)
    {
        var start = Math.Max(0, index - before);
        var length = Math.Min(totalLength, text.Length - start);
        return CleanProse(text.Substring(start, length));
    }

    private static string? NearestDate(string text)
    {
        var match = DeepDateRegex.Match(text);
        return match.Success ? match.Value : null;
    }

    private static DateTime? ParseRecognizedDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return DateTime.TryParse(value, PtBr, DateTimeStyles.None, out var parsed) ? parsed : null;
    }

    private static bool IsOnOrBefore(string? value, DateTime? reference)
    {
        if (reference is null) return false;
        var parsed = ParseRecognizedDate(value);
        return parsed is not null && parsed <= reference;
    }

    private static bool IsAfter(string? value, DateTime? reference)
    {
        if (reference is null) return false;
        var parsed = ParseRecognizedDate(value);
        return parsed is not null && parsed > reference;
    }
}
