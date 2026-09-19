namespace Organiza.Tests.Support;

public sealed class TemporaryDirectory : IDisposable
{
    public TemporaryDirectory()
    {
        FullPath = Path.Combine(Path.GetTempPath(), "Organiza.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(FullPath);
    }

    public string FullPath { get; }
    public string PathFor(string name) => Path.Combine(FullPath, name);

    public void Dispose()
    {
        if (Directory.Exists(FullPath)) Directory.Delete(FullPath, recursive: true);
    }
}
