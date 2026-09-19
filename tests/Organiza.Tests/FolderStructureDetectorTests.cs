using Organiza.Application.Services;
using Organiza.Domain.Organization;

namespace Organiza.Tests;

public sealed class FolderStructureDetectorTests
{
    [Fact]
    public void SameNumericPrefix_DoesNotMixLegalAndPropertyTemplates()
    {
        Assert.False(StandardFolderManager.IsEquivalentFolder("01 INICIAL", StandardFolders.Research));
        Assert.False(StandardFolderManager.IsEquivalentFolder("02 CITACAO", StandardFolders.AuctionAndJudicial));
        Assert.False(StandardFolderManager.IsEquivalentFolder("08 RECURSOS", StandardFolders.Contracts));
    }

    [Fact]
    public void DetectsLegalProcessTemplate()
    {
        var result = FolderStructureDetector.Detect("C:\\processo",
            ["00 AUTOS", "01 INICIAL", "02 CITACAO", "03 DEFESA", "07 SENTENCA"]);

        Assert.Equal(FolderStructureProfile.LegalProcess, result);
    }

    [Fact]
    public void DetectsDrivePortfolioTemplate()
    {
        var result = FolderStructureDetector.Detect("G:\\Meu Drive\\Nova Raiz",
            ["00 TRIAGEM", "01 FINANCEIRO", "02 LITERATURA", "03 JURÍDICO", "14 SISTEMAS"]);

        Assert.Equal(FolderStructureProfile.DrivePortfolio, result);
    }

    [Fact]
    public void DetectsPropertyTemplateUsingOfficialFolderNames()
    {
        var result = FolderStructureDetector.Detect("C:\\imovel",
            ["01 PESQUISA", "02 ARREMATAÇÃO", "03 CARTORIO E REGISTRO", "07A PROCESSOS ADMINISTRATIVOS", "09 FOTOS"]);

        Assert.Equal(FolderStructureProfile.PropertyDossier, result);
        Assert.Contains(StandardFolders.AdministrativeProceedings, StandardFolders.Numbered);
        Assert.Equal(10, StandardFolders.Numbered.Count);
    }

    [Theory]
    [InlineData("G:\\Meu Drive\\Nova Raiz\\00 TRIAGEM", FolderStructureProfile.Triage)]
    [InlineData("G:\\Meu Drive\\Nova Raiz\\01 FINANCEIRO", FolderStructureProfile.Financial)]
    [InlineData("G:\\Meu Drive\\Nova Raiz\\02 LITERATURA", FolderStructureProfile.Literature)]
    [InlineData("G:\\Meu Drive\\Nova Raiz\\07 ESCRITÓRIO", FolderStructureProfile.Office)]
    [InlineData("G:\\Meu Drive\\Nova Raiz\\12 CURSOS", FolderStructureProfile.Courses)]
    [InlineData("G:\\Meu Drive\\Nova Raiz\\13 REFERÊNCIAS", FolderStructureProfile.References)]
    [InlineData("G:\\Meu Drive\\Nova Raiz\\14 SISTEMAS", FolderStructureProfile.Systems)]
    public void RecognizesIncompleteCategoriesWithoutInventingSubfolders(string path, FolderStructureProfile expected)
    {
        var result = FolderStructureDetector.Detect(path, []);

        Assert.Equal(expected, result);
        Assert.True(FolderStructureDetector.ProtectsExistingStructure(result));
    }

    [Fact]
    public void CategoryRootDoesNotReceivePropertyModules()
    {
        var result = FolderStructureDetector.Detect("G:\\Meu Drive\\Nova Raiz\\04 REGULARIZANDO", ["P01R", "P02R"]);

        Assert.Equal(FolderStructureProfile.PropertyPortfolioRoot, result);
        Assert.True(FolderStructureDetector.ProtectsExistingStructure(result));
    }

    [Fact]
    public void EmptyDossierInsidePropertyCategoryCanUsePropertyModel()
    {
        var result = FolderStructureDetector.Detect(
            "G:\\Meu Drive\\Nova Raiz\\04 REGULARIZANDO\\P01R Avenida Exemplo 100", []);

        Assert.Equal(FolderStructureProfile.PropertyDossier, result);
        Assert.False(FolderStructureDetector.ProtectsExistingStructure(result));
    }

    [Fact]
    public void DetectsRealPersonalPersonTemplate()
    {
        var result = FolderStructureDetector.Detect("G:\\pessoa",
            ["01 Identificacao", "02 Certidoes e registros", "03 Procuracoes", "04 Comprovantes de endereco"]);

        Assert.Equal(FolderStructureProfile.PersonalDocumentsPerson, result);
        Assert.True(FolderStructureDetector.ProtectsExistingStructure(result));
    }

    [Fact]
    public void DetectsRealPersonalCompanyTemplate()
    {
        var result = FolderStructureDetector.Detect("G:\\empresa",
            ["01 Contrato Social", "02 Certidões", "03 CNPJ e Inscrições", "05 Documentos Contábeis"]);

        Assert.Equal(FolderStructureProfile.PersonalDocumentsCompany, result);
    }

    [Fact]
    public void DetectsRealPetAndDeadArchiveTemplates()
    {
        Assert.Equal(FolderStructureProfile.PetDocuments,
            FolderStructureDetector.Detect("G:\\pet", ["01 Vacinação", "02 Documentos"]));
        Assert.Equal(FolderStructureProfile.DeadArchive,
            FolderStructureDetector.Detect("G:\\Meu Drive\\Nova Raiz\\11 MORTO",
                ["01 VENDIDO", "02 CANCELADO", "03 INDICE GERAL"]));
    }

    [Fact]
    public void ProtectsVehicleAndAuctionScaffoldsWithUndefinedInternals()
    {
        var vehicle = FolderStructureDetector.Detect(
            "G:\\Meu Drive\\Nova Raiz\\09 VEICULOS\\_MODELO_PASTA_DONO\\_MODELO_PASTA_VEICULO", []);
        var auction = FolderStructureDetector.Detect(
            "G:\\Meu Drive\\Nova Raiz\\10 LEILAO\\_MODELO_PASTA_PESQUISADOR\\_MODELO_LOTE_LEILAO", []);

        Assert.Equal(FolderStructureProfile.VehicleScaffold, vehicle);
        Assert.Equal(FolderStructureProfile.AuctionScaffold, auction);
        Assert.True(FolderStructureDetector.ProtectsExistingStructure(vehicle));
        Assert.True(FolderStructureDetector.ProtectsExistingStructure(auction));
    }
}
