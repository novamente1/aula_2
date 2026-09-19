namespace Organiza.Domain.Selection;

public enum FolderOrganizationMode
{
    OrganizeFilesOnly,
    ApplyStandardStructure
}

public sealed record FolderSelection(
    string RootPath,
    bool IncludeSubfolders,
    FolderOrganizationMode Mode = FolderOrganizationMode.OrganizeFilesOnly);

public sealed record StandardFolderInspection(
    bool HasAllStandardFolders,
    IReadOnlyList<string> ExistingFolders,
    IReadOnlyList<string> MissingFolders);

public sealed record ContextMismatchCandidate(
    string FullPath,
    string DetectedContext,
    string ExpectedContext,
    string Reason);

public sealed record EquivalentFolderGroup(
    string CanonicalName,
    IReadOnlyList<string> FolderPaths);

public sealed record WorkspaceValidationResult(
    string? PrimaryContext,
    IReadOnlyList<ContextMismatchCandidate> ContextMismatches,
    IReadOnlyList<EquivalentFolderGroup> EquivalentFolderGroups,
    IReadOnlyList<string> EmptyFolders)
{
    public bool HasContextContamination => ContextMismatches.Count > 0;
    public bool NeedsHygiene => EquivalentFolderGroups.Count > 0 || EmptyFolders.Count > 0;
}
