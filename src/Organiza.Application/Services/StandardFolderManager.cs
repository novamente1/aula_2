using System.Globalization;
using System.Text;
using Organiza.Application.Abstractions;
using Organiza.Application.Common;
using Organiza.Domain.Operations;
using Organiza.Domain.Organization;
using Organiza.Domain.Selection;

namespace Organiza.Application.Services;

public sealed class StandardFolderManager(IFileSystem fileSystem)
{
    public StandardFolderInspection Inspect(string rootPath)
    {
        var existing = fileSystem.EnumerateDirectories(rootPath)
            .Select(Path.GetFileName)
            .Where(name => name is not null)
            .Cast<string>()
            .ToArray();
        var missing = StandardFolders.PropertyTemplate.Where(folder =>
            !existing.Any(candidate => IsEquivalentFolder(candidate, folder))).ToArray();
        return new StandardFolderInspection(missing.Length == 0, existing, missing);
    }

    public string ResolveOrCreate(string rootPath, string canonicalName, ExplicitApproval approval)
    {
        if (!approval.Granted)
        {
            throw new ApprovalRequiredException($"criar/reutilizar {canonicalName}");
        }

        var match = fileSystem.EnumerateDirectories(rootPath)
            .Where(path => IsEquivalentFolder(Path.GetFileName(path), canonicalName))
            .OrderBy(path => IsBareNumberedFolder(Path.GetFileName(path)) ? 1 : 0)
            .FirstOrDefault(path => !IsBareNumberedFolder(Path.GetFileName(path)));
        if (match is not null)
        {
            return match;
        }

        var path = Path.Combine(rootPath, canonicalName);
        fileSystem.CreateDirectory(path);
        return path;
    }

    public IReadOnlyList<string> CreateAllMissing(string rootPath, ExplicitApproval approval)
    {
        if (!approval.Granted)
        {
            throw new ApprovalRequiredException("criar pastas padrão");
        }

        return StandardFolders.PropertyTemplate.Select(folder => ResolveOrCreate(rootPath, folder, approval)).ToArray();
    }

    public IReadOnlyList<string> PrepareOptionalStructure(
        string rootPath,
        FolderOrganizationMode mode,
        ExplicitApproval approval)
    {
        if (mode == FolderOrganizationMode.OrganizeFilesOnly) return [];
        var folders = CreateAllMissing(rootPath, approval);
        ConsolidateSimilar(rootPath, approval);
        return folders;
    }

    public FolderConsolidationResult ConsolidateSimilar(string rootPath, ExplicitApproval approval)
    {
        if (!approval.Granted)
            throw new ApprovalRequiredException("unificar pastas similares");

        var root = Path.GetFullPath(rootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var movedFiles = new List<FolderConsolidationMove>();
        var removedFolders = new List<string>();
        var failures = new List<FolderConsolidationFailure>();
        var pathGuard = new PathGuard();

        foreach (var canonicalName in StandardFolders.All)
        {
            var canonicalPath = Path.Combine(root, canonicalName);
            var matches = fileSystem.EnumerateDirectories(root)
                .Where(path => !WorkspaceFilePolicy.IsInsideOperationalFolder(root, path) &&
                               !WorkspaceFilePolicy.IsInsideInternalFolder(root, path) &&
                               IsEquivalentFolder(Path.GetFileName(path), canonicalName))
                .OrderByDescending(path => path.Length)
                .ToArray();
            if (matches.Length == 0) continue;

            if (!fileSystem.DirectoryExists(canonicalPath)) fileSystem.CreateDirectory(canonicalPath);
            foreach (var sourceFolder in matches.Where(path =>
                         !string.Equals(Path.GetFullPath(path), Path.GetFullPath(canonicalPath), StringComparison.OrdinalIgnoreCase)))
            {
                foreach (var sourceFile in fileSystem.EnumerateFiles(sourceFolder, SearchOption.AllDirectories).ToArray())
                {
                    try
                    {
                        var desiredPath = Path.Combine(canonicalPath, Path.GetFileName(sourceFile));
                        var destination = GetUniqueFilePath(desiredPath);
                        pathGuard.EnsureAllowed(destination);
                        fileSystem.MoveFile(sourceFile, destination);
                        movedFiles.Add(new(sourceFile, destination));
                    }
                    catch (Exception exception) when (exception is not OperationCanceledException)
                    {
                        failures.Add(new(sourceFile, $"{exception.GetType().Name}: {exception.Message}"));
                    }
                }

                try
                {
                    RemoveEmptyTree(sourceFolder, removedFolders);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    failures.Add(new(sourceFolder, $"{exception.GetType().Name}: {exception.Message}"));
                }
            }
        }

        try
        {
            removedFolders.AddRange(new EmptyFolderCleaner(fileSystem).RemoveEmptyFolders(root));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            failures.Add(new(root, $"Limpeza final: {exception.GetType().Name}: {exception.Message}"));
        }
        return new(movedFiles, removedFolders.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), failures);
    }

    public static bool IsEquivalentFolder(string candidateName, string canonicalName)
    {
        if (Normalize(candidateName) == Normalize(canonicalName)) return true;
        if (!StandardFolders.Aliases.TryGetValue(canonicalName, out var aliases)) return false;
        var normalizedCandidate = Normalize(candidateName);
        return aliases.Any(alias =>
        {
            var normalizedAlias = Normalize(alias);
            return normalizedCandidate == normalizedAlias ||
                   normalizedCandidate.StartsWith(normalizedAlias + " ", StringComparison.Ordinal);
        });
    }

    public static string Normalize(string value)
    {
        var decomposed = value.Normalize(NormalizationForm.FormD);
        var withoutAccents = new string(decomposed
            .Where(character => CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            .ToArray());
        return withoutAccents.Normalize(NormalizationForm.FormC).Trim().ToUpperInvariant();
    }

    private string GetUniqueFilePath(string desiredPath)
    {
        if (!fileSystem.FileExists(desiredPath)) return desiredPath;
        var directory = Path.GetDirectoryName(desiredPath)!;
        var stem = Path.GetFileNameWithoutExtension(desiredPath);
        var extension = Path.GetExtension(desiredPath);
        for (var index = 2; ; index++)
        {
            var candidate = Path.Combine(directory,
                PathNameShortener.FitFileName(directory, $"{stem}_{index}{extension}"));
            if (!fileSystem.FileExists(candidate)) return candidate;
        }
    }

    private void RemoveEmptyTree(string sourceFolder, ICollection<string> removed)
    {
        var directories = fileSystem.EnumerateDirectories(sourceFolder, SearchOption.AllDirectories)
            .OrderByDescending(path => path.Length)
            .Append(sourceFolder)
            .ToArray();
        foreach (var directory in directories)
        {
            if (!fileSystem.DirectoryExists(directory) ||
                fileSystem.EnumerateFiles(directory, SearchOption.TopDirectoryOnly).Any() ||
                fileSystem.EnumerateDirectories(directory).Any()) continue;
            fileSystem.DeleteDirectory(directory);
            removed.Add(directory);
        }
    }

    private static bool IsBareNumberedFolder(string value) =>
        value.Trim().Length == 2 && int.TryParse(value.Trim(), out var number) && number is >= 1 and <= 9;
}

public sealed record FolderConsolidationMove(string SourcePath, string DestinationPath);

public sealed record FolderConsolidationFailure(string SourcePath, string Details);

public sealed record FolderConsolidationResult(
    IReadOnlyList<FolderConsolidationMove> MovedFiles,
    IReadOnlyList<string> RemovedFolders,
    IReadOnlyList<FolderConsolidationFailure> Failures);
