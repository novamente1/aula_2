using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using Organiza.Application.Abstractions;
using Organiza.Domain.Organization;
using Organiza.Infrastructure.Pdf;

namespace Organiza.Infrastructure.Files;

public sealed class DeepDocumentTextExtractor(PdfFirstPageTextExtractor pdf) : IDocumentTextExtractor
{
    private static readonly HashSet<string> PlainTextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".json", ".csv", ".tsv", ".xml", ".html", ".htm", ".log", ".rtf"
    };

    public async Task<ExtractedText> ExtractAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        var extension = Path.GetExtension(filePath);
        if (string.Equals(extension, ".pdf", StringComparison.OrdinalIgnoreCase))
            return await pdf.ExtractAsync(filePath, cancellationToken);
        if (string.Equals(extension, ".docx", StringComparison.OrdinalIgnoreCase))
            return await ExtractDocxAsync(filePath, cancellationToken);
        if (PlainTextExtensions.Contains(extension))
        {
            var text = await File.ReadAllTextAsync(filePath, Encoding.UTF8, cancellationToken);
            return new(text, TextExtractionMethod.PlainText, 1,
                HasUsefulText(text) ? 1 : 0, IsComplete: true);
        }

        return new(string.Empty, TextExtractionMethod.NotApplicable, 0, 0, 0, true);
    }

    private static async Task<ExtractedText> ExtractDocxAsync(
        string filePath,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read,
            1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
        var entry = archive.GetEntry("word/document.xml");
        if (entry is null)
            return new(string.Empty, TextExtractionMethod.NoTextFound, 1, 0, 0, false,
                ["O DOCX não contém word/document.xml."]);
        await using var documentStream = entry.Open();
        var document = await XDocument.LoadAsync(documentStream, LoadOptions.PreserveWhitespace, cancellationToken);
        XNamespace word = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        var paragraphs = document.Descendants(word + "p")
            .Select(paragraph => string.Concat(paragraph.Descendants(word + "t").Select(node => node.Value)))
            .Where(value => !string.IsNullOrWhiteSpace(value));
        var text = string.Join(Environment.NewLine, paragraphs);
        return new(text, TextExtractionMethod.OfficeOpenXml, 1,
            HasUsefulText(text) ? 1 : 0, IsComplete: true);
    }

    private static bool HasUsefulText(string value) =>
        value.Count(char.IsLetterOrDigit) >= 4;
}
