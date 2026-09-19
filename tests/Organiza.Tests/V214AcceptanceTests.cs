using System.Text;
using System.Text.Json;
using Organiza.Application.Services;
using Organiza.Domain.Operations;
using Organiza.Domain.Files;
using Organiza.Domain.Organization;
using Organiza.Infrastructure.Files;
using Organiza.Infrastructure.History;
using Organiza.Tests.Support;

namespace Organiza.Tests;

public sealed class V214AcceptanceTests
{
    [Fact]
    public void OfficialCatalog_ContainsExactlyFifteenCategoriesAndFiveStates()
    {
        var definitions = DriveStructureCatalog.RootCategoryDefinitions;

        Assert.Equal(15, definitions.Count);
        Assert.Equal("00 TRIAGEM", definitions.First().Name);
        Assert.Equal("14 SISTEMAS", definitions.Last().Name);
        Assert.Equal(5, definitions.Select(item => item.State).Distinct().Count());
        Assert.Contains(definitions, item => item.Name == "13 REFERENCIAS" && item.State == OfficialStructureState.Incomplete);
        Assert.Contains(definitions, item => item.Name == "14 SISTEMAS" && item.Rule.Contains("lacuna 02"));
    }

    [Fact]
    public void Diagnostic_IsReadOnlyAndShowsFoundMissingAndDivergence()
    {
        using var temp = new TemporaryDirectory();
        foreach (var name in new[] { "00 TRIAGEM", "01 FINANCEIRO", "03 JURIDICO", "14 SISTEMAS", "PASTA FORA DO MODELO" })
            Directory.CreateDirectory(Path.Combine(temp.FullPath, name));
        var before = Directory.GetDirectories(temp.FullPath).Order().ToArray();

        var result = new StructureDiagnosticService(new PhysicalFileSystem()).Diagnose(temp.FullPath);

        Assert.Equal(FolderStructureProfile.DrivePortfolio, result.Profile);
        Assert.Contains(result.Items, item => item.ExpectedName == "00 TRIAGEM" && item.Status == StructureDiagnosticStatus.Found);
        Assert.Contains(result.Items, item => item.ExpectedName == "02 LITERATURA" && item.Status == StructureDiagnosticStatus.Missing);
        Assert.Contains(result.Items, item => item.FoundName == "PASTA FORA DO MODELO" && item.Status == StructureDiagnosticStatus.Divergence);
        Assert.Equal(before, Directory.GetDirectories(temp.FullPath).Order().ToArray());
    }

    [Fact]
    public void Diagnostic_RecognizesAllReusableModels()
    {
        AssertModel(DriveStructureCatalog.LegalProcessFolders, FolderStructureProfile.LegalProcess);
        AssertModel(StandardFolders.Numbered, FolderStructureProfile.PropertyDossier);
        AssertModel(DriveStructureCatalog.PersonalPersonFolders, FolderStructureProfile.PersonalDocumentsPerson);
        AssertModel(DriveStructureCatalog.PersonalCompanyFolders, FolderStructureProfile.PersonalDocumentsCompany);
        AssertModel(DriveStructureCatalog.PetFolders, FolderStructureProfile.PetDocuments);
        AssertModel(DriveStructureCatalog.DeadArchiveFolders, FolderStructureProfile.DeadArchive);
    }

    [Fact]
    public void UndefinedScaffoldsAndIncompleteRoots_AreNotCompleted()
    {
        using var temp = new TemporaryDirectory();
        var vehicles = Path.Combine(temp.FullPath, "09 VEICULOS");
        var model = Path.Combine(vehicles, "_MODELO_PASTA_DONO", "_MODELO_PASTA_VEICULO");
        Directory.CreateDirectory(model);

        var result = new StructureDiagnosticService(new PhysicalFileSystem()).Diagnose(model);

        Assert.Equal(FolderStructureProfile.VehicleScaffold, result.Profile);
        Assert.Equal(0, result.MissingCount);
        Assert.Single(result.Items);
        Assert.Equal("Nenhuma subpasta exigida", result.Items[0].ExpectedName);
    }

