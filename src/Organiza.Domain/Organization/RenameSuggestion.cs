namespace Organiza.Domain.Organization;

public sealed record RenameSuggestion(
    string OriginalPath,
    string SuggestedName,
    bool IsSelected = true,
    string? Reason = null,
    string? DestinationFolder = null,
    string? ClassificationRule = null,
    string? DocumentType = null,
    string? NamingProtocolStatus = null,
    string? QualityStatus = null,
    string? PrincipalId = null,
    string? SecondaryId = null);

public sealed record RenameBatchResult(
    int Selected,
    int Completed,
    int Skipped,
    int Failed,
    IReadOnlyList<LockedFileIssue> LockedFiles,
    string? MappingIndexPath = null);

public sealed record LockedFileIssue(
    string Path,
    string Message);
