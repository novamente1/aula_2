using System.Text.Json;
using System.Text.RegularExpressions;

namespace Organiza.Infrastructure.Files;

public sealed partial class GoogleDriveFolderLinkResolver
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _mappingPath;

    public GoogleDriveFolderLinkResolver(string? mappingPath = null)
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Organiza");
        Directory.CreateDirectory(directory);
        _mappingPath = mappingPath is null
            ? Path.Combine(directory, "google-drive-pastas.json")
            : Path.GetFullPath(mappingPath);
    }

    public bool IsGoogleDriveFolderUrl(string value) => TryExtractFolderId(value, out _);

    public string? Resolve(string url)
    {
        if (!TryExtractFolderId(url, out var id)) return null;
        var mappings = ReadMappings();
        return mappings.TryGetValue(id, out var path) && Directory.Exists(path)
            ? Path.GetFullPath(path)
            : null;
    }

    public void Remember(string url, string localFolderPath)
    {
        if (!TryExtractFolderId(url, out var id))
            throw new ArgumentException("O endereço não é um link de pasta do Google Drive.", nameof(url));
        var fullPath = Path.GetFullPath(localFolderPath);
        if (!Directory.Exists(fullPath)) throw new DirectoryNotFoundException(fullPath);
        var mappings = ReadMappings();
        mappings[id] = fullPath;
        var temporary = _mappingPath + $".{Guid.NewGuid():N}.tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(mappings, JsonOptions));
        File.Move(temporary, _mappingPath, overwrite: true);
    }

    private Dictionary<string, string> ReadMappings()
    {
        if (!File.Exists(_mappingPath)) return new(StringComparer.Ordinal);
        try
        {
            var parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(
                File.ReadAllText(_mappingPath), JsonOptions);
            return parsed is null
                ? new(StringComparer.Ordinal)
                : new(parsed, StringComparer.Ordinal);
        }
        catch (JsonException)
        {
            return new(StringComparer.Ordinal);
        }
    }

    private static bool TryExtractFolderId(string value, out string id)
    {
        id = string.Empty;
        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Host, "drive.google.com", StringComparison.OrdinalIgnoreCase)) return false;
        var match = FolderPathRegex().Match(uri.AbsolutePath);
        if (!match.Success) return false;
        id = match.Groups[1].Value;
        return true;
    }

    [GeneratedRegex(@"^/drive/(?:u/\d+/)?folders/([A-Za-z0-9_-]{10,})(?:/|$)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex FolderPathRegex();
}
