using System.Text.RegularExpressions;
using Organiza.Application.Common;

namespace Organiza.Application.Services;

public static partial class PathNameShortener
{
    private static readonly (string Term, string Abbreviation)[] OfficialAbbreviations =
    [
        ("Certidão de Dívida Ativa", "CDA"),
        ("Comprovante", "Comp"), ("Pagamento", "Pagto"), ("Notificação", "Notif"),
        ("Parcelamento", "Parcel"), ("Extrato", "Ext"), ("Portaria", "Port"),
        ("Procuração", "Proc"), ("Recibo", "Rec"), ("Ofício", "Of"), ("Carnê", "Carne"),
        ("Lançamento", "Lanc"), ("Certidão", "Cert"), ("Declaração", "Decl"),
        ("Autorização", "Autoriz"), ("Histórico", "Hist"), ("Relatório", "Relat"),
        ("Planilha", "Plan"), ("Cumprimento", "Cump"), ("Despacho", "Desp"),
        ("Intimação", "Int"), ("Manifestação", "Manif"), ("Petição", "Pet"),
        ("Réplica", "Rep"), ("Contestação", "Cont"), ("Impugnação", "Imp"),
        ("Embargos", "Emb"), ("Apelação", "Apel"), ("Agravo", "Agr")
    ];

    public static string FitFileName(string directory, string fileName)
    {
        var fullDirectory = Path.GetFullPath(directory);
        var currentPath = Path.Combine(fullDirectory, fileName);
        if (currentPath.Length <= PathGuard.BlockingLength) return fileName;

        var extension = Path.GetExtension(fileName);
        var stem = ApplyOfficialAbbreviations(Path.GetFileNameWithoutExtension(fileName).Trim());
        var maximumStemLength = PathGuard.BlockingLength - fullDirectory.Length - 1 - extension.Length;
        if (maximumStemLength < 12)
            throw new UnsafePathException(currentPath, currentPath.Length,
                "A própria estrutura de pastas deixa menos de 12 caracteres disponíveis para o nome do arquivo.");

        var isoDate = IsoDateRegex().Match(stem).Value;
        var datePrefix = string.IsNullOrWhiteSpace(isoDate) ? string.Empty : isoDate + " - ";
        var important = ImportantTokenRegex().Matches(stem).Cast<Match>()
            .Select(match => match.Value.Trim())
            .Where(value => !string.Equals(value, isoDate, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var importantSuffix = important.Length == 0 ? string.Empty : " - " + string.Join(" - ", important);
        if (importantSuffix.Length >= maximumStemLength - 4)
            importantSuffix = importantSuffix[..Math.Max(0, maximumStemLength / 2)].TrimEnd(' ', '-', '_');

        var descriptive = stem;
        if (!string.IsNullOrWhiteSpace(isoDate))
            descriptive = descriptive.Replace(isoDate, string.Empty, StringComparison.OrdinalIgnoreCase);
        foreach (var token in important)
            descriptive = descriptive.Replace(token, string.Empty, StringComparison.OrdinalIgnoreCase);
        descriptive = SeparatorRegex().Replace(descriptive, " ").Trim(' ', '-', '_');

        var availableDescription = maximumStemLength - datePrefix.Length - importantSuffix.Length;
        if (availableDescription < 4)
            throw new UnsafePathException(currentPath, currentPath.Length,
                "Não há espaço suficiente para preservar a extensão e os identificadores do documento.");
        if (descriptive.Length > availableDescription)
            descriptive = descriptive[..availableDescription].TrimEnd(' ', '-', '_', '.');
        if (string.IsNullOrWhiteSpace(descriptive)) descriptive = "Documento";

        var fitted = (datePrefix + descriptive + importantSuffix).Trim(' ', '-', '_');
        if (fitted.Length > maximumStemLength)
            fitted = fitted[..maximumStemLength].TrimEnd(' ', '-', '_', '.');
        var result = fitted + extension;
        var resultPath = Path.Combine(fullDirectory, result);
        if (resultPath.Length > PathGuard.BlockingLength)
            throw new UnsafePathException(resultPath, resultPath.Length,
                "O nome não pôde ser ajustado ao limite operacional.");
        return result;
    }

    public static string ApplyOfficialAbbreviations(string value)
    {
        var result = value;
        foreach (var (term, abbreviation) in OfficialAbbreviations)
            result = Regex.Replace(result, $@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(term)}(?![\p{{L}}\p{{N}}])",
                abbreviation, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return result;
    }

    [GeneratedRegex(@"(?:\b\d{7}-\d{2}(?:\.\d{4}\.\d\.\d{2}\.\d{4})?\b|\b[A-Za-z]{2,12}_\d{6,}(?:[-.]\d+)*\b|\b(?:\d{4}-\d{2}-\d{2}|\d{2}[-/.]\d{2}[-/.]\d{4})\b|\b[A-Z]{3}[0-9][A-Z0-9][0-9]{2}\b|\bID\s+[A-Z0-9]{4,}\b)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ImportantTokenRegex();

    [GeneratedRegex(@"\b(?:19|20)\d{2}-\d{2}-\d{2}\b", RegexOptions.CultureInvariant)]
    private static partial Regex IsoDateRegex();

    [GeneratedRegex(@"\s*(?:[-_]+|\s+)\s*")]
    private static partial Regex SeparatorRegex();
}
