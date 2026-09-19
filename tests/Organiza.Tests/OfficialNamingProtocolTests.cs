using System.Text;
using Organiza.Application.Services;
using Organiza.Domain.Files;
using Organiza.Domain.Organization;
using Organiza.Infrastructure.Files;
using Organiza.Tests.Support;

namespace Organiza.Tests;

public sealed class OfficialNamingProtocolTests
{
    [Fact]
    public void Protocol_UsesSequenceDescriptionAndProcess_WithoutBarcode()
    {
        const string barcode = "00190500954014481606906809350314337370000000100";
        var analysis = new DocumentAnalysis(
            new FileItem("C:\\teste\\entrada.pdf", 100, ".pdf"), "HASH",
            $"PODER JUDICIÁRIO SENTENÇA Processo 0000002-00.2026.5.00.0002 Código de barras {barcode}",
            TextExtractionMethod.PdfText, ["0000002-00.2026.5.00.0002"], []);
        var baseSuggestion = new RenameSuggestion(analysis.File.FullPath, "nome.pdf", true,
            DestinationFolder: StandardFolders.Litigation, DocumentType: "Sentença");

        var result = Assert.Single(OfficialFileNamingProtocol.Apply([baseSuggestion], [analysis]));

        Assert.Equal("01 - Sentença - 0000002-00.2026.5.00.0002.pdf", result.SuggestedName);
        Assert.Equal("0000002-00.2026.5.00.0002", result.PrincipalId);
        Assert.DoesNotContain(barcode, result.SuggestedName);
    }

