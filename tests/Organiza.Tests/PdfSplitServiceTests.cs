using System.Security.Cryptography;
using Organiza.Application.Abstractions;
using Organiza.Application.Common;
using Organiza.Application.Services;
using Organiza.Domain.Operations;
using Organiza.Infrastructure.Files;
using Organiza.Infrastructure.History;
using Organiza.Tests.Support;

namespace Organiza.Tests;

public sealed class PdfSplitServiceTests
{
    [Fact]
    public void DefaultSafetyLimit_IsExactly97MiB() =>
        Assert.Equal(97L * 1024 * 1024, Organiza.Domain.Files.PdfSafetyLimits.PartSizeBytes);

    [Fact]
    public void ProtectedModeThreshold_IsExactly50MiB() =>
        Assert.Equal(50L * 1024 * 1024, Organiza.Domain.Files.PdfSafetyLimits.ProtectedModeThresholdBytes);

    [Fact]
    public async Task Split_UsesMeasuredOutputSize_AndPreservesIdenticalOriginal()
    {
        using var directory = new TemporaryDirectory();
        var source = directory.PathFor("Processo.pdf");
        var originalBytes = Enumerable.Range(0, 200).Select(number => (byte)number).ToArray();
        await File.WriteAllBytesAsync(source, originalBytes);
        var fs = new PhysicalFileSystem();
        var hash = new Sha256HashCalculator(fs);
        var service = new PdfSplitService(
            fs,
            hash,
            new SizedFakePdfAdapter([60, 60, 30, 70]),
            new StandardFolderManager(fs),
            new PathGuard(),
            new JsonHistoryStore(directory.FullPath),
            new EmptyFolderCleaner(fs));

        var result = await service.SplitAsync(
            source,
            directory.FullPath,
            ExplicitApproval.Grant("teste"),
            maximumPartBytes: 100);

        Assert.False(File.Exists(source));
        Assert.True(File.Exists(result.PreservedOriginalPath));
        Assert.Equal(SHA256.HashData(originalBytes), SHA256.HashData(await File.ReadAllBytesAsync(result.PreservedOriginalPath)));
        Assert.Equal(3, result.Parts.Count);
        Assert.All(result.Parts, part => Assert.InRange(part.SizeBytes, 1, 100));
        Assert.Equal(new[] { (1, 1), (2, 3), (4, 4) }, result.Parts.Select(part => (part.FirstPage, part.LastPage)));
        Assert.DoesNotContain(Directory.EnumerateDirectories(directory.FullPath, "*", SearchOption.AllDirectories),
            path => Path.GetFileName(path).StartsWith(".organiza-pages-", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(new[] { "99 ORIGINAIS" }, Directory.EnumerateDirectories(directory.FullPath)
            .Where(path => !File.GetAttributes(path).HasFlag(FileAttributes.Hidden))
            .Select(Path.GetFileName).OrderBy(name => name).ToArray());
        Assert.All(result.Parts, part => Assert.Equal(part.SizeBytes, new FileInfo(part.FullPath).Length));
    }

    [Fact]
    public async Task Split_InSubfolderWithoutStandardStructure_CreatesOnlyOriginalsDestination()
    {
        using var directory = new TemporaryDirectory();
        var selectedSubfolder = Directory.CreateDirectory(directory.PathFor("subpasta selecionada")).FullName;
        var source = Path.Combine(selectedSubfolder, "grande.pdf");
        await File.WriteAllBytesAsync(source, Enumerable.Repeat((byte)42, 20).ToArray());
        var fs = new PhysicalFileSystem();
        var service = new PdfSplitService(fs, new Sha256HashCalculator(fs), new SizedFakePdfAdapter([8, 8, 8]),
            new StandardFolderManager(fs), new PathGuard(), new JsonHistoryStore(selectedSubfolder), new EmptyFolderCleaner(fs));

        var result = await service.SplitAsync(source, selectedSubfolder,
            ExplicitApproval.Grant("usuário recusou estrutura completa e confirmou apenas a divisão"), 10);

        Assert.True(File.Exists(result.PreservedOriginalPath));
        Assert.Equal(new[] { "99 ORIGINAIS" }, Directory.EnumerateDirectories(selectedSubfolder)
            .Where(path => !File.GetAttributes(path).HasFlag(FileAttributes.Hidden))
            .Select(Path.GetFileName).ToArray());
        Assert.DoesNotContain(Directory.EnumerateDirectories(selectedSubfolder), path =>
            Path.GetFileName(path) is "01" or "02" or "03" or "04" or "05" or "06" or "07" or "08" or "98 DUPLICADOS" or "FOTOS");
    }

    [Fact]
    public async Task Split_ReusesAnIdenticalOriginalAlreadyPreserved()
    {
        using var directory = new TemporaryDirectory();
        var originals = Directory.CreateDirectory(directory.PathFor("99 ORIGINAIS")).FullName;
        var source = directory.PathFor("processo.pdf");
        var preserved = Path.Combine(originals, "processo.pdf");
        var content = Enumerable.Repeat((byte)21, 24).ToArray();
        await File.WriteAllBytesAsync(source, content);
        await File.WriteAllBytesAsync(preserved, content);
        var fs = new PhysicalFileSystem();
        var service = new PdfSplitService(fs, new Sha256HashCalculator(fs), new SizedFakePdfAdapter([8, 8]),
            new StandardFolderManager(fs), new PathGuard(), new JsonHistoryStore(directory.FullPath),
            new EmptyFolderCleaner(fs));

        var result = await service.SplitAsync(source, directory.FullPath,
            ExplicitApproval.Grant("teste"), maximumPartBytes: 10);

        Assert.Equal(preserved, result.PreservedOriginalPath);
        Assert.Single(Directory.EnumerateFiles(originals));
        Assert.Equal(content, await File.ReadAllBytesAsync(preserved));
    }

    [Fact]
    public async Task Split_AcceptsAnOversizedSinglePage_AsItsOwnPart()
    {
        using var directory = new TemporaryDirectory();
        var source = directory.PathFor("pagina-gigante.pdf");
        await File.WriteAllBytesAsync(source, Enumerable.Repeat((byte)7, 32).ToArray());
        var fs = new PhysicalFileSystem();
        var service = new PdfSplitService(fs, new Sha256HashCalculator(fs),
            new SizedFakePdfAdapter([150, 40, 40]), new StandardFolderManager(fs), new PathGuard(),
            new JsonHistoryStore(directory.FullPath), new EmptyFolderCleaner(fs));

        var result = await service.SplitAsync(source, directory.FullPath,
            ExplicitApproval.Grant("teste de página unitária gigante"), maximumPartBytes: 100);

        Assert.Equal(2, result.Parts.Count);
        Assert.Equal((1, 1, 150L),
            (result.Parts[0].FirstPage, result.Parts[0].LastPage, result.Parts[0].SizeBytes));
        Assert.Equal((2, 3, 80L),
            (result.Parts[1].FirstPage, result.Parts[1].LastPage, result.Parts[1].SizeBytes));
        Assert.True(File.Exists(result.PreservedOriginalPath));
    }

    [Fact]
    public async Task Split_RasterizesOversizedImportedPage_WhenAdapterCanFitIt()
    {
        using var directory = new TemporaryDirectory();
        var source = directory.PathFor("recursos-globais.pdf");
        await File.WriteAllBytesAsync(source, Enumerable.Repeat((byte)9, 32).ToArray());
        var fs = new PhysicalFileSystem();
        var service = new PdfSplitService(fs, new Sha256HashCalculator(fs),
            new CompressingFakePdfAdapter([150, 40], compressedSize: 75),
            new StandardFolderManager(fs), new PathGuard(), new JsonHistoryStore(directory.FullPath),
            new EmptyFolderCleaner(fs));

        var result = await service.SplitAsync(source, directory.FullPath,
            ExplicitApproval.Grant("teste"), maximumPartBytes: 100);

        Assert.Equal(2, result.Parts.Count);
        Assert.Equal(75, result.Parts[0].SizeBytes);
        Assert.True(result.Parts[0].Rasterized);
        Assert.False(result.Parts[1].Rasterized);
        Assert.All(result.Parts, part => Assert.InRange(part.SizeBytes, 1, 100));
    }

    [Fact]
    public async Task Split_WithoutApproval_DoesNotTouchOriginal()
    {
        using var directory = new TemporaryDirectory();
        var source = directory.PathFor("Processo.pdf");
        await File.WriteAllBytesAsync(source, [1, 2, 3]);
        var fs = new PhysicalFileSystem();
        var service = new PdfSplitService(fs, new Sha256HashCalculator(fs), new SizedFakePdfAdapter([2, 2]),
            new StandardFolderManager(fs), new PathGuard(), new JsonHistoryStore(directory.FullPath), new EmptyFolderCleaner(fs));

        await Assert.ThrowsAsync<ApprovalRequiredException>(() =>
            service.SplitAsync(source, directory.FullPath, ExplicitApproval.Denied("teste"), 3));

        Assert.True(File.Exists(source));
        Assert.False(Directory.Exists(directory.PathFor("99 ORIGINAIS")));
    }

    [Fact]
    public async Task Split_WhenSourceIsLocked_ReturnsFriendlyErrorAndWritesFailureHistory()
    {
        using var directory = new TemporaryDirectory();
        var source = directory.PathFor("drive-bloqueado.pdf");
        await File.WriteAllBytesAsync(source, [1, 2, 3, 4]);
        var fs = new PhysicalFileSystem();
        var history = new JsonHistoryStore(directory.FullPath);
        var service = new PdfSplitService(fs, new Sha256HashCalculator(fs), new SizedFakePdfAdapter([2, 2]),
            new StandardFolderManager(fs), new PathGuard(), history, new EmptyFolderCleaner(fs));
        await using var lockStream = new FileStream(source, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var exception = await Assert.ThrowsAsync<PdfSplitOperationException>(() => service.SplitAsync(
            source, directory.FullPath, ExplicitApproval.Grant("teste"), maximumPartBytes: 3));

        Assert.Contains("bloqueado", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Google Drive", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(source));
        var entries = await history.ReadAsync();
        Assert.Contains(entries, entry => entry.Operation == "Falha ao dividir PDF" && entry.Status == OperationStatus.Failed);
    }

    [Fact]
    public async Task Split_WhenPdfProcessorFails_PreservesSourceAndWritesFailureHistory()
    {
        using var directory = new TemporaryDirectory();
        var source = directory.PathFor("pdf-incompativel.pdf");
        await File.WriteAllBytesAsync(source, [1, 2, 3, 4]);
        var fs = new PhysicalFileSystem();
        var history = new JsonHistoryStore(directory.FullPath);
        var service = new PdfSplitService(fs, new Sha256HashCalculator(fs), new FailingPdfAdapter(),
            new StandardFolderManager(fs), new PathGuard(), history, new EmptyFolderCleaner(fs));

        var exception = await Assert.ThrowsAsync<PdfSplitOperationException>(() => service.SplitAsync(
            source, directory.FullPath, ExplicitApproval.Grant("teste"), maximumPartBytes: 3));

        Assert.Contains("incompatível", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(source));
        Assert.Empty(Directory.EnumerateFiles(directory.FullPath, "*_parte-*.pdf", SearchOption.AllDirectories));
        var entries = await history.ReadAsync();
        Assert.Contains(entries, entry => entry.Operation == "Falha ao dividir PDF" && entry.Status == OperationStatus.Failed);
    }

    [Fact]
    public async Task Split_WhenLaterPartFails_RemovesAllPartsAlreadyCreatedFromRoot()
    {
        using var directory = new TemporaryDirectory();
        var source = directory.PathFor("lote.pdf");
        await File.WriteAllBytesAsync(source, [1, 2, 3, 4]);
        var fs = new PhysicalFileSystem();
        var service = new PdfSplitService(fs, new Sha256HashCalculator(fs), new FailOnSecondPartAdapter(),
            new StandardFolderManager(fs), new PathGuard(), new JsonHistoryStore(directory.FullPath),
            new EmptyFolderCleaner(fs));

        await Assert.ThrowsAsync<PdfSplitOperationException>(() => service.SplitAsync(
            source, directory.FullPath, ExplicitApproval.Grant("teste"), maximumPartBytes: 3));

        Assert.True(File.Exists(source));
        Assert.Empty(Directory.EnumerateFiles(directory.FullPath, "*_parte-*.pdf", SearchOption.TopDirectoryOnly));
    }

    [Fact]
    public async Task Split_RemovesPartsFromInterruptedAttempt_BeforeSafeRestart()
    {
        using var directory = new TemporaryDirectory();
        var source = directory.PathFor("documento.pdf");
        var stale = directory.PathFor("documento_parte-01.pdf");
        await File.WriteAllBytesAsync(source, [1, 2, 3, 4]);
        await File.WriteAllBytesAsync(stale, [9, 9, 9, 9, 9, 9, 9]);
        var fs = new PhysicalFileSystem();
        var history = new JsonHistoryStore(directory.FullPath);
        var service = new PdfSplitService(fs, new Sha256HashCalculator(fs), new SizedFakePdfAdapter([2, 2]),
            new StandardFolderManager(fs), new PathGuard(), history, new EmptyFolderCleaner(fs));

        var result = await service.SplitAsync(source, directory.FullPath,
            ExplicitApproval.Grant("reinício confirmado"), maximumPartBytes: 3);

        Assert.Equal(2, result.Parts.Count);
        Assert.Equal(2, result.Parts[0].SizeBytes);
        Assert.Contains(await history.ReadAsync(), entry => entry.Operation == "Limpar parte residual de PDF" &&
                                                            entry.Source == stale &&
                                                            entry.Status == OperationStatus.Completed);
    }

    [Fact]
    public async Task Split_UsesLargestFittingTail_EvenWhenSerializedSizeIsNonMonotonic()
    {
        using var directory = new TemporaryDirectory();
        var source = directory.PathFor("nao-monotono.pdf");
        await File.WriteAllBytesAsync(source, [1, 2, 3, 4]);
        var fs = new PhysicalFileSystem();
        var service = new PdfSplitService(fs, new Sha256HashCalculator(fs), new NonMonotonicTailAdapter(),
            new StandardFolderManager(fs), new PathGuard(), new JsonHistoryStore(directory.FullPath),
            new EmptyFolderCleaner(fs));

        var result = await service.SplitAsync(source, directory.FullPath,
            ExplicitApproval.Grant("teste"), maximumPartBytes: 10);

        var part = Assert.Single(result.Parts);
        Assert.Equal((1, 3), (part.FirstPage, part.LastPage));
        Assert.Equal(6, part.SizeBytes);
    }

    private sealed class SizedFakePdfAdapter(IReadOnlyList<int> pageSizes) : IPdfDocumentAdapter
    {
        public int GetPageCount(string sourcePath) => pageSizes.Count;

        public void RenderPagesToFile(string sourcePath, int firstPageIndex, int pageCount, string destinationPath)
        {
            var length = pageSizes.Skip(firstPageIndex).Take(pageCount).Sum();
            using var stream = new FileStream(destinationPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            stream.SetLength(length);
        }
    }

    private sealed class FailingPdfAdapter : IPdfDocumentAdapter
    {
        public int GetPageCount(string sourcePath) =>
            throw new InvalidOperationException("Estrutura PDF incompatível para teste.");

        public void RenderPagesToFile(string sourcePath, int firstPageIndex, int pageCount, string destinationPath) =>
            throw new InvalidOperationException("Não deveria renderizar após a falha de leitura.");
    }

    private sealed class CompressingFakePdfAdapter(IReadOnlyList<int> pageSizes, int compressedSize)
        : IPdfDocumentAdapter
    {
        public int GetPageCount(string sourcePath) => pageSizes.Count;

        public void RenderPagesToFile(string sourcePath, int firstPageIndex, int pageCount, string destinationPath)
        {
            using var stream = new FileStream(destinationPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            stream.SetLength(pageSizes.Skip(firstPageIndex).Take(pageCount).Sum());
        }

        public bool TryRenderSinglePageWithinLimit(
            string sourcePath, int pageIndex, long maximumBytes, string destinationPath)
        {
            using var stream = new FileStream(destinationPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            stream.SetLength(compressedSize);
            return compressedSize <= maximumBytes;
        }
    }

    private sealed class FailOnSecondPartAdapter : IPdfDocumentAdapter
    {
        public int GetPageCount(string sourcePath) => 2;

        public void RenderPagesToFile(string sourcePath, int firstPageIndex, int pageCount, string destinationPath)
        {
            if (firstPageIndex > 0) throw new IOException("Falha simulada na segunda parte.");
            using var stream = new FileStream(destinationPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            stream.SetLength(pageCount == 1 ? 2 : 4);
        }
    }

    private sealed class NonMonotonicTailAdapter : IPdfDocumentAdapter
    {
        public int GetPageCount(string sourcePath) => 3;

        public void RenderPagesToFile(string sourcePath, int firstPageIndex, int pageCount, string destinationPath)
        {
            var size = pageCount switch { 3 => 6, 2 => 20, _ => 2 };
            using var stream = new FileStream(destinationPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            stream.SetLength(size);
        }
    }
}
