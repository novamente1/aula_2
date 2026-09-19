namespace Organiza.Application.Services;

public sealed class RootProtectionGuard
{
    public void EnsureInternalFileOperation(string selectedRoot, string sourcePath, string destinationPath)
    {
        var root = Normalize(selectedRoot);
        var source = Path.GetFullPath(sourcePath);
        var destination = Path.GetFullPath(destinationPath);
        var rootPrefix = root + Path.DirectorySeparatorChar;

        if (string.Equals(source.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), root,
                StringComparison.OrdinalIgnoreCase) ||
            string.Equals(destination.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), root,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("A pasta raiz selecionada é protegida e nunca pode ser renomeada.");
        }

        if (!source.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase) ||
            !destination.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("A operação foi bloqueada porque sairia da pasta raiz selecionada.");
        }
    }

    private static string Normalize(string path) => Path.GetFullPath(path)
        .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}
