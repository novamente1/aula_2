using Organiza.Application.Common;
using Organiza.Application.Services;
using Organiza.Domain.Files;
using Organiza.Tests.Support;

namespace Organiza.Tests;

public sealed class PathGuardTests
{
    [Fact]
    public void WarnsAbove220Characters()
    {
        var guard = new PathGuard();
        var prefix = Path.GetFullPath(Path.GetTempPath());
        var path = prefix + new string('a', PathGuard.WarningLength + 1 - prefix.Length);

        var result = guard.Assess(path);

        Assert.Equal(PathRisk.Warning, result.Risk);
        Assert.True(result.CanProceed);
    }

    [Fact]
    public void BlocksAbove240Characters()
    {
        var guard = new PathGuard();
        var prefix = Path.GetFullPath(Path.GetTempPath());
        var path = prefix + new string('b', PathGuard.BlockingLength + 1 - prefix.Length);

        Assert.Equal(PathRisk.Blocked, guard.Assess(path).Risk);
        Assert.Throws<UnsafePathException>(() => guard.EnsureAllowed(path));
    }

    [Fact]
    public void AllowsExactly240Characters()
    {
        var guard = new PathGuard();
        var prefix = Path.GetFullPath(Path.GetTempPath());
        var path = prefix + new string('c', PathGuard.BlockingLength - prefix.Length);

        Assert.NotEqual(PathRisk.Blocked, guard.Assess(path).Risk);
        guard.EnsureAllowed(path);
    }

    [Fact]
    public void Shortener_PreservesExtensionAndProcessIdentifierUnder240Characters()
    {
        var directory = Path.Combine(Path.GetTempPath(), "Organiza", "pasta-profunda");
        var fileName = $"Petição com descrição extremamente longa {new string('x', 220)} proc 0000002-00.2026.5.00.0002.pdf";

        var fitted = PathNameShortener.FitFileName(directory, fileName);

        Assert.True(Path.Combine(Path.GetFullPath(directory), fitted).Length <= 240);
        Assert.EndsWith(".pdf", fitted, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("0000002-00.2026.5.00.0002", fitted, StringComparison.Ordinal);
    }

    [Fact]
    public void Shortener_UsesOfficialAbbreviationsOnlyWhenPathNeedsReduction()
    {
        using var directory = new TemporaryDirectory();
        var shortName = "Comprovante Pagamento IPTU.pdf";

        Assert.Equal(shortName, PathNameShortener.FitFileName(directory.FullPath, shortName));

        var longDirectory = Path.Combine(directory.FullPath, new string('x', 95));
        Directory.CreateDirectory(longDirectory);
        var shortened = PathNameShortener.FitFileName(longDirectory,
            "2026-08-28 - Comprovante Pagamento Notificação Lançamento IPTU - 02171270172.pdf");

        Assert.StartsWith("2026-08-28 - ", shortened);
        Assert.Contains("Comp", shortened);
        Assert.Contains("Pagto", shortened);
        Assert.Contains("Notif", shortened);
        Assert.DoesNotContain("Comprovante", shortened);
        Assert.True(Path.Combine(longDirectory, shortened).Length <= 240);
    }
}
