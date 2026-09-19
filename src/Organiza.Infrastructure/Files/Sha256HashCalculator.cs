using System.Security.Cryptography;
using Organiza.Application.Abstractions;

namespace Organiza.Infrastructure.Files;

public sealed class Sha256HashCalculator(IFileSystem fileSystem) : IHashCalculator
{
    public async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken = default)
    {
        await using var stream = fileSystem.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash);
    }
}
