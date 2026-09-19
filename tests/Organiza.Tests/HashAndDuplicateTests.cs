using Organiza.Application.Services;
using Organiza.Infrastructure.Files;
using Organiza.Tests.Support;

namespace Organiza.Tests;

public sealed class HashAndDuplicateTests
{
    [Fact]
    public async Task Sha256_UsesTheEntireFile()
    {
        using var directory = new TemporaryDirectory();
        var first = directory.PathFor("primeiro.bin");
        var second = directory.PathFor("segundo.bin");
        await File.WriteAllBytesAsync(first, [1, 2, 3, 4, 5, 6]);
        await File.WriteAllBytesAsync(second, [1, 2, 3, 4, 5, 7]);
        var fs = new PhysicalFileSystem();
        var hash = new Sha256HashCalculator(fs);

        Assert.NotEqual(await hash.ComputeSha256Async(first), await hash.ComputeSha256Async(second));
    }

    [Fact]
    public async Task DuplicateFinder_IgnoresNames_AndRequiresMatchingSizeAndHash()
    {
        using var directory = new TemporaryDirectory();
        await File.WriteAllTextAsync(directory.PathFor("nome A.txt"), "conteúdo idêntico");
        await File.WriteAllTextAsync(directory.PathFor("outro nome.dat"), "conteúdo idêntico");
        await File.WriteAllTextAsync(directory.PathFor("mesmo tamanho.txt"), "conteúdo diferente");
        var fs = new PhysicalFileSystem();
        var finder = new DuplicateFinder(fs, new Sha256HashCalculator(fs));

        var groups = await finder.FindAsync(directory.FullPath, recursive: false);

        var group = Assert.Single(groups);
        Assert.Equal(2, group.Files.Count);
        Assert.Contains(group.Files, file => file.File.Name == "nome A.txt");
        Assert.Contains(group.Files, file => file.File.Name == "outro nome.dat");
    }

    [Fact]
    public async Task DuplicateFinder_ExcludesOperationalFoldersAndGeneratedArtifacts()
    {
        using var directory = new TemporaryDirectory();
        var content = "mesmo conteúdo";
        await File.WriteAllTextAsync(directory.PathFor("principal.txt"), content);
        await File.WriteAllTextAsync(directory.PathFor("copia.txt"), content);
        var duplicates = Directory.CreateDirectory(directory.PathFor("98 duplicádos")).FullName;
        var originals = Directory.CreateDirectory(directory.PathFor("99 ORIGINAIS")).FullName;
        await File.WriteAllTextAsync(Path.Combine(duplicates, "quarentena.txt"), content);
        await File.WriteAllTextAsync(Path.Combine(originals, "preservado.txt"), content);
        await File.WriteAllTextAsync(directory.PathFor(".organiza_log.json"), content);
        var fs = new PhysicalFileSystem();

        var groups = await new DuplicateFinder(fs, new Sha256HashCalculator(fs))
            .FindAsync(directory.FullPath, recursive: true);

        var group = Assert.Single(groups);
        Assert.Equal(2, group.Files.Count);
        Assert.All(group.Files, file => Assert.Equal(directory.FullPath, Path.GetDirectoryName(file.File.FullPath)));
    }
}
