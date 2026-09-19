using System.Text.RegularExpressions;
using Organiza.Domain.Organization;

namespace Organiza.Application.Services;

public static partial class BatchContextIsolationService
{
    public static IReadOnlyList<RenameSuggestion> Apply(
        IReadOnlyList<RenameSuggestion> suggestions,
        IReadOnlyList<DocumentAnalysis> analyses)
    {
        var analysisByPath = analyses.ToDictionary(item => item.File.FullPath,
            StringComparer.OrdinalIgnoreCase);
        var platesByPath = analyses.ToDictionary(item => item.File.FullPath,
            item => ExtractPlates($"{item.File.Name}\n{item.FullText}"),
            StringComparer.OrdinalIgnoreCase);
        var counts = platesByPath.Values.SelectMany(item => item)
            .GroupBy(item => item, StringComparer.OrdinalIgnoreCase)
            .Select(group => new { Plate = group.Key, Count = group.Count() })
            .OrderByDescending(item => item.Count).ThenBy(item => item.Plate).ToArray();
        if (counts.Length == 0 || counts[0].Count < 2 ||
            (counts.Length > 1 && counts[0].Count == counts[1].Count)) return suggestions;

        var principalPlate = counts[0].Plate;
        return suggestions.Select(suggestion =>
        {
            if (!analysisByPath.ContainsKey(suggestion.OriginalPath) ||
                string.Equals(suggestion.ClassificationRule, "REVISAO-QUALIDADE-OCR", StringComparison.OrdinalIgnoreCase))
                return suggestion;
            var documentPlates = platesByPath[suggestion.OriginalPath];
            if (documentPlates.Count == 0 || documentPlates.Contains(principalPlate, StringComparer.OrdinalIgnoreCase))
                return suggestion;
            return suggestion with
            {
                DestinationFolder = StandardFolders.ContextReview,
                ClassificationRule = "REVISAO-CONTEXTO-PLACA",
                Reason = $"Documento cita {string.Join(", ", documentPlates)}, enquanto a placa predominante do lote é {principalPlate}. Separação sugerida para conferência humana; nenhuma origem externa foi inferida."
            };
        }).ToArray();
    }

    private static IReadOnlyList<string> ExtractPlates(string value) => VehiclePlateRegex().Matches(value)
        .Cast<Match>().Select(match => match.Value.ToUpperInvariant())
        .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    [GeneratedRegex(@"\b[A-Z]{3}[0-9][A-Z0-9][0-9]{2}\b", RegexOptions.IgnoreCase)]
    private static partial Regex VehiclePlateRegex();
}
