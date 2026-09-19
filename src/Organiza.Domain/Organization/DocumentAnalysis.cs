using Organiza.Domain.Files;

namespace Organiza.Domain.Organization;

public enum TextExtractionMethod
{
    NotApplicable,
    PdfText,
    Ocr,
    MixedPdfTextAndOcr,
    OfficeOpenXml,
    PlainText,
    NoTextFound
}

public sealed record ExtractedText(
    string Text,
    TextExtractionMethod Method,
    int PageCount = 1,
    int PagesWithText = 1,
    int OcrPages = 0,
    bool IsComplete = true,
    IReadOnlyList<string>? Warnings = null);

public sealed record DocumentAnalysis(
    FileItem File,
    string ContentSha256,
    string FullText,
    TextExtractionMethod ExtractionMethod,
    IReadOnlyList<string> ProcessIdentifiers,
    IReadOnlyList<string> DocumentDates,
    int PageCount = 1,
    int PagesWithText = 1,
    int OcrPages = 0,
    bool ExtractionComplete = true,
    IReadOnlyList<string>? ExtractionWarnings = null)
{
    // Compatibilidade com adaptadores existentes. O valor agora contém o texto
    // integral; novos consumidores devem preferir FullText.
    public string FirstPageText => FullText;
}
