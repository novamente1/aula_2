using System.Text.RegularExpressions;
using Organiza.Domain.Organization;

namespace Organiza.Application.Services;

/// <summary>
/// Aplica o protocolo aprovado para a remessa: sequência por pasta sugerida,
/// descrição curta e somente identificadores comprovados no conteúdo.
/// Linhas digitáveis e códigos de barras nunca são usados como ID principal.
/// </summary>
public static partial class OfficialFileNamingProtocol
{
    public static IReadOnlyList<RenameSuggestion> Apply(
        IReadOnlyList<RenameSuggestion> suggestions,
        IReadOnlyList<DocumentAnalysis> analyses)
    {
        var analysisByPath = analyses.ToDictionary(item => item.File.FullPath,
            StringComparer.OrdinalIgnoreCase);
        var result = new List<RenameSuggestion>(suggestions.Count);
        foreach (var folderGroup in suggestions.GroupBy(
                     item => item.DestinationFolder ?? Path.GetDirectoryName(item.OriginalPath) ?? string.Empty,
                     StringComparer.OrdinalIgnoreCase))
        {
            var ordered = folderGroup.OrderBy(item => item.OriginalPath,
                StringComparer.CurrentCultureIgnoreCase).ToArray();
            var sequenceNumber = 0;
            var usedSequences = new HashSet<int>();
            for (var index = 0; index < ordered.Length; index++)
            {
                var suggestion = ordered[index];
                if (!analysisByPath.TryGetValue(suggestion.OriginalPath, out var analysis) ||
                    string.Equals(suggestion.ClassificationRule, "REVISAO-QUALIDADE-OCR",
                        StringComparison.OrdinalIgnoreCase))
                {
                    result.Add(suggestion);
                    continue;
                }

                var identifiers = ExtractIdentifiers(analysis, suggestion.DocumentType);
                var existingSequence = ExistingSequenceRegex().Match(Path.GetFileName(suggestion.OriginalPath));
                if (!existingSequence.Success ||
                    !int.TryParse(existingSequence.Groups[1].Value, out sequenceNumber) ||
                    sequenceNumber < 1 || !usedSequences.Add(sequenceNumber))
                {
                    sequenceNumber = 1;
                    while (!usedSequences.Add(sequenceNumber)) sequenceNumber++;
                }
                var sequence = sequenceNumber.ToString(sequenceNumber > 99 ? "000" : "00");
                var description = CleanDescription(suggestion.DocumentType, analysis.FullText, analysis.File.FullPath);
                var parts = new List<string> { sequence, description };
                if (!string.IsNullOrWhiteSpace(identifiers.Principal)) parts.Add(identifiers.Principal);
                if (!string.IsNullOrWhiteSpace(identifiers.Secondary)) parts.Add(identifiers.Secondary);
                var extension = Path.GetExtension(suggestion.OriginalPath);
                var name = string.Join(" - ", parts) + extension;
                var status = identifiers.Principal is null
                    ? "Parcial: sequência e descrição comprovadas; ID principal não localizado e não inventado."
                    : identifiers.Secondary is null
                        ? "Conforme: sequência, descrição e ID principal comprovado; extensão preservada."
                        : "Conforme: sequência, descrição, ID principal e ID secundário comprovados; extensão preservada.";
                result.Add(suggestion with
                {
                    SuggestedName = name,
                    NamingProtocolStatus = status,
                    PrincipalId = identifiers.Principal,
                    SecondaryId = identifiers.Secondary,
                    Reason = $"{suggestion.Reason} Protocolo NN aplicado; linha digitável/código de barras excluídos dos identificadores.".Trim()
                });
            }
        }

        return suggestions.Select(item => result.Single(candidate =>
            string.Equals(candidate.OriginalPath, item.OriginalPath, StringComparison.OrdinalIgnoreCase))).ToArray();
    }

    private static (string? Principal, string? Secondary) ExtractIdentifiers(
        DocumentAnalysis analysis,
        string? documentType)
    {
        var text = analysis.FullText;
        var identifierEvidence = IsImage(analysis.File.Extension) ? $"{text}\n{analysis.File.FullPath}" : text;
        var process = analysis.ProcessIdentifiers.FirstOrDefault();
        var cpf = MatchGroup(CpfRegex(), text);
        var cnpj = MatchGroup(CnpjRegex(), text);
        var iptu = DigitsOnly(MatchGroup(IptuRegex(), text));
        var registration = DigitsOnly(MatchGroup(RegistrationRegex(), text));
        var renavam = DigitsOnly(MatchGroup(RenavamRegex(), text));
        var plateValue = VehiclePlateRegex().Match(identifierEvidence) is { Success: true } plate
            ? plate.Value.ToUpperInvariant()
            : null;

        var type = documentType ?? string.Empty;
        var isVehicle = ContainsAny(type, "veículo", "veiculo", "renavam", "crlv", "detran", "senatran");
        var principal = isVehicle ? renavam ?? plateValue ?? process
            : ContainsAny(type, "cpf", "rg", "identidade") ? cpf ?? process
            : ContainsAny(type, "cnpj", "empresa") ? cnpj ?? process
            : ContainsAny(type, "iptu") ? iptu ?? registration ?? process
            : ContainsAny(type, "matrícula", "matricula", "imobiliária", "imobiliaria") ? registration ?? process
            : process ?? cpf ?? cnpj ?? iptu ?? registration ?? renavam ?? plateValue;

        string? secondary = null;
        if (!string.Equals(plateValue, principal, StringComparison.OrdinalIgnoreCase)) secondary = plateValue;
        if (secondary is null && !string.Equals(process, principal, StringComparison.OrdinalIgnoreCase)) secondary = process;
        var parcel = MatchGroup(ParcelRegex(), text);
        if (secondary is null && parcel is not null) secondary = $"Parc {parcel.PadLeft(2, '0')}";
        secondary ??= analysis.DocumentDates.Select(ToIsoDate).FirstOrDefault(value => value is not null);
        if (string.Equals(principal, secondary, StringComparison.OrdinalIgnoreCase)) secondary = null;
        return (principal, secondary);
    }

