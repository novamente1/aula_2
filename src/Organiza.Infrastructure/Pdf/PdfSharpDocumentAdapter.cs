using System.Diagnostics;
using Organiza.Application.Abstractions;
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;

namespace Organiza.Infrastructure.Pdf;

public sealed class PdfSharpDocumentAdapter : IPdfDocumentAdapter
{
    private const int FileBufferBytes = 1024 * 1024;
    private static readonly TimeSpan RenderTimeout = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan StructuralSplitTimeout = TimeSpan.FromMinutes(10);
    private const string PikePdfSplitScript =
        "import pikepdf,sys; s=pikepdf.open(sys.argv[1]); o=pikepdf.Pdf.new(); " +
        "a=int(sys.argv[3]); n=int(sys.argv[4]); o.pages.extend(s.pages[a:a+n]); " +
        "o.save(sys.argv[2], object_stream_mode=pikepdf.ObjectStreamMode.generate, compress_streams=True)";
    private const string PikePdfCountScript =
        "import pikepdf,sys; p=pikepdf.open(sys.argv[1]); print(len(p.pages))";
    private readonly string? _pdfToPpm = ExecutableLocator.Find("pdftoppm.exe");
    private readonly string? _pdfInfo = ExecutableLocator.Find("pdfinfo.exe");
    private readonly string? _python = ExecutableLocator.Find("python.exe");

    public int GetPageCount(string sourcePath)
    {
        var externalCount = TryGetPageCountWithPdfInfo(sourcePath) ?? TryGetPageCountWithPikePdf(sourcePath);
        if (externalCount > 0) return externalCount.Value;

        if (new FileInfo(sourcePath).Length > Organiza.Domain.Files.PdfSafetyLimits.ProtectedModeThresholdBytes)
            throw new IOException(
                "O PDF excede 50 MiB e a contagem protegida de páginas não está disponível. " +
                "Instale o Poppler (pdfinfo) ou o Python com pikepdf; o arquivo não foi carregado na memória e permanece intacto.");

        using var input = OpenBufferedSource(sourcePath);
        using var document = PdfReader.Open(input, PdfDocumentOpenMode.Import);
        return document.PageCount;
    }

    private int? TryGetPageCountWithPdfInfo(string sourcePath)
    {
        if (_pdfInfo is null) return null;
        var output = RunForOutput(_pdfInfo, [sourcePath], TimeSpan.FromMinutes(2));
        if (output is null) return null;
        foreach (var line in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (!line.StartsWith("Pages:", StringComparison.OrdinalIgnoreCase)) continue;
            if (int.TryParse(line["Pages:".Length..].Trim(), out var count) && count > 0) return count;
        }
        return null;
    }

    private int? TryGetPageCountWithPikePdf(string sourcePath)
    {
        if (_python is null) return null;
        var output = RunForOutput(_python, ["-c", PikePdfCountScript, sourcePath], TimeSpan.FromMinutes(2));
        return int.TryParse(output?.Trim(), out var count) && count > 0 ? count : null;
    }

