namespace Organiza.Application.Services;

public static class DocumentPreviewPolicy
{
    private static readonly IReadOnlySet<string> BrowserFriendlyExtensions =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".pdf", ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".svg", ".txt"
        };

    public static bool ShouldUseBrowser(string filePath) =>
        BrowserFriendlyExtensions.Contains(Path.GetExtension(filePath));

    public static string ToLocalFileUri(string filePath) =>
        new Uri(Path.GetFullPath(filePath)).AbsoluteUri;
}
