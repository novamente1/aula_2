using Organiza.Application.Abstractions;

namespace Organiza.Infrastructure.Files;

public sealed class PhysicalFileSystem : IFileSystem
{
    public bool DirectoryExists(string path) => Directory.Exists(path);
    public bool FileExists(string path) => File.Exists(path);
    public void CreateDirectory(string path) => Directory.CreateDirectory(path);
    public IEnumerable<string> EnumerateDirectories(string path, SearchOption option = SearchOption.TopDirectoryOnly) =>
        Directory.EnumerateDirectories(path, "*", BuildOptions(option));
    public IEnumerable<string> EnumerateFiles(string path, SearchOption option) =>
        Directory.EnumerateFiles(path, "*", BuildOptions(option));
    public long GetFileLength(string path) => new FileInfo(path).Length;
    public Stream OpenRead(string path) => new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.SequentialScan);
    public void WriteAllBytes(string path, byte[] content) => File.WriteAllBytes(path, content);
    public void CopyFile(string source, string destination, bool overwrite = false) => File.Copy(source, destination, overwrite);
    public void MoveFile(string source, string destination) => File.Move(source, destination);
    public void ReplaceFile(string source, string destination) => File.Move(source, destination, overwrite: true);
    public void MoveDirectory(string source, string destination) => Directory.Move(source, destination);
    public void DeleteFile(string path) => File.Delete(path);
    public void DeleteDirectory(string path) => Directory.Delete(path, recursive: false);

    private static EnumerationOptions BuildOptions(SearchOption option) => new()
    {
        RecurseSubdirectories = option == SearchOption.AllDirectories,
        IgnoreInaccessible = true,
        ReturnSpecialDirectories = false,
        AttributesToSkip = 0,
        MatchCasing = MatchCasing.CaseInsensitive
    };
}
