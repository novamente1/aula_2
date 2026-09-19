using Organiza.Application.Services;

namespace Organiza.Tests;

public sealed class DocumentPreviewPolicyTests
{
    [Theory]
    [InlineData("documento.pdf")]
    [InlineData("foto.JPEG")]
    [InlineData("imagem.png")]
    [InlineData("anotacao.txt")]
    public void BrowserFriendlyDocuments_UseQuickBrowserPreview(string name)
    {
        Assert.True(DocumentPreviewPolicy.ShouldUseBrowser(name));
    }

    [Theory]
    [InlineData("contrato.docx")]
    [InlineData("planilha.xlsx")]
    public void OfficeDocuments_KeepTheirInstalledApplication(string name)
    {
        Assert.False(DocumentPreviewPolicy.ShouldUseBrowser(name));
    }

    [Fact]
    public void LocalFileUri_EscapesSpacesAndAccents()
    {
        var uri = DocumentPreviewPolicy.ToLocalFileUri("C:\\Pasta de teste\\Certidão.pdf");

        Assert.StartsWith("file:///C:/", uri, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Pasta%20de%20teste", uri);
        Assert.Contains("Certid", uri);
    }
}
