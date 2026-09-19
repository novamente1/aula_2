using Organiza.Application.Abstractions;
using Organiza.Domain.Organization;

namespace Organiza.Application.Services;

public sealed class EmptyFolderCleaner(IFileSystem fileSystem)
{
    public IReadOnlyList<string> FindEmptyFolders(string rootPath)
    {
        var root = Path.GetFullPath(rootPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var directories = fileSystem.EnumerateDirectories(root, SearchOption.AllDirectories)
            .Select(Path.GetFullPath)
            .ToArray();
        var directoriesWithFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in fileSystem.EnumerateFiles(root, SearchOption.AllDirectories))
        {
            var current = Path.GetDirectoryName(Path.GetFullPath(file));
            while (!string.IsNullOrWhiteSpace(current) &&
                   current.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                if (!directoriesWithFiles.Add(current)) break;
                current = Path.GetDirectoryName(current);
            }
        }

        return directories
            .OrderByDescending(path => path.Length)
            .Where(directory =>
            {
                var name = Path.GetFileName(directory);
                return !DriveStructureCatalog.IsProtectedFolderName(name) &&
                       !string.Equals(name, "logs", StringComparison.OrdinalIgnoreCase) &&
                       !name.StartsWith(".organiza", StringComparison.OrdinalIgnoreCase) &&
                       !directoriesWithFiles.Contains(directory);
            })
            .ToArray();
    }

    public IReadOnlyList<string> RemoveEmptyFolders(string rootPath)
    {
        var removed = new List<string>();
        foreach (var directory in FindEmptyFolders(rootPath).OrderByDescending(path => path.Length))
        {
            if (!fileSystem.DirectoryExists(directory) ||
                fileSystem.EnumerateFiles(directory, SearchOption.TopDirectoryOnly).Any() ||
                fileSystem.EnumerateDirectories(directory).Any()) continue;
            fileSystem.DeleteDirectory(directory);
            removed.Add(directory);
        }
        return removed;
    }
}
