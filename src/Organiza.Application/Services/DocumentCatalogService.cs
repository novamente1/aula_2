using Organiza.Application.Abstractions;
using Organiza.Domain.Organization;

namespace Organiza.Application.Services;

public sealed class DocumentCatalogService(IFileSystem fileSystem, IDocumentCatalogStore store)
{
    public string CatalogPath => store.CatalogPath;

    public async Task<DocumentCatalogSnapshot> SaveAsync(
        string rootPath,
        IReadOnlyList<DocumentAnalysis> analyses,
        IReadOnlyList<RenameSuggestion> suggestions,
        CancellationToken cancellationToken = default)
    {
        if (analyses.Count != suggestions.Count)
            throw new InvalidOperationException("O catálogo não pode ser criado porque análises e sugestões não correspondem.");

        var directNames = fileSystem.DirectoryExists(rootPath)
            ? fileSystem.EnumerateDirectories(rootPath).Select(Path.GetFileName).OfType<string>().ToArray()
            : [];
        var profile = FolderStructureDetector.Detect(rootPath, directNames);
        var byPath = suggestions.ToDictionary(item => item.OriginalPath, StringComparer.OrdinalIgnoreCase);
        var generatedAt = DateTimeOffset.Now;
        var entries = analyses.Select(analysis =>
        {
            var suggestion = byPath[analysis.File.FullPath];
            var sha = analysis.ContentSha256.ToUpperInvariant();
            var documentId = $"DOC-{sha[..Math.Min(20, sha.Length)]}";
            return new DocumentCatalogEntry(
                documentId,
                Path.GetRelativePath(rootPath, analysis.File.FullPath),
                analysis.File.Name,
                suggestion.SuggestedName,
                sha,
                analysis.File.SizeBytes,
                suggestion.DocumentType ?? "Não determinado",
                suggestion.DestinationFolder ?? string.Empty,
                suggestion.ClassificationRule ?? "CLASSIFICACAO-NAO-DETERMINADA",
                suggestion.Reason ?? "Sem justificativa disponível.",
                profile.ToString(),
                analysis.ExtractionMethod,
                analysis.ProcessIdentifiers,
                analysis.DocumentDates,
                analysis.ExtractionComplete,
                analysis.ExtractionWarnings ?? [],
                generatedAt);
        }).ToArray();
        var snapshot = new DocumentCatalogSnapshot("1.0", Path.GetFullPath(rootPath), generatedAt, entries);
        await store.SaveAsync(snapshot, cancellationToken);
        return snapshot;
    }
}
