using Organiza.Domain.Organization;

namespace Organiza.Application.Services;

public sealed class FileLockInspector
{
    public LockedFileIssue? CheckWriteAccess(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath)) return null;
        try
        {
            using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.ReadWrite,
                FileShare.None, bufferSize: 1, FileOptions.None);
            return null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            var name = Path.GetFileName(fullPath);
            return new(fullPath,
                $"ALERTA: O arquivo [{name}] está aberto ou em uso por outro aplicativo. " +
                $"Feche-o para concluir a operação. Caminho: {fullPath}");
        }
    }
}
