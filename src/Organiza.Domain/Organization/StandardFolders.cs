namespace Organiza.Domain.Organization;

public static class StandardFolders
{
    public const string Research = "01 PESQUISA";
    public const string AuctionAndJudicial = "02 ARREMATAÇÃO";
    public const string Registry = "03 CARTORIO E REGISTRO";
    public const string Improvements = "04 REFORMA E PUBLICIDADE";
    public const string Financial = "05 IPTU";
    public const string Leasing = "06 LOCACAO";
    public const string Litigation = "07 ACOES";
    public const string AdministrativeProceedings = "07A PROCESSOS ADMINISTRATIVOS";
    public const string Contracts = "08 CONTRATOS";
    public const string Reports = Research;
    public const string Duplicates = "98 DUPLICADOS";
    public const string PropertyDuplicates = "_DUPLICADAS_CONFIRMADAS_PENDENTE_EXCLUSAO_USUARIO";
    public const string MigrationLogs = "_LOGS_MIGRACAO";
    public const string ContextReview = "_NAO_PERTENCE_A_ESTA_PASTA";
    public const string QualityReview = "_REVISAR_ORIGEM_OUTRAS_PASTAS_PENDENTE_DECISAO_USUARIO";
    public const string Originals = "99 ORIGINAIS";
    public const string Photos = "09 FOTOS";

    public static readonly IReadOnlyList<string> Numbered =
    [
        Research,
        AuctionAndJudicial,
        Registry,
        Improvements,
        Financial,
        Leasing,
        Litigation,
        AdministrativeProceedings,
        Contracts,
        Photos
    ];

    public static readonly IReadOnlyList<string> PropertyTemplate =
    [
        PropertyDuplicates,
        MigrationLogs,
        ContextReview,
        QualityReview,
        .. Numbered
    ];

    public static readonly IReadOnlyList<string> All =
    [
        .. PropertyTemplate,
        Duplicates,
        Originals
    ];

    public static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> Aliases =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            [Research] = [Research, "01 CONTRATOS E PESQUISA"],
            [AuctionAndJudicial] = [AuctionAndJudicial, "02 ARREMATAÇÃO E JUDICIAL"],
            [Registry] = [Registry, "03 CARTÓRIO E REGISTRO", "03 CARTORIO"],
            [Improvements] = [Improvements, "04 REFORMA E BENFEITORIAS"],
            [Financial] = [Financial, "05 IPTU CONDOMÍNIO E FINANCEIRO"],
            [Leasing] = [Leasing, "06 LOCAÇÃO"],
            [Litigation] = [Litigation, "07 PROCESSOS E PEÇAS JUDICIAIS"],
            [AdministrativeProceedings] = [AdministrativeProceedings, "07A PROCESSOS ADMINISTRATIVOS"],
            [Contracts] = [Contracts],
            [Photos] = [Photos, "FOTOS"],
            [PropertyDuplicates] = [PropertyDuplicates],
            [Duplicates] = [Duplicates, "12 DUPLICADOS"],
            [ContextReview] = [ContextReview, "96 NÃO PERTENCE À PASTA"],
            [QualityReview] = [QualityReview, "97 REVISAR QUALIDADE"],
            [Originals] = [Originals]
        };
}
