using Organiza.Domain.Files;
using Organiza.Domain.Operations;
using Organiza.Domain.Organization;

namespace Organiza.Application.Abstractions;

public interface IFileSystem
{
    bool DirectoryExists(string path);
    bool FileExists(string path);
    void CreateDirectory(string path);
    IEnumerable<string> EnumerateDirectories(string path, SearchOption option = SearchOption.TopDirectoryOnly);
    IEnumerable<string> EnumerateFiles(string path, SearchOption option);
    long GetFileLength(string path);
    Stream OpenRead(string path);
    void WriteAllBytes(string path, byte[] content);
    void CopyFile(string source, string destination, bool overwrite = false);
    void MoveFile(string source, string destination);
    void ReplaceFile(string source, string destination);
    void MoveDirectory(string source, string destination);
    void DeleteFile(string path);
    void DeleteDirectory(string path);
}

public interface IHashCalculator
{
    Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken = default);
}

public interface IHistoryStore
{
    Task AppendAsync(IEnumerable<OperationLogEntry> entries, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OperationLogEntry>> ReadAsync(CancellationToken cancellationToken = default);
}

public interface IPdfDocumentAdapter
{
    int GetPageCount(string sourcePath);
    void RenderPagesToFile(string sourcePath, int firstPageIndex, int pageCount, string destinationPath);

    bool TryRenderSinglePageWithinLimit(
        string sourcePath,
        int pageIndex,
        long maximumBytes,
        string destinationPath) => false;
}

public interface IContentSuggestionGateway
{
    Task<IReadOnlyList<RenameSuggestion>> GenerateAsync(
        IReadOnlyList<DocumentAnalysis> documents,
        CancellationToken cancellationToken = default);
}

public interface IDocumentTextExtractor
{
    Task<ExtractedText> ExtractAsync(string filePath, CancellationToken cancellationToken = default);
}

public interface IFirstPageTextExtractor : IDocumentTextExtractor;

public interface IDocumentCatalogStore
{
    string CatalogPath { get; }
    Task SaveAsync(DocumentCatalogSnapshot catalog, CancellationToken cancellationToken = default);
}
