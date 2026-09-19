using Organiza.Domain.Organization;

namespace Organiza.Application.Services;

/// <summary>
/// Última barreira antes da tela de revisão. Uma sugestão sem identidade
/// documental compreensível nunca pode parecer pronta para aplicação.
/// </summary>
public static class OfficialFileNameQualityGate
{
    private static readonly string[] InvalidTypes =
    [
        "documento", "documento digitalizado", "não determinado", "nao determinado",
        "não identificado", "nao identificado", "arquivo"
    ];

    public static IReadOnlyList<RenameSuggestion> Apply(IReadOnlyList<RenameSuggestion> suggestions) =>
        suggestions.Select(Validate).ToArray();

    public static bool IsMeaningfulDocumentType(string? documentType)
    {
        if (string.IsNullOrWhiteSpace(documentType)) return false;
        var normalized = StandardFolderManager.Normalize(documentType);
        if (InvalidTypes.Any(invalid => string.Equals(normalized,
                StandardFolderManager.Normalize(invalid), StringComparison.Ordinal)) ||
            normalized.StartsWith("NAO IDENTIFICADO", StringComparison.Ordinal)) return false;
        if (normalized is "RG" or "CPF" or "CNH" or "CRLV") return true;
        return documentType.Count(char.IsLetter) >= 3;
    }

    private static RenameSuggestion Validate(RenameSuggestion suggestion)
    {
        if (string.Equals(suggestion.ClassificationRule, "REVISAO-QUALIDADE-OCR",
                StringComparison.OrdinalIgnoreCase)) return suggestion;
        if (IsMeaningfulDocumentType(suggestion.DocumentType) && HasReadableDescription(suggestion.SuggestedName))
            return suggestion;

        return suggestion with
        {
            SuggestedName = Path.GetFileName(suggestion.OriginalPath),
            IsSelected = false,
            DestinationFolder = StandardFolders.QualityReview,
            ClassificationRule = "REVISAO-NOME-INCOMPLETO",
            NamingProtocolStatus = "Bloqueado: o tipo documental não foi comprovado; número e data isolados não formam um nome válido.",
            QualityStatus = "Revisão humana obrigatória — sugestão automática não aplicada.",
            Reason = "REVISAO-NOME-INCOMPLETO: o sistema preservou o nome atual porque não comprovou uma descrição documental compreensível."
        };
    }

    private static bool HasReadableDescription(string suggestedName)
    {
        var stem = Path.GetFileNameWithoutExtension(suggestedName);
        var segments = stem.Split(" - ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length < 2) return false;
        var description = segments[0].All(char.IsDigit) ? segments[1] : segments[0];
        return description.Count(char.IsLetter) >= 2;
    }
}
