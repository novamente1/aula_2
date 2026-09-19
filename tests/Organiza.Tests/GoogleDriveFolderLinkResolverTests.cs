using Organiza.Infrastructure.Files;
using Organiza.Tests.Support;

namespace Organiza.Tests;

public sealed class GoogleDriveFolderLinkResolverTests
{
    [Fact]
    public void Resolver_RecognizesRemembersAndResolvesDriveFolderUrl()
    {
        using var directory = new TemporaryDirectory();
        var mapping = directory.PathFor("mapping.json");
        var local = Directory.CreateDirectory(directory.PathFor("Dossiê Exemplo")).FullName;
        var resolver = new GoogleDriveFolderLinkResolver(mapping);
        const string url = "https://drive.google.com/drive/folders/1wdyMwBNKgNPs9NT7Asx2b6qQNjctVDof?usp=drive_link";

        Assert.True(resolver.IsGoogleDriveFolderUrl(url));
        Assert.Null(resolver.Resolve(url));
        resolver.Remember(url, local);

        Assert.Equal(local, new GoogleDriveFolderLinkResolver(mapping).Resolve(url));
    }

    [Fact]
    public void Resolver_DoesNotTreatArbitraryWebAddressAsDriveFolder()
    {
        using var directory = new TemporaryDirectory();
        var resolver = new GoogleDriveFolderLinkResolver(directory.PathFor("mapping.json"));

        Assert.False(resolver.IsGoogleDriveFolderUrl("https://example.com/drive/folders/abc"));
    }
}
