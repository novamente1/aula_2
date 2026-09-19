using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Organiza.Application.Abstractions;
using Organiza.Application.Services;
using Organiza.Domain.Organization;

namespace Organiza.Infrastructure.History;

public sealed class JsonDocumentCatalogStore : IDocumentCatalogStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };
    private readonly string _internalDirectory;
    public string CatalogPath { get; }

    public JsonDocumentCatalogStore(string rootPath)
    {
        _internalDirectory = Path.Combine(Path.GetFullPath(rootPath), WorkspaceFilePolicy.InternalDirectoryName);
        CatalogPath = Path.Combine(_internalDirectory, "catalogo_documental.json");
    }

    public async Task SaveAsync(DocumentCatalogSnapshot catalog, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_internalDirectory);
        File.SetAttributes(_internalDirectory,
            File.GetAttributes(_internalDirectory) | FileAttributes.Hidden | FileAttributes.System);
        var temporaryPath = Path.Combine(_internalDirectory, $"catalogo-{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write,
                             FileShare.None, 64 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                var bom = Encoding.UTF8.GetPreamble();
                await stream.WriteAsync(bom, cancellationToken);
                await JsonSerializer.SerializeAsync(stream, catalog, Options, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }
            File.Move(temporaryPath, CatalogPath, overwrite: true);
            File.SetAttributes(CatalogPath, File.GetAttributes(CatalogPath) | FileAttributes.Hidden);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                try { File.Delete(temporaryPath); } catch { }
            }
        }
    }
}
