using Organiza.Domain.Organization;

namespace Organiza.Application.Services;

public enum FolderStructureProfile
{
    PropertyDossier,
    PropertyModule,
    LegalProcess,
    DrivePortfolio,
    PropertyPortfolioRoot,
    PersonalDocumentsPerson,
    PersonalDocumentsCompany,
    PetDocuments,
    DeadArchive,
    VehicleScaffold,
    AuctionScaffold,
    Triage,
    Financial,
    Literature,
    Office,
    PersonalDocumentsRoot,
    VehiclesRoot,
    AuctionRoot,
    Courses,
    References,
    Systems,
    GeneralOrUnknown
}

public static class DriveStructureCatalog
{
    public static readonly IReadOnlyList<RootCategoryDefinition> RootCategoryDefinitions =
    [
        new("00 TRIAGEM", OfficialStructureState.Empty, "Pouso temporário; não impor modelo fixo."),
        new("01 FINANCEIRO", OfficialStructureState.Empty, "Categoria vazia; não inventar subpastas."),
        new("02 LITERATURA", OfficialStructureState.Incomplete, "Pastas livres por assunto; não impor numeração."),
        new("03 JURIDICO", OfficialStructureState.Confirmed, "Modelo processual confirmado."),
        new("04 REGULARIZANDO", OfficialStructureState.Confirmed, "Dossiês de imóvel confirmados."),
        new("05 VENDA", OfficialStructureState.ReadyModel, "Modelo de imóvel pronto e caso real."),
        new("06 ATIVO", OfficialStructureState.ReadyModel, "Modelo de imóvel pronto e caso real."),
        new("07 ESCRITORIO", OfficialStructureState.Incomplete, "Modelo de colaborador ainda não definido."),
        new("08 DOC PESSOAL", OfficialStructureState.Confirmed, "Modelos de pessoa, empresa e animal confirmados."),
        new("09 VEICULOS", OfficialStructureState.Skeleton, "Esqueleto existente; subpastas internas não definidas."),
        new("10 LEILAO", OfficialStructureState.Skeleton, "Esqueleto existente; subpastas internas não definidas."),
        new("11 MORTO", OfficialStructureState.Confirmed, "Modelo próprio por status de encerramento."),
        new("12 CURSOS", OfficialStructureState.Empty, "Categoria vazia; não inventar subpastas."),
        new("13 REFERENCIAS", OfficialStructureState.Incomplete, "Preservar lacunas 01 a 07."),
        new("14 SISTEMAS", OfficialStructureState.Incomplete, "Preservar a lacuna 02.")
    ];

    public static readonly IReadOnlyList<string> RootCategories =
        RootCategoryDefinitions.Select(item => item.Name).ToArray();

    public static readonly IReadOnlyList<string> LegalProcessFolders =
    [
        "00 AUTOS", "01 INICIAL", "02 CITACAO", "03 DEFESA", "04 REPLICA", "05 PROVAS",
        "06 DECISOES", "07 SENTENCA", "08 RECURSOS", "09 EXECUCAO", "10 PUBLICACOES",
        "11 RELATORIOS", "12 DUPLICADOS"
    ];

    public static readonly IReadOnlyList<string> PersonalPersonFolders =
    [
        "01 IDENTIFICACAO", "02 CERTIDOES E REGISTROS", "03 PROCURACOES",
        "04 COMPROVANTES DE ENDERECO", "05 SAUDE", "06 FINANCEIRO E APOSENTADORIA",
        "07 FOTOS E MEMORIAS", "08 DOCUMENTOS PADRAO PARA ENVIO",
        "09 DECLARACOES E AUTORIZACOES", "10 BOLETIM DE OCORRENCIA",
        "11 DOCUMENTOS COMPLEMENTARES", "12 DOCUMENTOS DUPLICADOS"
    ];

    public static readonly IReadOnlyList<string> PersonalCompanyFolders =
    [
        "01 CONTRATO SOCIAL", "02 CERTIDOES", "03 CNPJ E INSCRICOES", "04 PROCURACOES",
        "05 DOCUMENTOS CONTABEIS", "06 PROCESSOS"
    ];

    public static readonly IReadOnlyList<string> PetFolders = ["01 VACINACAO", "02 DOCUMENTOS"];
    public static readonly IReadOnlyList<string> DeadArchiveFolders =
        ["01 VENDIDO", "02 CANCELADO", "03 INDICE GERAL"];