    [Fact]
    public void Protocol_OmitsUnknownId_InsteadOfInventingPlaceholder()
    {
        var analysis = new DocumentAnalysis(
            new FileItem("C:\\teste\\entrada.pdf", 100, ".pdf"), "HASH", "Relatório simples",
            TextExtractionMethod.PdfText, [], []);
        var baseSuggestion = new RenameSuggestion(analysis.File.FullPath, "nome.pdf", true,
            DestinationFolder: StandardFolders.Research, DocumentType: "Relatório");

        var result = Assert.Single(OfficialFileNamingProtocol.Apply([baseSuggestion], [analysis]));

        Assert.Equal("01 - Relatório.pdf", result.SuggestedName);
        Assert.DoesNotContain("INDEFINIDO", result.SuggestedName, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Protocol_VehicleDocumentPrioritizesRenavamAndKeepsPlateAsSecondaryId()
    {
        var analysis = new DocumentAnalysis(
            new FileItem("C:\\teste\\caminhao.pdf", 100, ".pdf"), "HASH",
            "CERTIFICADO DE REGISTRO E LICENCIAMENTO DE VEÍCULO RENAVAM 11111111111 PLACA DEF4G56",
            TextExtractionMethod.PdfText, [], []);
        var baseSuggestion = new RenameSuggestion(analysis.File.FullPath, "nome.pdf", true,
            DestinationFolder: StandardFolders.Research, DocumentType: "Documento do veículo");

        var result = Assert.Single(OfficialFileNamingProtocol.Apply([baseSuggestion], [analysis]));

        Assert.Equal("01 - Doc Renavam - 11111111111 - DEF4G56.pdf", result.SuggestedName);
        Assert.Equal("11111111111", result.PrincipalId);
        Assert.Equal("DEF4G56", result.SecondaryId);
    }

    [Fact]
    public void Protocol_DebtStatementThatMentionsRenavam_IsNotVehicleCertificate()
    {
        var analysis = new DocumentAnalysis(
            new FileItem("C:\\teste\\debitos.pdf", 100, ".pdf"), "HASH",
            "DEMONSTRATIVO DE DÉBITOS DO VEÍCULO RENAVAM 11111111111 PLACA DEF4G56 Data 28/01/2025",
            TextExtractionMethod.PdfText, [], ["28/01/2025"]);
        var baseSuggestion = new RenameSuggestion(analysis.File.FullPath, "nome.pdf", true,
            DestinationFolder: StandardFolders.Financial, DocumentType: "Demonstrativo de débitos");

        var result = Assert.Single(OfficialFileNamingProtocol.Apply([baseSuggestion], [analysis]));

        Assert.Contains("Demonstrativo de débitos", result.SuggestedName);
        Assert.DoesNotContain("Doc Renavam", result.SuggestedName);
        Assert.Contains("11111111111", result.SuggestedName);
        Assert.Contains("DEF4G56", result.SuggestedName);
    }

    [Fact]
    public void ContextIsolation_SeparatesDocumentWhosePlateDiffersFromDominantBatch()
    {
        var analyses = new[]
        {
            Analysis("a.pdf", "Documento placa DEF4G56"),
            Analysis("b.pdf", "Documento placa DEF4G56"),
            Analysis("c.pdf", "Portal de serviços placa HIJ7K89")
        };
        var suggestions = analyses.Select((item, index) => new RenameSuggestion(
            item.File.FullPath, $"0{index + 1} - Documento.pdf", true,
            DestinationFolder: StandardFolders.Research, ClassificationRule: "TESTE",
            DocumentType: "Documento")).ToArray();

        var result = BatchContextIsolationService.Apply(suggestions, analyses);

        Assert.Equal(StandardFolders.Research, result[0].DestinationFolder);
        Assert.Equal(StandardFolders.Research, result[1].DestinationFolder);
        Assert.Equal(StandardFolders.ContextReview, result[2].DestinationFolder);
        Assert.Equal("REVISAO-CONTEXTO-PLACA", result[2].ClassificationRule);
    }

    private static DocumentAnalysis Analysis(string name, string text) => new(
        new FileItem(Path.Combine("C:\\teste", name), 100, ".pdf"), name, text,
        TextExtractionMethod.PdfText, [], []);

    [Fact]
    public void Protocol_CompactsLongJudicialDescription()
    {
        var analysis = new DocumentAnalysis(
            new FileItem("C:\\teste\\notificacao.pdf", 100, ".pdf"), "HASH",
            "NOTIFICAÇÃO DE ADIAMENTO DO LEILÃO E EXPEDIÇÃO DE MANDADO Processo 0000002-00.2026.5.00.0002",
            TextExtractionMethod.PdfText, ["0000002-00.2026.5.00.0002"], []);
        var baseSuggestion = new RenameSuggestion(analysis.File.FullPath, "nome.pdf", true,
            DestinationFolder: StandardFolders.Litigation,
            DocumentType: "Notificação difere adiamento do leilão expedição mandado imissão de posse reintegração");

        var result = Assert.Single(OfficialFileNamingProtocol.Apply([baseSuggestion], [analysis]));

        Assert.StartsWith("01 - Notificação de leilão - 0000002-00.2026.5.00.0002", result.SuggestedName);
        Assert.True(Path.GetFileNameWithoutExtension(result.SuggestedName).Length < 80);
    }

    [Fact]
    public void QualityGate_BlocksNumberAndDateWithoutDocumentDescription()
    {
        var original = "C:\\teste\\0001-74 - 2013-06-24.docx";
        var suggestion = new RenameSuggestion(original, "01 - 0001-74 - 2013-06-24.docx", true,
            DestinationFolder: StandardFolders.Research, DocumentType: "0001-74 - 2013-06-24");

        var result = Assert.Single(OfficialFileNameQualityGate.Apply([suggestion]));

        Assert.Equal(Path.GetFileName(original), result.SuggestedName);
        Assert.False(result.IsSelected);
        Assert.Equal(StandardFolders.QualityReview, result.DestinationFolder);
        Assert.Equal("REVISAO-NOME-INCOMPLETO", result.ClassificationRule);
    }

    [Fact]
    public void MappingIndex_UsesTriageLogsAtPortfolioRoot_AndUtf8Bom()
    {
        using var directory = new TemporaryDirectory();
        var triage = Directory.CreateDirectory(directory.PathFor("00 TRIAGEM")).FullName;
        var finalFolder = Directory.CreateDirectory(directory.PathFor("03 JURIDICO")).FullName;
        var finalPath = Path.Combine(finalFolder, "01 - Sentença - 1001307.pdf");
        var fs = new PhysicalFileSystem();

        var path = new MappingIndexService(fs).Append(directory.FullPath,
            [new("1001307", "Sentença", finalPath)]);

        Assert.Equal(Path.Combine(triage, "06 LOGs", MappingIndexService.FileName), path);
        var bytes = File.ReadAllBytes(path);
        Assert.True(bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble));
        var content = File.ReadAllText(path, Encoding.UTF8);
        Assert.Contains("1001307 | Sentença | 01 - Sentença - 1001307.pdf | 03 JURIDICO", content);
    }
}
