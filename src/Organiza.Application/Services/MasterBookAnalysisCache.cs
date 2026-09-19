using System.Text.Json;
using System.Text.Json.Serialization;
using System.Security.Cryptography;
using System.Text;
using System.Collections.Concurrent;
using Organiza.Application.Abstractions;
using Organiza.Domain.Files;
using Organiza.Domain.MasterBook;
using Organiza.Domain.Organization;

namespace Organiza.Application.Services;

public sealed record MasterBookAnalysisCacheResult(
    IReadOnlyList<DocumentAnalysis> CachedAnalyses,
    IReadOnlyList<FileItem> FilesRequiringAnalysis)
{
    public int CacheHits => CachedAnalyses.Count;
}

public sealed class MasterBookAnalysisCache(IFileSystem fileSystem, IHashCalculator hashCalculator)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task<MasterBookAnalysisCacheResult> LoadValidAsync(
        string rootPath,
        IReadOnlyList<FileItem> files,
        CancellationToken cancellationToken = default)
    {
        string? selectedRootPath = null;
        MasterSourceDocument[] sources = [];
        foreach (var cachePath in new[]
                 {
                     Path.Combine(rootPath, MasterBookFileNames.Json),
                     GetLocalCachePath(rootPath)
                 }.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!fileSystem.FileExists(cachePath)) continue;
            try
            {
                await using var stream = fileSystem.OpenRead(cachePath);
                using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
                var root = json.RootElement;
                selectedRootPath = root.GetProperty("SelectedRootPath").GetString();
                sources = root.GetProperty("SourceDocuments").EnumerateArray()
                    .Select(element => element.Deserialize<MasterSourceDocument>(JsonOptions))
                    .Where(source => source is not null)
                    .Cast<MasterSourceDocument>()
                    .ToArray();
                if (sources.Length > 0) break;
            }
            catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
            {
                selectedRootPath = null;
                sources = [];
            }
        }

        if (string.IsNullOrWhiteSpace(selectedRootPath) || !string.Equals(Path.GetFullPath(selectedRootPath),
                Path.GetFullPath(rootPath), StringComparison.OrdinalIgnoreCase))
            return new([], files);

        var sourcesByHash = sources
            .GroupBy(source => source.Sha256, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var cached = new ConcurrentBag<DocumentAnalysis>();
        var missing = new ConcurrentBag<FileItem>();
        await Parallel.ForEachAsync(files, new ParallelOptions
        {
            MaxDegreeOfParallelism = 2,
            CancellationToken = cancellationToken
        }, async (file, token) =>
        {
            var hash = await hashCalculator.ComputeSha256Async(file.FullPath, token);
            if (!sourcesByHash.TryGetValue(hash, out var source) || source.SizeBytes != file.SizeBytes)
            {
                missing.Add(file);
                return;
            }

            cached.Add(new DocumentAnalysis(file, source.Sha256, source.ExtractedText,
                source.ExtractionMethod, source.ProcessIdentifiers, source.DocumentDates,
                source.PageCount, source.PagesWithText, source.OcrPages, source.ExtractionComplete,
                source.ExtractionWarnings));
        });

        var fileOrder = files.Select((file, index) => (file.FullPath, index))
            .ToDictionary(item => item.FullPath, item => item.index, StringComparer.OrdinalIgnoreCase);
        return new(
            cached.OrderBy(item => fileOrder[item.File.FullPath]).ToArray(),
            missing.OrderBy(item => fileOrder[item.FullPath]).ToArray());
    }

    public static void SaveLocalCopy(IFileSystem fileSystem, string rootPath, byte[] jsonContent)
    {
        try
        {
            var path = GetLocalCachePath(rootPath);
            fileSystem.CreateDirectory(Path.GetDirectoryName(path)!);
            fileSystem.WriteAllBytes(path, jsonContent);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // O relatório principal já foi salvo na pasta. A cópia local é apenas uma otimização resiliente.
        }
    }

    private static string GetLocalCachePath(string rootPath)
    {
        var normalized = Path.GetFullPath(rootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .ToUpperInvariant();
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Organiza", "master-cache", $"{key}.json");
    }
}
