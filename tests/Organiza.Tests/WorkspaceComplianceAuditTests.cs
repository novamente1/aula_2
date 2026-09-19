using Organiza.Application.Services;
using Organiza.Domain.Organization;
using Organiza.Infrastructure.Files;
using Organiza.Tests.Support;

namespace Organiza.Tests;

public sealed class WorkspaceComplianceAuditTests
{
    [Fact]
    public async Task Audit_DetectsPortfolioRootWithMultipleDossiers()
    {
        using var directory = new TemporaryDirectory();
        Directory.CreateDirectory(directory.PathFor("V053 VENDIDO - P063 Veículo Exemplo ABC1D23"));
        Directory.CreateDirectory(directory.PathFor("V051 VENDIDO - P053 Exemplo"));
        var fs = new PhysicalFileSystem();

        var result = await new WorkspaceComplianceAuditService(fs, new Sha256HashCalculator(fs))
            .AuditAsync(directory.FullPath);

        Assert.Contains(result.Findings, finding =>
            finding.Code == "MULTIPLE_DOSSIERS_ROOT" && finding.Severity == ComplianceSeverity.Critical);
    }

    [Fact]
    public async Task Audit_DetectsTemporaryDuplicateAndIncompleteMasterBook()
    {
        using var directory = new TemporaryDirectory();
        foreach (var name in StandardFolders.All) Directory.CreateDirectory(directory.PathFor(name));
        var internalFolder = Directory.CreateDirectory(directory.PathFor(".organiza")).FullName;
        await File.WriteAllTextAsync(Path.Combine(internalFolder, "history.lock"), string.Empty);
        await File.WriteAllTextAsync(directory.PathFor("solto-a.txt"), "conteúdo igual");
        await File.WriteAllTextAsync(directory.PathFor("solto-b.txt"), "conteúdo igual");
        await File.WriteAllTextAsync(directory.PathFor("LIVRO MESTRE 360.md"), "relatório");
        var fs = new PhysicalFileSystem();

        var result = await new WorkspaceComplianceAuditService(fs, new Sha256HashCalculator(fs))
            .AuditAsync(directory.FullPath);

        Assert.Contains(result.Findings, finding => finding.Code == "TEMPORARY_FILES");
        Assert.Contains(result.Findings, finding => finding.Code == "EXACT_DUPLICATES");
        Assert.Contains(result.Findings, finding => finding.Code == "MASTER_BOOK_JSON_MISSING");
        Assert.Equal(1, result.DuplicateGroups);
    }

    [Fact]
    public async Task Audit_ReportsEveryFilePathAbove240AsCritical()
    {
        using var directory = new TemporaryDirectory();
        var longName = new string('a', 210) + ".txt";
        await File.WriteAllTextAsync(directory.PathFor(longName), "conteúdo");
        var fs = new PhysicalFileSystem();

        var result = await new WorkspaceComplianceAuditService(fs, new Sha256HashCalculator(fs))
            .AuditAsync(directory.FullPath);

        var finding = Assert.Single(result.Findings, item => item.Code == "PATH_OVER_240_FILE");
        Assert.Equal(ComplianceSeverity.Critical, finding.Severity);
        Assert.Contains(finding.Examples, example => example.Contains("caracteres", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Audit_RecognizesGoogleWorkspacePointerWithoutTreatingItAsDocumentContent()
    {
        using var directory = new TemporaryDirectory();
        await File.WriteAllTextAsync(directory.PathFor("Documento remoto.gdoc"), "ponteiro");
        await File.WriteAllTextAsync(directory.PathFor("Documento físico.txt"), "conteúdo");
        var fs = new PhysicalFileSystem();

        var result = await new WorkspaceComplianceAuditService(fs, new Sha256HashCalculator(fs))
            .AuditAsync(directory.FullPath);

        var finding = Assert.Single(result.Findings, item => item.Code == "GOOGLE_WORKSPACE_POINTER");
        Assert.Equal(ComplianceSeverity.Information, finding.Severity);
        Assert.Contains(finding.Examples, path => path.EndsWith("Documento remoto.gdoc"));
        Assert.Equal(2, result.FileCount);
        Assert.Equal(new FileInfo(directory.PathFor("Documento físico.txt")).Length, result.TotalBytes);
    }

    [Fact]
    public async Task LightweightAudit_DefersHashesForLargeFolderSelection()
    {
        using var directory = new TemporaryDirectory();
        await File.WriteAllTextAsync(directory.PathFor("a.txt"), "mesmo tamanho A");
        await File.WriteAllTextAsync(directory.PathFor("b.txt"), "mesmo tamanho B");
        var fs = new PhysicalFileSystem();

        var result = await new WorkspaceComplianceAuditService(fs, new ThrowingHashCalculator())
            .AuditAsync(directory.FullPath, includeContentHashes: false);

        Assert.Equal(0, result.DuplicateGroups);
        Assert.Contains(result.Findings, finding => finding.Code == "DUPLICATE_SCAN_DEFERRED");
    }

    [Fact]
    public async Task LightweightAudit_TraversesDeepSyntheticTreeWithoutHashingEveryFile()
    {
        using var directory = new TemporaryDirectory();
        const int folderCount = 25;
        const int filesPerFolder = 40;
        for (var folderIndex = 0; folderIndex < folderCount; folderIndex++)
        {
            var folder = Directory.CreateDirectory(directory.PathFor($"grupo-{folderIndex:00}")).FullName;
            for (var fileIndex = 0; fileIndex < filesPerFolder; fileIndex++)
                await File.WriteAllTextAsync(Path.Combine(folder, $"documento-{fileIndex:000}.txt"),
                    $"{folderIndex:00}-{fileIndex:000}");
        }
        var fs = new PhysicalFileSystem();

        var result = await new WorkspaceComplianceAuditService(fs, new ThrowingHashCalculator())
            .AuditAsync(directory.FullPath, includeContentHashes: false);

        Assert.Equal(folderCount * filesPerFolder, result.FileCount);
        Assert.Equal(folderCount, result.DirectoryCount);
        Assert.Contains(result.Findings, finding => finding.Code == "DUPLICATE_SCAN_DEFERRED");
    }

    private sealed class ThrowingHashCalculator : Organiza.Application.Abstractions.IHashCalculator
    {
        public Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("O hash não deveria ser calculado na seleção leve.");
    }
}