    public static readonly IReadOnlySet<string> ProtectedFolderNames = RootCategories
        .Concat(StandardFolders.All)
        .Concat(LegalProcessFolders)
        .Concat(PersonalPersonFolders)
        .Concat(PersonalCompanyFolders)
        .Concat(PetFolders)
        .Concat(DeadArchiveFolders)
        .Concat([StandardFolders.ContextReview, StandardFolders.QualityReview, "08 AMBIGUOS", "ANIMAIS DE ESTIMACAO", "_MODELO_PASTA_IMOVEL",
            "_MODELO_PASTA_DONO", "_MODELO_PASTA_VEICULO", "_MODELO_PASTA_PESQUISADOR",
            "_MODELO_LOTE_LEILAO", "_CLIENTES_TERCEIROS_ESCRITORIO"])
        .Select(StandardFolderManager.Normalize)
        .ToHashSet(StringComparer.Ordinal);

    public static bool IsProtectedFolderName(string name) =>
        ProtectedFolderNames.Contains(StandardFolderManager.Normalize(name));

    public static bool MatchesAtLeast(IReadOnlyCollection<string> normalized, IEnumerable<string> expected, int minimum) =>
        expected.Count(name => normalized.Contains(StandardFolderManager.Normalize(name))) >= minimum;
}

public static class FolderStructureDetector
{
    public static FolderStructureProfile Detect(string rootPath, IReadOnlyList<string> directFolderNames)
    {
        var normalized = directFolderNames.Select(StandardFolderManager.Normalize)
            .ToHashSet(StringComparer.Ordinal);

        if (DriveStructureCatalog.MatchesAtLeast(normalized, DriveStructureCatalog.LegalProcessFolders, 4))
            return FolderStructureProfile.LegalProcess;
        if (DriveStructureCatalog.MatchesAtLeast(normalized, DriveStructureCatalog.RootCategories, 4))
            return FolderStructureProfile.DrivePortfolio;
        if (DriveStructureCatalog.MatchesAtLeast(normalized, StandardFolders.Numbered, 3))
            return FolderStructureProfile.PropertyDossier;
        if (DriveStructureCatalog.MatchesAtLeast(normalized, DriveStructureCatalog.PersonalPersonFolders, 4))
            return FolderStructureProfile.PersonalDocumentsPerson;
        if (DriveStructureCatalog.MatchesAtLeast(normalized, DriveStructureCatalog.PersonalCompanyFolders, 3))
            return FolderStructureProfile.PersonalDocumentsCompany;
        if (DriveStructureCatalog.MatchesAtLeast(normalized, DriveStructureCatalog.PetFolders, 2))
            return FolderStructureProfile.PetDocuments;
        if (DriveStructureCatalog.MatchesAtLeast(normalized, DriveStructureCatalog.DeadArchiveFolders, 2))
            return FolderStructureProfile.DeadArchive;

        var segments = Path.GetFullPath(rootPath)
            .Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries)
            .Select(StandardFolderManager.Normalize)
            .ToArray();
        var leaf = segments.LastOrDefault() ?? string.Empty;

        if (leaf == "NOVA RAIZ") return FolderStructureProfile.DrivePortfolio;
        if (leaf == "_MODELO_PASTA_IMOVEL") return FolderStructureProfile.PropertyDossier;
        if (leaf is "_MODELO_PASTA_DONO" or "_MODELO_PASTA_VEICULO")
            return FolderStructureProfile.VehicleScaffold;
        if (leaf is "_MODELO_PASTA_PESQUISADOR" or "_MODELO_LOTE_LEILAO")
            return FolderStructureProfile.AuctionScaffold;
        if (StandardFolders.Numbered.Any(name => StandardFolderManager.Normalize(name) == leaf))
            return FolderStructureProfile.PropertyModule;

        var rootProfile = RootCategoryProfile(leaf);
        if (rootProfile != FolderStructureProfile.GeneralOrUnknown) return rootProfile;

