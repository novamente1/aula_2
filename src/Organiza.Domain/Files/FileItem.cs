namespace Organiza.Domain.Files;

public sealed record FileItem(string FullPath, long SizeBytes, string Extension)
{
    public string Name => Path.GetFileName(FullPath);
}
