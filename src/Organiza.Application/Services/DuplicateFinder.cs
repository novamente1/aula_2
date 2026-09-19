using Organiza.Application.Abstractions;
using Organiza.Domain.Files;
using System.Collections.Concurrent;

namespace Organiza.Application.Services;

public sealed record DuplicateScanProgress(int Current, int Total, string Message);

public sealed class DuplicateFinder(IFileSystem fileSystem, IHashCalculator hashCalculator)
{
    public async Task<IReadOnlyList<DuplicateGroup>> FindAsync(
        string rootPath,
        bool recursive,
        IProgress<DuplicateScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var sizeGroups = fileSystem.EnumerateFiles(rootPath, option)
            .Where(path => WorkspaceFilePolicy.IsInteractiveCandidate(rootPath, path))
            .Select(path => new FileItem(path, fileSystem.GetFileLength(path), Path.GetExtension(path)))
            .GroupBy(file => file.SizeBytes)
            .Where(group => group.Count() > 1)
            .SelectMany(group => group)
            .ToArray();

        if (sizeGroups.Length == 0) return [];
        var completed = 0;
        var hashed = new ConcurrentBag<(FileItem File, string Hash)>();
        progress?.Report(new(0, sizeGroups.Length,
            $"{sizeGroups.Length} arquivo(s) com tamanho repetido exigem confirmação SHA-256."));
        await Parallel.ForEachAsync(sizeGroups, new ParallelOptions
        {
            MaxDegreeOfParallelism = 2,
            CancellationToken = cancellationToken
        }, async (file, token) =>
        {
            var hash = await hashCalculator.ComputeSha256Async(file.FullPath, token);
            hashed.Add((file, hash));
            var done = Interlocked.Increment(ref completed);
            progress?.Report(new(done, sizeGroups.Length, $"SHA-256: {Path.GetFileName(file.FullPath)}"));
        });

        var result = new List<DuplicateGroup>();
        foreach (var group in hashed
                     .GroupBy(item => (item.File.SizeBytes, item.Hash))
                     .Where(group => group.Count() > 1))
        {
            var files = group.Select(item => item.File).ToArray();
            var principal = files
                .OrderBy(file => file.FullPath.Count(character => character is '\\' or '/'))
                .ThenBy(file => file.Name.Length)
                .ThenBy(file => file.FullPath, StringComparer.OrdinalIgnoreCase)
                .First();
            var candidates = files
                .Select(file => new DuplicateCandidate(file, file == principal))
                .ToArray();
            result.Add(new DuplicateGroup(group.Key.SizeBytes, group.Key.Hash, candidates));
        }

        return result.OrderByDescending(group => group.SizeBytes).ToArray();
    }
}