    [Fact]
    public void ExplicitNovaRaizTemplate_CreatesExactlyFifteenCategoriesAndNothingInsideThem()
    {
        using var temp = new TemporaryDirectory();
        var root = Path.Combine(temp.FullPath, "Nova Raiz");
        Directory.CreateDirectory(root);
        var service = new OfficialStructureTemplateService(new PhysicalFileSystem());

        var result = service.Apply(root, OfficialStructureTemplate.NovaRaiz,
            ExplicitApproval.Grant("teste controlado"));

        Assert.Equal(15, result.Created.Count);
        Assert.Equal(DriveStructureCatalog.RootCategories.Order(),
            Directory.GetDirectories(root).Select(Path.GetFileName).Order());
        Assert.All(Directory.GetDirectories(root), path => Assert.Empty(Directory.GetFileSystemEntries(path)));
    }

    [Fact]
    public void PropertyTemplate_Keeps07And07AAsSiblings()
    {
        using var temp = new TemporaryDirectory();
        var root = Path.Combine(temp.FullPath, "04 REGULARIZANDO", "P01R Imovel teste");
        Directory.CreateDirectory(root);
        var service = new OfficialStructureTemplateService(new PhysicalFileSystem());

        service.Apply(root, OfficialStructureTemplate.Property, ExplicitApproval.Grant("teste controlado"));

        Assert.True(Directory.Exists(Path.Combine(root, "07 ACOES")));
        Assert.True(Directory.Exists(Path.Combine(root, "07A PROCESSOS ADMINISTRATIVOS")));
        Assert.False(Directory.Exists(Path.Combine(root, "07 ACOES", "07A PROCESSOS ADMINISTRATIVOS")));

        var second = service.Apply(root, OfficialStructureTemplate.Property,
            ExplicitApproval.Grant("segunda aplicação controlada"));
        Assert.Empty(second.Created);
        Assert.Equal(StandardFolders.PropertyTemplate.Count, second.Reused.Count);
        Assert.Contains(StandardFolders.PropertyDuplicates, second.Reused.Select(Path.GetFileName));
        Assert.Contains(StandardFolders.MigrationLogs, second.Reused.Select(Path.GetFileName));
        Assert.Contains(StandardFolders.ContextReview, second.Reused.Select(Path.GetFileName));
        Assert.Contains(StandardFolders.QualityReview, second.Reused.Select(Path.GetFileName));
        Assert.DoesNotContain(Directory.GetDirectories(root), path => Path.GetFileName(path).Contains("(1)"));
    }

    [Theory]
    [InlineData(OfficialStructureTemplate.PersonalPerson, 12)]
    [InlineData(OfficialStructureTemplate.PersonalCompany, 6)]
    [InlineData(OfficialStructureTemplate.Pet, 2)]
    public void PersonalTemplates_AreAvailableOnlyInsidePersonalDocuments(
        OfficialStructureTemplate template, int expectedCount)
    {
        using var temp = new TemporaryDirectory();
        var root = Path.Combine(temp.FullPath, "08 DOC PESSOAL", "Cadastro teste");
        Directory.CreateDirectory(root);
        var service = new OfficialStructureTemplateService(new PhysicalFileSystem());

        var result = service.Apply(root, template, ExplicitApproval.Grant("teste controlado"));

        Assert.Equal(expectedCount, result.Created.Count);
    }

    [Fact]
    public void VehicleScaffold_OffersNoInventedTemplate()
    {
        using var temp = new TemporaryDirectory();
        var root = Path.Combine(temp.FullPath, "09 VEICULOS", "_MODELO_PASTA_DONO", "_MODELO_PASTA_VEICULO");
        Directory.CreateDirectory(root);

        var options = new OfficialStructureTemplateService(new PhysicalFileSystem()).GetCompatibleOptions(root);

        Assert.Empty(options);
    }

    [Fact]
    public void IncompleteReferencesAndSystems_OfferNoAutomaticCompletion()
    {
        using var temp = new TemporaryDirectory();
        var references = Path.Combine(temp.FullPath, "13 REFERENCIAS");
        var systems = Path.Combine(temp.FullPath, "14 SISTEMAS");
        Directory.CreateDirectory(Path.Combine(references, "00 Modelo LEIA-ME"));
        Directory.CreateDirectory(Path.Combine(references, "08 Maquete"));
        Directory.CreateDirectory(Path.Combine(references, "09 Manuais"));
        Directory.CreateDirectory(Path.Combine(systems, "01 Projetos GEM"));
        Directory.CreateDirectory(Path.Combine(systems, "03 Escopos"));
        var service = new OfficialStructureTemplateService(new PhysicalFileSystem());

        Assert.Empty(service.GetCompatibleOptions(references));
        Assert.Empty(service.GetCompatibleOptions(systems));
        Assert.False(Directory.Exists(Path.Combine(references, "01")));
        Assert.False(Directory.Exists(Path.Combine(systems, "02")));
    }

