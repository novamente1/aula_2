using System.Text.RegularExpressions;
using Organiza.Domain.MasterBook;
using Organiza.Domain.Organization;

namespace Organiza.Application.Services;

/// <summary>
/// Mantém as áreas de preservação e os artefatos do próprio Organiza fora dos
/// fluxos interativos. O Livro Mestre usa sua própria política porque precisa
/// inventariar recursivamente a pasta protegida.
/// </summary>
public static partial class WorkspaceFilePolicy
{
    public const string InternalDirectoryName = ".organiza";
    private static readonly HashSet<string> GoogleWorkspacePointerExtensions = new(
        [".gdoc", ".gsheet", ".gslides", ".gdraw", ".gform", ".gmap", ".gsite", ".gjam"],
        StringComparer.OrdinalIgnoreCase);
    private static readonly string[] DuplicateFolderNames =
        [StandardFolders.Duplicates, "12 DUPLICADOS", "12 Documentos duplicados"];

    public static bool IsInteractiveCandidate(string selectedRoot, string path) =>
        IsInsideRoot(selectedRoot, path) &&
        !IsInsideOperationalFolder(selectedRoot, path) &&
        !IsInsideInternalFolder(selectedRoot, path) &&
        !IsGeneratedPdfPart(path) &&
        !IsGoogleWorkspacePointer(path) &&
        !IsGeneratedArtifact(path);

    public static bool IsGoogleWorkspacePointer(string path) =>
        GoogleWorkspacePointerExtensions.Contains(Path.GetExtension(path));

    public static bool IsPdfSplitCandidate(string selectedRoot, string path) =>
        IsInteractiveCandidate(selectedRoot, path) &&
        string.Equals(Path.GetExtension(path), ".pdf", StringComparison.OrdinalIgnoreCase) &&
        !IsGeneratedPdfPart(path);

    public static bool IsGeneratedPdfPart(string path) =>
        GeneratedPartRegex().IsMatch(Path.GetFileName(path));

    public static bool IsInsideOperationalFolder(string selectedRoot, string path)
    {
        if (!IsInsideRoot(selectedRoot, path)) return false;
        var relative = Path.GetRelativePath(Path.GetFullPath(selectedRoot), Path.GetFullPath(path));
        var firstSegment = relative.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return firstSegment is not null &&
               (IsDuplicateFolderName(firstSegment) ||
                StandardFolderManager.IsEquivalentFolder(firstSegment, StandardFolders.ContextReview) ||
                StandardFolderManager.IsEquivalentFolder(firstSegment, StandardFolders.QualityReview) ||
                StandardFolderManager.IsEquivalentFolder(firstSegment, StandardFolders.Originals));
    }

    public static bool IsInsideDuplicatesFolder(string selectedRoot, string path)
    {
        if (!IsInsideRoot(selectedRoot, path)) return false;
        var relative = Path.GetRelativePath(Path.GetFullPath(selectedRoot), Path.GetFullPath(path));
        var firstSegment = relative.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return firstSegment is not null && IsDuplicateFolderName(firstSegment);
    }

    public static bool IsInsideOriginalsFolder(string selectedRoot, string path) =>
        IsInsideNamedTopLevelFolder(selectedRoot, path, StandardFolders.Originals);

    private static bool IsInsideNamedTopLevelFolder(string selectedRoot, string path, string canonicalFolder)
    {
        if (!IsInsideRoot(selectedRoot, path)) return false;
        var relative = Path.GetRelativePath(Path.GetFullPath(selectedRoot), Path.GetFullPath(path));
        var firstSegment = relative.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return firstSegment is not null &&
               StandardFolderManager.IsEquivalentFolder(firstSegment, canonicalFolder);
    }

    private static bool IsDuplicateFolderName(string value) => DuplicateFolderNames.Any(name =>
        StandardFolderManager.Normalize(value) == StandardFolderManager.Normalize(name));

    public static bool IsInsideInternalFolder(string selectedRoot, string path)
    {
        if (!IsInsideRoot(selectedRoot, path)) return false;
        var relative = Path.GetRelativePath(Path.GetFullPath(selectedRoot), Path.GetFullPath(path));
        var firstSegment = relative.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return string.Equals(firstSegment, InternalDirectoryName, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsGeneratedArtifact(string path)
    {
        var name = Path.GetFileName(path);
        return string.Equals(name, ".organiza_log.json", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(name, MappingIndexService.FileName, StringComparison.OrdinalIgnoreCase) ||
               MasterBookFileNames.IsMarkdown(name) ||
               string.Equals(name, MasterBookFileNames.Json, StringComparison.OrdinalIgnoreCase) ||
               IsTemporaryOrLockFileName(name);
    }

    public static bool IsTemporaryOrLockFileName(string name) =>
        name.StartsWith(".$", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("~$", StringComparison.OrdinalIgnoreCase) ||
        name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) ||
        name.EndsWith(".lock", StringComparison.OrdinalIgnoreCase);

    public static bool IsInsideRoot(string selectedRoot, string path)
    {
        var root = Path.GetFullPath(selectedRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var candidate = Path.GetFullPath(path);
        return candidate.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    [GeneratedRegex(@"_parte-\d+(?:_\d+)?\.pdf$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex GeneratedPartRegex();
}
