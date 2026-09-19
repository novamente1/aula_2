using Organiza.Application.Services;
using Organiza.Domain.Operations;
using Organiza.Infrastructure.History;
using Organiza.Tests.Support;

namespace Organiza.Tests;

public sealed class HistoryAndWorkspacePolicyTests
{
    [Theory]
    [InlineData("arquivo.tmp")]
    [InlineData(".organiza_log.json.tmp")]
    [InlineData(".$documento.docx")]
    [InlineData("~$documento.docx")]
    [InlineData("processamento.lock")]
    public void TemporaryAndLockFiles_AreNeverInteractiveCandidates(string fileName)
    {
        using var directory = new TemporaryDirectory();
        var path = directory.PathFor(fileName);

        Assert.False(WorkspaceFilePolicy.IsInteractiveCandidate(directory.FullPath, path));
    }

    [Fact]
    public void InternalOrganizaDirectory_IsNeverAnInteractiveCandidate()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.PathFor(WorkspaceFilePolicy.InternalDirectoryName), "qualquer-arquivo.json");

        Assert.False(WorkspaceFilePolicy.IsInteractiveCandidate(directory.FullPath, path));
    }

    [Fact]
    public void GeneratedPdfPart_IsNeverAnInteractiveCandidate()
    {
        using var directory = new TemporaryDirectory();
        var path = directory.PathFor("processo_parte-07.pdf");

        Assert.False(WorkspaceFilePolicy.IsInteractiveCandidate(directory.FullPath, path));
    }

    [Theory]
    [InlineData("Documento.gdoc")]
    [InlineData("Planilha.gsheet")]
    [InlineData("Apresentação.gslides")]
    public void GoogleWorkspacePointers_AreNeverInteractiveCandidates(string fileName)
    {
        using var directory = new TemporaryDirectory();
        var path = directory.PathFor(fileName);

        Assert.True(WorkspaceFilePolicy.IsGoogleWorkspacePointer(path));
        Assert.False(WorkspaceFilePolicy.IsInteractiveCandidate(directory.FullPath, path));
    }

    [Fact]
    public async Task ConcurrentHistoryWrites_AreSerializedWithoutTemporaryFilesInRoot()
    {
        using var directory = new TemporaryDirectory();
        var stores = Enumerable.Range(0, 30).Select(_ => new JsonHistoryStore(directory.FullPath)).ToArray();

        await Task.WhenAll(stores.Select((store, index) => store.AppendAsync([
            new OperationLogEntry(DateTimeOffset.Now, "Teste concorrente", $"origem-{index}", null,
                OperationStatus.Completed)
        ])));

        var entries = await new JsonHistoryStore(directory.FullPath).ReadAsync();
        Assert.Equal(30, entries.Count(entry => entry.Operation == "Teste concorrente"));
        Assert.Empty(Directory.EnumerateFiles(directory.FullPath, "*.tmp", SearchOption.TopDirectoryOnly));
        var internalDirectory = directory.PathFor(WorkspaceFilePolicy.InternalDirectoryName);
        Assert.True(Directory.Exists(internalDirectory));
        Assert.Empty(Directory.EnumerateFiles(internalDirectory, "*.tmp", SearchOption.TopDirectoryOnly));
        Assert.False(File.Exists(Path.Combine(internalDirectory, "history.lock")));
        Assert.True(File.GetAttributes(internalDirectory).HasFlag(FileAttributes.Hidden));
        var historyPath = Path.Combine(internalDirectory, "historico.json");
        Assert.True(File.Exists(historyPath));
        Assert.True(File.GetAttributes(historyPath).HasFlag(FileAttributes.Hidden));
    }

    [Fact]
    public async Task LegacyRootTemporaryLog_IsMigratedIntoHiddenInternalArea()
    {
        using var directory = new TemporaryDirectory();
        var legacy = directory.PathFor(JsonHistoryStore.FileName + ".tmp");
        await File.WriteAllTextAsync(legacy, "[]");
        var store = new JsonHistoryStore(directory.FullPath);

        await store.AppendAsync([
            new OperationLogEntry(DateTimeOffset.Now, "Migração", directory.FullPath, null,
                OperationStatus.Completed)
        ]);

        Assert.False(File.Exists(legacy));
        var internalDirectory = directory.PathFor(WorkspaceFilePolicy.InternalDirectoryName);
        var migrated = Assert.Single(Directory.EnumerateFiles(internalDirectory, "legacy-history-*.json.tmp"));
        Assert.True(File.GetAttributes(migrated).HasFlag(FileAttributes.Hidden));
    }
}
