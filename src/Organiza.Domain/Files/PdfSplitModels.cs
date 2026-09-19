namespace Organiza.Domain.Files;

public static class PdfSafetyLimits
{
    public const long PartSizeBytes = 97L * 1024 * 1024;
    public const long ProtectedModeThresholdBytes = 50L * 1024 * 1024;
}

public sealed record PdfPart(
    string FullPath,
    int PartNumber,
    long SizeBytes,
    int FirstPage,
    int LastPage,
    bool Rasterized = false);

public sealed record PdfSplitResult(
    string OriginalPath,
    string PreservedOriginalPath,
    IReadOnlyList<PdfPart> Parts);
