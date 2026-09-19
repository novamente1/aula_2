using Organiza.Application.Abstractions;
using Organiza.Application.Common;
using Organiza.Domain.Operations;
using Organiza.Domain.Organization;

namespace Organiza.Application.Services;

public enum OfficialStructureTemplate
{
    NovaRaiz,
    Property,
    LegalProcess,
    PersonalPerson,
    PersonalCompany,
    Pet,
    DeadArchive
}

public sealed record StructureTemplateOption(
    OfficialStructureTemplate Template,
    string DisplayName,
    string Explanation);

public sealed record StructureTemplateApplyResult(
    OfficialStructureTemplate Template,
    IReadOnlyList<string> Created,
    IReadOnlyList<string> Reused);

public sealed class OfficialStructureTemplateService(IFileSystem fileSystem)
{
    public IReadOnlyList<StructureTemplateOption> GetCompatibleOptions(string rootPath)
    {
        var direct = fileSystem.EnumerateDirectories(rootPath)
            .Select(Path.GetFileName).OfType<string>().ToArray();
        var profile = FolderStructureDetector.Detect(rootPath, direct);
        var normalizedSegments = Path.GetFullPath(rootPath)
            .Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries)
            .Select(StandardFolderManager.Normalize).ToArray();
        var options = new List<StructureTemplateOption>();
        if (profile == FolderStructureProfile.DrivePortfolio)
            options.Add(new(OfficialStructureTemplate.NovaRaiz, "15 categorias da Nova Raiz",
                "Cria somente categorias ausentes de 00 TRIAGEM a 14 SISTEMAS; não cria subpastas internas."));
        if (profile == FolderStructureProfile.PropertyDossier)
            options.Add(new(OfficialStructureTemplate.Property, "Modelo de imóvel — 10 módulos",
                "Inclui 07 ACOES e 07A PROCESSOS ADMINISTRATIVOS como pastas irmãs."));
        if (profile == FolderStructureProfile.LegalProcess || normalizedSegments.Contains("03 JURIDICO") ||
            normalizedSegments.Contains("07 ACOES"))
            options.Add(new(OfficialStructureTemplate.LegalProcess, "Modelo de processo — 13 módulos",
                "Cria 00 AUTOS a 12 DUPLICADOS no processo selecionado."));
        if (normalizedSegments.Contains("08 DOC PESSOAL"))
        {
            options.Add(new(OfficialStructureTemplate.PersonalPerson, "Modelo de pessoa física — 12 módulos",
                "Use somente em uma pasta individual identificada por nome/CPF."));
            options.Add(new(OfficialStructureTemplate.PersonalCompany, "Modelo de empresa — 6 módulos",
                "Use somente em uma pasta individual identificada por nome/CNPJ."));
            options.Add(new(OfficialStructureTemplate.Pet, "Modelo de animal — 2 módulos",
                "Use somente na pasta individual do animal."));
        }
        if (profile == FolderStructureProfile.DeadArchive)
            options.Add(new(OfficialStructureTemplate.DeadArchive, "Modelo 11 MORTO — 3 status",
                "Cria somente VENDIDO, CANCELADO e INDICE GERAL; nunca aplica modelo de imóvel."));
        return options;
    }

    public StructureTemplateApplyResult Apply(
        string rootPath,
        OfficialStructureTemplate template,
        ExplicitApproval approval)
    {
        if (!approval.Granted) throw new ApprovalRequiredException("criar estrutura oficial selecionada");
        var compatible = GetCompatibleOptions(rootPath).Select(option => option.Template).Contains(template);
        if (!compatible)
            throw new InvalidOperationException(
                "O modelo escolhido não é compatível com a posição desta pasta na estrutura oficial.");

        var expected = FoldersFor(template);
        var existing = fileSystem.EnumerateDirectories(rootPath).ToArray();
        var created = new List<string>();
        var reused = new List<string>();
        foreach (var name in expected)
        {
            var match = existing.FirstOrDefault(path =>
                StandardFolderManager.Normalize(Path.GetFileName(path)) == StandardFolderManager.Normalize(name));
            if (match is not null)
            {
                reused.Add(match);
                continue;
            }
            var path = Path.Combine(rootPath, name);
            new PathGuard().EnsureAllowed(path);
            fileSystem.CreateDirectory(path);
            created.Add(path);
        }
        return new(template, created, reused);
    }

    public static IReadOnlyList<string> FoldersFor(OfficialStructureTemplate template) => template switch
    {
        OfficialStructureTemplate.NovaRaiz => DriveStructureCatalog.RootCategories,
        OfficialStructureTemplate.Property => StandardFolders.PropertyTemplate,
        OfficialStructureTemplate.LegalProcess => DriveStructureCatalog.LegalProcessFolders,
        OfficialStructureTemplate.PersonalPerson => DriveStructureCatalog.PersonalPersonFolders,
        OfficialStructureTemplate.PersonalCompany => DriveStructureCatalog.PersonalCompanyFolders,
        OfficialStructureTemplate.Pet => DriveStructureCatalog.PetFolders,
        OfficialStructureTemplate.DeadArchive => DriveStructureCatalog.DeadArchiveFolders,
        _ => []
    };
}