    private static string CleanDescription(string? value, string text, string fullPath)
    {
        var clean = string.IsNullOrWhiteSpace(value) ? "Documento" : value.Trim();
        var titleZone = text.Length <= 1_200 ? text : text[..1_200];
        if (ContainsAny(clean, "documento do veículo", "documento do veiculo", "crlv") &&
            ContainsAny(titleZone, "certificado de registro e licenciamento", "CRLV", "CRV"))
            clean = "Doc Renavam";
        else if (ContainsAny(clean, "ofício", "oficio") && ContainsAny(titleZone, "renajud") &&
                 ContainsAny(titleZone, "baixa", "restrição", "restricao"))
            clean = "Ofício de baixa Renajud";
        else if (ContainsAny(text, "notificação", "notificacao") && ContainsAny(text, "leilão", "leilao"))
            clean = "Notificação de leilão";
        else if (ContainsAny(clean, "fotografia"))
            clean = ContainsAny(fullPath, "caminhão", "caminhao") ? "Foto caminhão" : "Foto do dossiê";
        clean = InvalidNameCharactersRegex().Replace(clean, " ");
        clean = WhitespaceRegex().Replace(clean, " ").Trim(' ', '-', '_');
        if (clean.Length <= 48) return clean;
        var shortened = clean[..48];
        var lastSpace = shortened.LastIndexOf(' ');
        return (lastSpace > 24 ? shortened[..lastSpace] : shortened).TrimEnd();
    }

    private static bool ContainsAny(string value, params string[] terms) =>
        terms.Any(term => value.Contains(term, StringComparison.OrdinalIgnoreCase));

    private static bool IsImage(string extension) => extension.ToLowerInvariant() is
        ".jpg" or ".jpeg" or ".png" or ".tif" or ".tiff" or ".bmp" or ".webp";

    private static string? MatchGroup(Regex regex, string text)
    {
        var match = regex.Match(text);
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    private static string? DigitsOnly(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var digits = Regex.Replace(value, @"\D", string.Empty);
        return digits.Length == 0 ? null : digits;
    }

    private static string? ToIsoDate(string value)
    {
        var formats = new[] { "dd/MM/yyyy", "d/M/yyyy", "dd-MM-yyyy", "d-M-yyyy", "dd.MM.yyyy", "yyyy-MM-dd" };
        return DateTime.TryParseExact(value, formats, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var date)
            ? date.ToString("yyyy-MM-dd")
            : null;
    }

    [GeneratedRegex(@"(?i)\bCPF\b\s*(?:n[º°o.]*)?[:\-]?\s*(\d{3}\.?\d{3}\.?\d{3}-?\d{2})")]
    private static partial Regex CpfRegex();

    [GeneratedRegex(@"(?i)\bCNPJ\b\s*(?:n[º°o.]*)?[:\-]?\s*(\d{2}\.?\d{3}\.?\d{3}/?\d{4}-?\d{2})")]
    private static partial Regex CnpjRegex();

    [GeneratedRegex(@"(?i)\b(?:IPTU|SQL|cadastro\s+do\s+im[oó]vel)\b\s*(?:n[º°o.]*)?[:\-]?\s*([\d.\-]{5,25})")]
    private static partial Regex IptuRegex();

    [GeneratedRegex(@"(?i)\bmatr[ií]cula\b\s*(?:n[º°o.]*)?[:\-]?\s*([\d.\-]{3,20})")]
    private static partial Regex RegistrationRegex();

    [GeneratedRegex(@"(?i)\bRENAVAM\b\s*(?:n[º°o.]*)?[:\-]?\s*(\d{9,11})")]
    private static partial Regex RenavamRegex();

    [GeneratedRegex(@"\b[A-Z]{3}[0-9][A-Z0-9][0-9]{2}\b", RegexOptions.IgnoreCase)]
    private static partial Regex VehiclePlateRegex();

    [GeneratedRegex(@"(?i)\bparc(?:ela)?\s*(?:n[º°o.]*)?[:\-]?\s*(\d{1,3})\b")]
    private static partial Regex ParcelRegex();

    [GeneratedRegex(@"[<>:\""/\\|?*\p{C}]")]
    private static partial Regex InvalidNameCharactersRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex(@"^(\d{2,3})\s+-\s+")]
    private static partial Regex ExistingSequenceRegex();
}
