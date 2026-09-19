using System.Diagnostics;
using System.Text;
using Organiza.Application.Abstractions;
using Organiza.Domain.Organization;

namespace Organiza.Infrastructure.Pdf;

public sealed class PdfFirstPageTextExtractor : IFirstPageTextExtractor
{
    private static readonly TimeSpan ToolTimeout = TimeSpan.FromMinutes(2);
    private readonly string? _pdfToText;
    private readonly string? _pdfToPpm;
    private readonly string? _tesseract;

    public PdfFirstPageTextExtractor()
    {
        _pdfToText = ExecutableLocator.Find("pdftotext.exe");
        _pdfToPpm = ExecutableLocator.Find("pdftoppm.exe");
        _tesseract = ExecutableLocator.Find("tesseract.exe",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Tesseract-OCR", "tesseract.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Tesseract-OCR", "tesseract.exe"));
    }

    internal PdfFirstPageTextExtractor(string? pdfToText, string? pdfToPpm, string? tesseract)
    {
        _pdfToText = pdfToText;
        _pdfToPpm = pdfToPpm;
        _tesseract = tesseract;
    }

    public bool NativeTextAvailable => _pdfToText is not null;
    public bool ManagedTextAvailable => true;
    public bool PdfRasterAvailable => _pdfToPpm is not null;
    public bool OcrAvailable => _pdfToPpm is not null && _tesseract is not null;

    public async Task<ExtractedText> ExtractAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (!string.Equals(Path.GetExtension(filePath), ".pdf", StringComparison.OrdinalIgnoreCase))
            return new(string.Empty, TextExtractionMethod.NotApplicable, 0, 0);

        var warnings = new List<string>();
        string[] pages = [];
        if (_pdfToText is not null)
        {
            var native = await RunAsync(_pdfToText,
                ["-enc", "UTF-8", "-layout", filePath, "-"], cancellationToken);
            if (native.ExitCode == 0)
                pages = SplitPages(native.StandardOutput);
        }

        if (pages.Length == 0 && new FileInfo(filePath).Length <= Organiza.Domain.Files.PdfSafetyLimits.ProtectedModeThresholdBytes)
        {
            try
            {
                using var document = UglyToad.PdfPig.PdfDocument.Open(filePath);
                pages = document.GetPages().Select(page => page.Text).ToArray();
            }
            catch (Exception exception)
            {
                warnings.Add($"A extração local de texto do PDF falhou: {exception.Message}");
            }
        }
        else if (pages.Length == 0)
        {
            warnings.Add("PDF acima de 50 MiB sem pdftotext: a extração gerenciada foi bloqueada para proteger a memória.");
        }

        if (pages.Length == 0)
            pages = Enumerable.Repeat(string.Empty, TryGetPageCount(filePath)).ToArray();

        var ocrPages = 0;
        for (var index = 0; index < pages.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (HasUsefulText(pages[index])) continue;
            if (_pdfToPpm is null || _tesseract is null)
            {
                warnings.Add($"Página {index + 1} sem texto; OCR indisponível.");
                continue;
            }

            var ocrText = await ExtractPageWithOcrAsync(filePath, index + 1, cancellationToken);
            if (HasUsefulText(ocrText))
            {
                pages[index] = ocrText;
                ocrPages++;
            }
            else
            {
                warnings.Add($"Página {index + 1} permaneceu sem texto após OCR.");
            }
        }

        var pagesWithText = pages.Count(HasUsefulText);
        var combined = string.Join(Environment.NewLine + Environment.NewLine,
            pages.Select((text, index) => $"--- PÁGINA {index + 1} ---{Environment.NewLine}{text.Trim()}"));
        var method = pagesWithText == 0
            ? TextExtractionMethod.NoTextFound
            : ocrPages == 0
                ? TextExtractionMethod.PdfText
                : pagesWithText == ocrPages
                    ? TextExtractionMethod.Ocr
                    : TextExtractionMethod.MixedPdfTextAndOcr;
        return new(combined, method, pages.Length, pagesWithText, ocrPages,
            pagesWithText == pages.Length, warnings);
    }

    private async Task<string> ExtractPageWithOcrAsync(
        string filePath,
        int pageNumber,
        CancellationToken cancellationToken)
    {
        var outputPrefix = Path.Combine(Path.GetTempPath(), $"organiza-ocr-{Guid.NewGuid():N}");
        var imagePath = outputPrefix + ".png";
        try
        {
            var page = pageNumber.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var render = await RunAsync(_pdfToPpm!,
                ["-f", page, "-l", page, "-singlefile", "-png", "-r", "250", filePath, outputPrefix],
                cancellationToken);
            if (render.ExitCode != 0 || !File.Exists(imagePath)) return string.Empty;
            var language = ResolveTesseractLanguage(_tesseract!);
            var ocr = await RunAsync(_tesseract!,
                [imagePath, "stdout", "-l", language, "--psm", "6"], cancellationToken);
            return ocr.ExitCode == 0 ? ocr.StandardOutput.Trim() : string.Empty;
        }
        finally
        {
            if (File.Exists(imagePath)) File.Delete(imagePath);
        }
    }

    private int TryGetPageCount(string filePath)
    {
        try
        {
            return new PdfSharpDocumentAdapter().GetPageCount(filePath);
        }
        catch
        {
            return 1;
        }
    }

    private static string[] SplitPages(string value)
    {
        var pages = value.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\f').ToList();
        if (pages.Count > 1 && string.IsNullOrWhiteSpace(pages[^1])) pages.RemoveAt(pages.Count - 1);
        return pages.Count == 0 ? [string.Empty] : pages.ToArray();
    }

    private static bool HasUsefulText(string value) => value.Count(character => char.IsLetterOrDigit(character)) >= 4;

    private static string ResolveTesseractLanguage(string tesseractPath)
    {
        var dataPath = Path.Combine(Path.GetDirectoryName(tesseractPath)!, "tessdata");
        var hasPortuguese = File.Exists(Path.Combine(dataPath, "por.traineddata"));
        var hasEnglish = File.Exists(Path.Combine(dataPath, "eng.traineddata"));
        if (hasPortuguese && hasEnglish) return "por+eng";
        if (hasPortuguese) return "por";
        return "eng";
    }

    private static async Task<ProcessResult> RunAsync(
        string executable,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start()) return new(-1, string.Empty, "Não foi possível iniciar a ferramenta.");
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ToolTimeout);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            throw;
        }

        return new(process.ExitCode, await outputTask, await errorTask);
    }

    private sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);
}

internal static class ExecutableLocator
{
    public static string? Find(string fileName, params string[] knownPaths)
    {
        foreach (var path in knownPaths.Where(File.Exists)) return path;

        var pathVariable = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var directory in pathVariable.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(directory.Trim().Trim('"'), fileName);
            if (File.Exists(candidate)) return candidate;
        }

        var wingetPackages = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft", "WinGet", "Packages");
        if (!Directory.Exists(wingetPackages)) return null;
        try
        {
            return Directory.EnumerateFiles(wingetPackages, fileName, SearchOption.AllDirectories).FirstOrDefault();
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }
}
