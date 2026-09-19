using Organiza.Application.Services;
using Organiza.Domain.Organization;
using Organiza.Infrastructure.Files;
using Organiza.Tests.Support;

namespace Organiza.Tests;

public sealed class WorkspacePreAnalysisTests
{
    [Fact]
    public async Task Inspect_FlagsFilesFromAProcessDifferentFromTheRootDossier()
    {
        using var parent = new TemporaryDirectory();
        var root = Directory.CreateDirectory(parent.PathFor(
            "Processo 0000003-00.2026.8.00.0003 PESSOA EXEMPLO USUCAPIAO")).FullName;
        await File.WriteAllTextAsync(Path.Combine(root,
            "peticao 0000003-00.2026.8.00.0003.pdf"), "principal");
        await File.WriteAllTextAsync(Path.Combine(root,
            "ATOrd_0000002-00.2026.5.00.0002_parte-01.pdf"), "trabalhista");
        var originals = Directory.CreateDirectory(Path.Combine(root, "99 ORIGINAIS")).FullName;
        await File.WriteAllTextAsync(Path.Combine(originals,
            "ATOrd_0000002-00.2026.5.00.0002.pdf"), "trabalhista original");

        var result = new WorkspacePreAnalysisService(new PhysicalFileSystem())
            .Inspect(root, recursive: true);

        Assert.Equal("Processo 0000003-00.2026.8.00.0003", result.PrimaryContext);
        Assert.Single(result.ContextMismatches);
        Assert.All(result.ContextMismatches, item =>
            Assert.Contains("0000002-00.2026.5.00.0002", item.DetectedContext));
    }

    [Fact]
    public async Task Inspect_AllowsRelatedProcesses_WhenRootDoesNotDeclareSingleProcess()
    {
        using var directory = new TemporaryDirectory();
        await File.WriteAllTextAsync(directory.PathFor("ATOrd_0000002-00.2026.5.00.0002.pdf"), "trabalhista");
        await File.WriteAllTextAsync(directory.PathFor("Usucapião 0000003-00.2026.8.00.0003.pdf"), "posse do mesmo bem");

        var result = new WorkspacePreAnalysisService(new PhysicalFileSystem())
            .Inspect(directory.FullPath, recursive: true);

        Assert.False(result.HasContextContamination);
        Assert.Empty(result.ContextMismatches);
    }

    [Fact]
    public void Inspect_FindsEquivalentFoldersAndUnusedEmptyCategories()
    {
        using var directory = new TemporaryDirectory();
        Directory.CreateDirectory(directory.PathFor("02 ARREMATAÇÃO E JUDICIAL"));
        Directory.CreateDirectory(directory.PathFor("02 arrematacao antiga"));
        var removable = Directory.CreateDirectory(directory.PathFor("rascunhos vazios")).FullName;
        var structural = Directory.CreateDirectory(directory.PathFor("04")).FullName;

        var result = new WorkspacePreAnalysisService(new PhysicalFileSystem())
            .Inspect(directory.FullPath, recursive: true);

        Assert.Single(result.EquivalentFolderGroups);
        Assert.Contains(removable, result.EmptyFolders, StringComparer.OrdinalIgnoreCase);
        Assert.Contains(structural, result.EmptyFolders, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Cleaner_PreviewsAndRemovesNestedEmptyTree_ButPreservesStructuralFolders()
    {
        using var directory = new TemporaryDirectory();
        var parent = Directory.CreateDirectory(directory.PathFor("temporaria")).FullName;
        var child = Directory.CreateDirectory(Path.Combine(parent, "filha")).FullName;
        var structural = Directory.CreateDirectory(directory.PathFor("99 ORIGINAIS")).FullName;
        var canonicalCategory = Directory.CreateDirectory(directory.PathFor(StandardFolders.Improvements)).FullName;
        var photos = Directory.CreateDirectory(directory.PathFor(StandardFolders.Photos)).FullName;
        var unusedCategory = Directory.CreateDirectory(directory.PathFor("04")).FullName;
        var cleaner = new EmptyFolderCleaner(new PhysicalFileSystem());

        var preview = cleaner.FindEmptyFolders(directory.FullPath);
        var removed = cleaner.RemoveEmptyFolders(directory.FullPath);

        Assert.Contains(parent, preview, StringComparer.OrdinalIgnoreCase);
        Assert.Contains(child, removed, StringComparer.OrdinalIgnoreCase);
        Assert.Contains(parent, removed, StringComparer.OrdinalIgnoreCase);
        Assert.Contains(unusedCategory, removed, StringComparer.OrdinalIgnoreCase);
        Assert.True(Directory.Exists(structural));
        Assert.True(Directory.Exists(canonicalCategory));
        Assert.True(Directory.Exists(photos));
    }
}