    private static string? RunForOutput(string executable, IReadOnlyList<string> arguments, TimeSpan timeout)
    {
        var startInfo = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
        try
        {
            using var process = new Process { StartInfo = startInfo };
            if (!process.Start()) return null;
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit((int)timeout.TotalMilliseconds))
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                return null;
            }
            _ = error.GetAwaiter().GetResult();
            return process.ExitCode == 0 ? output.GetAwaiter().GetResult() : null;
        }
        catch
        {
            return null;
        }
    }

    public void RenderPagesToFile(string sourcePath, int firstPageIndex, int pageCount, string destinationPath)
    {
        if (TryRenderPagesWithPikePdf(sourcePath, firstPageIndex, pageCount, destinationPath))
            return;

        using var input = OpenBufferedSource(sourcePath);
        using var source = PdfReader.Open(input, PdfDocumentOpenMode.Import);
        if (firstPageIndex < 0 || pageCount <= 0 || firstPageIndex + pageCount > source.PageCount)
            throw new ArgumentOutOfRangeException(nameof(pageCount));

        using var output = new PdfDocument();
        for (var index = firstPageIndex; index < firstPageIndex + pageCount; index++)
        {
            output.AddPage(source.Pages[index]);
        }

        using var stream = new FileStream(destinationPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            bufferSize: 1024 * 1024, FileOptions.SequentialScan);
        output.Save(stream, closeStream: false);
        stream.Flush(flushToDisk: true);
    }

    private bool TryRenderPagesWithPikePdf(
        string sourcePath,
        int firstPageIndex,
        int pageCount,
        string destinationPath)
    {
        if (_python is null) return false;

        var startInfo = new ProcessStartInfo(_python)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in new[]
                 {
                     "-c", PikePdfSplitScript, sourcePath, destinationPath,
                     firstPageIndex.ToString(System.Globalization.CultureInfo.InvariantCulture),
                     pageCount.ToString(System.Globalization.CultureInfo.InvariantCulture)
                 })
        {
            startInfo.ArgumentList.Add(argument);
        }

        try
        {
            using var process = new Process { StartInfo = startInfo };
            if (!process.Start()) return false;
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            if (!process.WaitForExit((int)StructuralSplitTimeout.TotalMilliseconds))
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                DeleteIfExists(destinationPath);
                return false;
            }

            if (process.ExitCode == 0 && File.Exists(destinationPath) && new FileInfo(destinationPath).Length > 0)
                return true;
        }
        catch
        {
            // A dependência local é opcional; o importador PdfSharp permanece como fallback.
        }

        DeleteIfExists(destinationPath);
        return false;
    }

    public bool TryRenderSinglePageWithinLimit(
        string sourcePath,
        int pageIndex,
        long maximumBytes,
        string destinationPath)
    {
        if (_pdfToPpm is null || maximumBytes <= 0) return false;

        var scratchRoot = Path.Combine(Path.GetTempPath(), "Organiza", "PdfRaster");
        Directory.CreateDirectory(scratchRoot);
        var token = Guid.NewGuid().ToString("N");
        var prefix = Path.Combine(scratchRoot, token);
        var imagePath = prefix + ".jpg";
        var trialPdf = prefix + ".pdf";
        try
        {
            // Começa com boa legibilidade e reduz gradualmente apenas quando a
            // página importada carrega recursos globais gigantes do PDF original.
            var profiles = new[]
            {
                (Dpi: 180, Quality: 88),
                (Dpi: 150, Quality: 82),
                (Dpi: 120, Quality: 76),
                (Dpi: 96, Quality: 70),
                (Dpi: 72, Quality: 62)
            };

            foreach (var profile in profiles)
            {
                DeleteIfExists(imagePath);
                DeleteIfExists(trialPdf);
                var exitCode = RunPdfToPpm(sourcePath, pageIndex, prefix, profile.Dpi, profile.Quality);
                if (exitCode != 0 || !File.Exists(imagePath)) continue;

                CreateImagePdf(sourcePath, pageIndex, imagePath, trialPdf);
                var size = new FileInfo(trialPdf).Length;
                if (size <= 0 || size > maximumBytes) continue;

                File.Move(trialPdf, destinationPath);
                return true;
            }

            return false;
        }
        finally
        {
            DeleteIfExists(imagePath);
            DeleteIfExists(trialPdf);
        }
    }

    private int RunPdfToPpm(string sourcePath, int pageIndex, string outputPrefix, int dpi, int quality)
    {
        var startInfo = new ProcessStartInfo(_pdfToPpm!)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in new[]
                 {
                     "-f", (pageIndex + 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
                     "-l", (pageIndex + 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
                     "-singlefile", "-jpeg", "-r", dpi.ToString(System.Globalization.CultureInfo.InvariantCulture),
                     "-jpegopt", $"quality={quality}", sourcePath, outputPrefix
                 })
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start()) return -1;
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        if (process.WaitForExit((int)RenderTimeout.TotalMilliseconds)) return process.ExitCode;
        try { process.Kill(entireProcessTree: true); } catch { }
        return -1;
    }

    private static void CreateImagePdf(
        string sourcePath,
        int pageIndex,
        string imagePath,
        string destinationPath)
    {
        using var sourceStream = OpenBufferedSource(sourcePath);
        using var source = PdfReader.Open(sourceStream, PdfDocumentOpenMode.Import);
        var sourcePage = source.Pages[pageIndex];
        using var output = new PdfDocument();
        var page = output.AddPage();
        page.Width = sourcePage.Width;
        page.Height = sourcePage.Height;
        page.Orientation = sourcePage.Orientation;
        using (var image = XImage.FromFile(imagePath))
        using (var graphics = XGraphics.FromPdfPage(page))
        {
            graphics.DrawImage(image, 0, 0, page.Width.Point, page.Height.Point);
        }

        using var stream = new FileStream(destinationPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            FileBufferBytes, FileOptions.SequentialScan);
        output.Save(stream, closeStream: false);
        stream.Flush(flushToDisk: true);
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path)) File.Delete(path);
    }

    private static FileStream OpenBufferedSource(string sourcePath) =>
        new(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, FileBufferBytes, FileOptions.RandomAccess);
}