    [Fact]
    public void AllContextualDuplicateFolders_AreExcludedFromInteractiveFlows()
    {
        using var temp = new TemporaryDirectory();
        foreach (var folder in new[] { "98 DUPLICADOS", "12 DUPLICADOS", "12 Documentos duplicados" })
        {
            var file = Path.Combine(temp.FullPath, folder, "copia.pdf");
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, "duplicado");
            Assert.True(WorkspaceFilePolicy.IsInsideDuplicatesFolder(temp.FullPath, file));
            Assert.False(WorkspaceFilePolicy.IsInteractiveCandidate(temp.FullPath, file));
        }
    }

    [Theory]
    [InlineData("legal", "12 DUPLICADOS", "DUP-LEGAL-12")]
    [InlineData("person", "12 Documentos duplicados", "DUP-PESSOA-12")]
    [InlineData("general", "98 DUPLICADOS", "DUP-GERAL-98")]
    public void DuplicateDestination_IsResolvedByContext(string context, string expectedFolder, string expectedRule)
    {
        using var temp = new TemporaryDirectory();
        var names = context switch
        {
            "legal" => DriveStructureCatalog.LegalProcessFolders.Take(4),
            "person" => DriveStructureCatalog.PersonalPersonFolders.Take(4),
            _ => Array.Empty<string>()
        };
        foreach (var name in names) Directory.CreateDirectory(Path.Combine(temp.FullPath, name));

        var decision = new DuplicateDestinationResolver(new PhysicalFileSystem()).Resolve(temp.FullPath);

        Assert.Equal(expectedFolder, decision.FolderName);
        Assert.Equal(expectedRule, decision.RuleCode);
    }

    [Fact]
    public async Task Catalog_PersistsStableIdentityRuleAndUtf8Bom()
    {
        using var temp = new TemporaryDirectory();
        var file = Path.Combine(temp.FullPath, "documento.pdf");
        await File.WriteAllTextAsync(file, "conteúdo");
        var sha = new string('A', 64);
        var analysis = new DocumentAnalysis(
            new FileItem(file, new FileInfo(file).Length, ".pdf"), sha,
            "CARTEIRA DE IDENTIDADE REGISTRO GERAL", TextExtractionMethod.PdfText,
            [], ["18/09/2026"]);
        var suggestion = new RenameSuggestion(file, "2026-09-18 - RG.pdf", true,
            "DOC-PESSOAL-CONTEUDO: identidade detectada no conteúdo.", "01 IDENTIFICACAO",
            "DOC-PESSOAL-CONTEUDO", "RG");
        var store = new JsonDocumentCatalogStore(temp.FullPath);
        var service = new DocumentCatalogService(new PhysicalFileSystem(), store);

        var snapshot = await service.SaveAsync(temp.FullPath, [analysis], [suggestion]);

        var bytes = await File.ReadAllBytesAsync(store.CatalogPath);
        Assert.True(bytes.AsSpan().StartsWith(Encoding.UTF8.GetPreamble()));
        Assert.Equal("DOC-AAAAAAAAAAAAAAAAAAAA", snapshot.Documents[0].DocumentId);
        Assert.Equal("DOC-PESSOAL-CONTEUDO", snapshot.Documents[0].ClassificationRule);
        var json = await File.ReadAllTextAsync(store.CatalogPath);
        using var document = JsonDocument.Parse(json);
        Assert.Equal("RG", document.RootElement.GetProperty("Documents")[0].GetProperty("DocumentType").GetString());
    }

    private static void AssertModel(IReadOnlyList<string> folders, FolderStructureProfile expectedProfile)
    {
        using var temp = new TemporaryDirectory();
        foreach (var name in folders) Directory.CreateDirectory(Path.Combine(temp.FullPath, name));

        var result = new StructureDiagnosticService(new PhysicalFileSystem()).Diagnose(temp.FullPath);

        Assert.Equal(expectedProfile, result.Profile);
        Assert.Equal(folders.Count, result.FoundCount);
        Assert.Equal(0, result.MissingCount);
    }
}