        if (segments.Any(segment => segment is "04 REGULARIZANDO" or "05 VENDA" or "06 ATIVO"))
            return FolderStructureProfile.PropertyDossier;
        if (segments.Contains("03 JURIDICO") || segments.Contains("07 ACOES"))
            return FolderStructureProfile.LegalProcess;
        if (segments.Contains("08 DOC PESSOAL")) return FolderStructureProfile.PersonalDocumentsRoot;
        if (segments.Contains("09 VEICULOS")) return FolderStructureProfile.VehiclesRoot;
        if (segments.Contains("10 LEILAO")) return FolderStructureProfile.AuctionRoot;
        return FolderStructureProfile.GeneralOrUnknown;
    }

    public static bool ProtectsExistingStructure(FolderStructureProfile profile) =>
        profile is not FolderStructureProfile.PropertyDossier and not FolderStructureProfile.GeneralOrUnknown;

    public static string Describe(FolderStructureProfile profile) => profile switch
    {
        FolderStructureProfile.PropertyDossier => "dossiê de imóvel confirmado (10 módulos, de 01 PESQUISA a 09 FOTOS, incluindo 07A)",
        FolderStructureProfile.PropertyModule => "módulo interno de um imóvel; não criar outro modelo dentro dele",
        FolderStructureProfile.LegalProcess => "processo jurídico (00 AUTOS a 12 DUPLICADOS)",
        FolderStructureProfile.DrivePortfolio => "Nova Raiz (00 TRIAGEM a 14 SISTEMAS)",
        FolderStructureProfile.PropertyPortfolioRoot => "categoria de imóveis; o modelo só pode existir dentro de cada imóvel",
        FolderStructureProfile.PersonalDocumentsPerson => "documentos de pessoa física (12 módulos)",
        FolderStructureProfile.PersonalDocumentsCompany => "documentos de empresa (6 módulos)",
        FolderStructureProfile.PetDocuments => "documentos de animal (Vacinação e Documentos)",
        FolderStructureProfile.DeadArchive => "arquivo encerrado por status (Vendido, Cancelado e Índice Geral)",
        FolderStructureProfile.VehicleScaffold => "modelo proprietário/veículo ainda sem subpastas internas validadas",
        FolderStructureProfile.AuctionScaffold => "modelo pesquisador/lote ainda sem subpastas internas validadas",
        FolderStructureProfile.Triage => "triagem temporária; sem modelo fixo",
        FolderStructureProfile.Financial => "Financeiro ainda sem modelo real confirmado",
        FolderStructureProfile.Literature => "Literatura por assunto; sem modelo fixo",
        FolderStructureProfile.Office => "Escritório; modelo de colaborador ainda pendente",
        FolderStructureProfile.PersonalDocumentsRoot => "Documentos pessoais; preservar os modelos existentes",
        FolderStructureProfile.VehiclesRoot => "Veículos; esqueleto existente ainda sem estrutura interna validada",
        FolderStructureProfile.AuctionRoot => "Leilão; esqueleto existente ainda sem estrutura interna validada",
        FolderStructureProfile.Courses => "Cursos ainda sem modelo real confirmado",
        FolderStructureProfile.References => "Referências incompleta; preservar as lacunas atuais",
        FolderStructureProfile.Systems => "Sistemas incompleta; preservar as lacunas atuais",
        _ => "pasta geral ou sem modelo reconhecido"
    };

    private static FolderStructureProfile RootCategoryProfile(string normalizedLeaf) => normalizedLeaf switch
    {
        "00 TRIAGEM" => FolderStructureProfile.Triage,
        "01 FINANCEIRO" => FolderStructureProfile.Financial,
        "02 LITERATURA" => FolderStructureProfile.Literature,
        "03 JURIDICO" => FolderStructureProfile.LegalProcess,
        "04 REGULARIZANDO" or "05 VENDA" or "06 ATIVO" => FolderStructureProfile.PropertyPortfolioRoot,
        "07 ESCRITORIO" => FolderStructureProfile.Office,
        "08 DOC PESSOAL" => FolderStructureProfile.PersonalDocumentsRoot,
        "09 VEICULOS" => FolderStructureProfile.VehiclesRoot,
        "10 LEILAO" => FolderStructureProfile.AuctionRoot,
        "11 MORTO" => FolderStructureProfile.DeadArchive,
        "12 CURSOS" => FolderStructureProfile.Courses,
        "13 REFERENCIAS" => FolderStructureProfile.References,
        "14 SISTEMAS" => FolderStructureProfile.Systems,
        _ => FolderStructureProfile.GeneralOrUnknown
    };
}
