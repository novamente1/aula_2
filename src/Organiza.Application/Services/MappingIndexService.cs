using System.Text;
using Organiza.Application.Abstractions;
using Organiza.Domain.Organization;

namespace Organiza.Application.Services;

public sealed record MappingIndexEntry(
    string? PrincipalId,
    string DocumentType,
    string FinalPath);

public sealed class MappingIndexService(IFileSystem fileSystem)
{
    public const string FileName = "INDICE_MAPEAMENTO.txt";

    public string Append(string rootPath, IReadOnlyList<MappingIndexEntry> entries)
    {
        var indexDirectory = ResolveDirectory(rootPath);
        fileSystem.CreateDirectory(indexDirectory);
        var indexPath = Path.Combine(indexDirectory, FileName);
        var existing = ReadExisting(indexPath);
        var lines = entries.Select(entry => string.Join(" | ",
            entry.PrincipalId ?? string.Empty,
            entry.DocumentType,
            Path.GetFileName(entry.FinalPath),
            Path.GetRelativePath(rootPath, Path.GetDirectoryName(entry.FinalPath)!)))
            .Where(line => !existing.Contains(line, StringComparer.OrdinalIgnoreCase))
            .ToArray();
        if (lines.Length == 0 && fileSystem.FileExists(indexPath)) return indexPath;

        var content = string.Join(Environment.NewLine, existing.Concat(lines));
        if (content.Length > 0) content += Environment.NewLine;
        var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
        fileSystem.WriteAllBytes(indexPath, encoding.GetPreamble().Concat(encoding.GetBytes(content)).ToArray());
        return indexPath;
    }

    private string ResolveDirectory(string rootPath)
    {
        var rootName = Path.GetFileName(Path.GetFullPath(rootPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (StandardFolderManager.Normalize(rootName) == StandardFolderManager.Normalize("00 TRIAGEM"))
            return Path.Combine(rootPath, "06 LOGs");

        var directDirectories = fileSystem.EnumerateDirectories(rootPath).ToArray();
        var triage = directDirectories.FirstOrDefault(path =>
            StandardFolderManager.Normalize(Path.GetFileName(path)) ==
            StandardFolderManager.Normalize("00 TRIAGEM"));
        if (triage is not null) return Path.Combine(triage, "06 LOGs");

        var profile = FolderStructureDetector.Detect(rootPath,
            directDirectories.Select(Path.GetFileName).OfType<string>().ToArray());
        if (profile is FolderStructureProfile.DrivePortfolio or FolderStructureProfile.PropertyPortfolioRoot)
            return Path.Combine(rootPath, "00 TRIAGEM", "06 LOGs");

        // Não cria uma falsa Nova Raiz dentro de um processo ou dossiê isolado.
        return Path.Combine(rootPath, WorkspaceFilePolicy.InternalDirectoryName);
    }

    private IReadOnlyList<string> ReadExisting(string path)
    {
        if (!fileSystem.FileExists(path)) return [];
        using var stream = fileSystem.OpenRead(path);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd().Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries);
    }
}
