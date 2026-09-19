using Organiza.Application.Abstractions;
using Organiza.Domain.Organization;

namespace Organiza.Application.Services;

public enum OfficialStructureState
{
    Confirmed,
    ReadyModel,
    Skeleton,
    Empty,
    Incomplete
}

public enum StructureDiagnosticStatus
{
    Found,
    Recognized,
    Missing,
    Divergence
}

public sealed record RootCategoryDefinition(
    string Name,
    OfficialStructureState State,
    string Rule);

public sealed record StructureDiagnosticItem(
    string ExpectedName,
    OfficialStructureState State,
    StructureDiagnosticStatus Status,
    string FoundName,
    string Details,
    string PossibleAction);

public sealed record StructureDiagnosticResult(
    FolderStructureProfile Profile,
    string ProfileDescription,
    IReadOnlyList<StructureDiagnosticItem> Items)
{
    public int FoundCount => Items.Count(item =>
        item.Status is StructureDiagnosticStatus.Found or StructureDiagnosticStatus.Recognized);
    public int MissingCount => Items.Count(item => item.Status == StructureDiagnosticStatus.Missing);
    public int DivergenceCount => Items.Count(item => item.Status == StructureDiagnosticStatus.Divergence);
    public string Summary =>
        $"Perfil reconhecido: {ProfileDescription}. Encontrado/reconhecido: {FoundCount}; " +
        $"ausente: {MissingCount}; divergências: {DivergenceCount}. Diagnóstico somente leitura.";
}

public sealed class StructureDiagnosticService(IFileSystem fileSystem)
{
    public StructureDiagnosticResult Diagnose(string rootPath)
    {
        var root = Path.GetFullPath(rootPath);
        var directNames = fileSystem.EnumerateDirectories(root)
            .Select(Path.GetFileName)
            .OfType<string>()
            .ToArray();
        var profile = FolderStructureDetector.Detect(root, directNames);
        var expected = ExpectedFor(profile);
        var items = expected.Count == 0
            ? DiagnoseProtectedOpenStructure(profile, directNames)
            : DiagnoseExpected(expected, directNames);
        return new(profile, FolderStructureDetector.Describe(profile), items);
    }

    private static IReadOnlyList<StructureDiagnosticItem> DiagnoseExpected(
        IReadOnlyList<ExpectedFolder> expected,
        IReadOnlyList<string> actual)
    {
        var items = new List<StructureDiagnosticItem>();
        var matchedActual = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var definition in expected)
        {
            var match = actual.FirstOrDefault(candidate => definition.IsMatch(candidate));
            if (match is null)
            {
                items.Add(new(definition.Name, definition.State, StructureDiagnosticStatus.Missing, "—",
                    definition.Details, definition.MissingAction));
                continue;
            }

            matchedActual.Add(match);
            var exact = StandardFolderManager.Normalize(match) == StandardFolderManager.Normalize(definition.Name);
            items.Add(new(definition.Name, definition.State,
                exact ? StructureDiagnosticStatus.Found : StructureDiagnosticStatus.Recognized,
                match, definition.Details,
                exact ? "Preservar." : "Reutilizar a pasta equivalente; não criar cópia ou sufixo."));
        }

        foreach (var unexpected in actual.Where(name => !matchedActual.Contains(name)))
        {
            items.Add(new("Fora do modelo confirmado", OfficialStructureState.Incomplete,
                StructureDiagnosticStatus.Divergence, unexpected,
                "A pasta existe, mas não pertence ao modelo confirmado para este perfil.",
                "Revisar manualmente. O Organiza não renomeará nem removerá esta pasta no diagnóstico."));
        }
        return items;
    }

    private static IReadOnlyList<StructureDiagnosticItem> DiagnoseProtectedOpenStructure(
        FolderStructureProfile profile,
        IReadOnlyList<string> actual)
    {
        var state = ProfileState(profile);
        var rule = FolderStructureDetector.Describe(profile);
        var items = actual.Select(name => new StructureDiagnosticItem(
            "Estrutura livre/protegida", state, StructureDiagnosticStatus.Recognized, name, rule,
            "Preservar. Não completar, numerar ou inventar subpastas.")).ToList();
        if (items.Count == 0)
        {
            items.Add(new("Nenhuma subpasta exigida", state, StructureDiagnosticStatus.Recognized, "—", rule,
                "Nenhuma ação estrutural automática é permitida para este perfil."));
        }
        return items;
    }

    private static IReadOnlyList<ExpectedFolder> ExpectedFor(FolderStructureProfile profile) => profile switch
    {
        FolderStructureProfile.DrivePortfolio => DriveStructureCatalog.RootCategoryDefinitions
            .Select(item => new ExpectedFolder(item.Name, item.State, item.Rule,
                "Criar somente após aprovação explícita da estrutura da Nova Raiz.",
                candidate => StandardFolderManager.Normalize(candidate) == StandardFolderManager.Normalize(item.Name)))
            .ToArray(),
        FolderStructureProfile.PropertyDossier => StandardFolders.Numbered
            .Select(name => ExactExpected(name, "Módulo oficial do modelo de imóvel.")).ToArray(),
        FolderStructureProfile.LegalProcess => DriveStructureCatalog.LegalProcessFolders
            .Select(name => ExactExpected(name, "Módulo oficial do processo jurídico.")).ToArray(),
        FolderStructureProfile.PersonalDocumentsPerson => DriveStructureCatalog.PersonalPersonFolders
            .Select(name => ExactExpected(name, "Módulo oficial de pessoa física.")).ToArray(),
        FolderStructureProfile.PersonalDocumentsCompany => DriveStructureCatalog.PersonalCompanyFolders
            .Select(name => ExactExpected(name, "Módulo oficial de empresa.")).ToArray(),
        FolderStructureProfile.PetDocuments => DriveStructureCatalog.PetFolders
            .Select(name => ExactExpected(name, "Módulo oficial de animal de estimação.")).ToArray(),
        FolderStructureProfile.DeadArchive => DriveStructureCatalog.DeadArchiveFolders
            .Select(name => ExactExpected(name, "Modelo próprio de encerramento; nunca aplicar modelo de imóvel.")).ToArray(),
        _ => []
    };

    private static ExpectedFolder ExactExpected(string name, string details) => new(
        name, OfficialStructureState.Confirmed, details,
        "A criação só pode ocorrer em uma etapa autorizada e compatível com este perfil.",
        candidate => StandardFolderManager.Normalize(candidate) == StandardFolderManager.Normalize(name));

    private static OfficialStructureState ProfileState(FolderStructureProfile profile) => profile switch
    {
        FolderStructureProfile.VehicleScaffold or FolderStructureProfile.AuctionScaffold or
            FolderStructureProfile.VehiclesRoot or FolderStructureProfile.AuctionRoot => OfficialStructureState.Skeleton,
        FolderStructureProfile.Financial or FolderStructureProfile.Courses or FolderStructureProfile.Triage =>
            OfficialStructureState.Empty,
        FolderStructureProfile.PropertyPortfolioRoot => OfficialStructureState.ReadyModel,
        _ => OfficialStructureState.Incomplete
    };

    private sealed record ExpectedFolder(
        string Name,
        OfficialStructureState State,
        string Details,
        string MissingAction,
        Func<string, bool> IsMatch);
}
