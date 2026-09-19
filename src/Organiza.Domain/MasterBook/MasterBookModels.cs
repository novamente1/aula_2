using Organiza.Domain.Organization;

namespace Organiza.Domain.MasterBook;

public static class MasterBookFileNames
{
    public const string Markdown = "LIVRO MESTRE 360.md";
    public const string UpdatedMarkdown = "LIVRO MESTRE 360 - ATUALIZADO.md";
    public const string Json = ".organiza_livro_mestre_360.json";

    public static bool IsMarkdown(string name) =>
        string.Equals(name, Markdown, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(name, UpdatedMarkdown, StringComparison.OrdinalIgnoreCase);
}

public sealed record MasterSourceDocument(
    string RelativePath,
    long SizeBytes,
    string Sha256,
    TextExtractionMethod ExtractionMethod,
    IReadOnlyList<string> ProcessIdentifiers,
    IReadOnlyList<string> DocumentDates,
    string ExtractedText,
    int PageCount,
    int PagesWithText,
    int OcrPages,
    bool ExtractionComplete,
    IReadOnlyList<string> ExtractionWarnings);

public sealed record MasterEvidence(
    string RelativePath,
    string Excerpt);

public sealed record MasterFinancialEntry(
    string Category,
    string Description,
    decimal? Amount,
    string? Date,
    string RelativePath,
    string Evidence);

public sealed record MasterBookFinding(
    string Description,
    IReadOnlyList<string> EvidenceFiles,
    string Confidence,
    IReadOnlyList<MasterEvidence>? Evidence = null);

public sealed record MasterBookSection(
    int Number,
    string Title,
    string Status,
    IReadOnlyList<MasterBookFinding> Findings,
    IReadOnlyList<string> PendingHumanReview);

public sealed record MasterExecutiveSynthesis(
    string DossierOverview,
    string PropertyAndAuctionOverview,
    string ProceduralOverview,
    string PriorCounselOverview,
    string FinancialOverview,
    IReadOnlyList<string> ImmediatePriorities);

public sealed record MasterFinancialConsolidation(
    string Category,
    int Entries,
    int Documents,
    decimal RecognizedTotal,
    string ReviewStatus);

public sealed record MasterLegalThesis(
    string Thesis,
    string SupportingBasis,
    string CounterpointOrGap,
    string Confidence,
    IReadOnlyList<string> EvidenceFiles);

public sealed record MasterProceduralRisk(
    string Risk,
    string Probability,
    string Impact,
    string Urgency,
    string Mitigation,
    IReadOnlyList<string> EvidenceFiles);

public sealed record MasterStrategicAction(
    int Priority,
    string Action,
    string Rationale,
    string ExpectedResult,
    IReadOnlyList<string> EvidenceFiles);

public sealed record MasterRegistryRestriction(
    string Type,
    string Status,
    string? RegistryReference,
    string? Date,
    string Description,
    string RelativePath,
    string Evidence);

public sealed record MasterPropertyRegistryAnalysis(
    bool RegistryDocumentFound,
    string Summary,
    string? RegistrationNumber,
    string? RegistryOffice,
    string? LatestRegistryDocument,
    IReadOnlyList<MasterRegistryRestriction> Restrictions,
    IReadOnlyList<string> PendingReview);

public sealed record MasterRegistryAuctionEvent(
    string EventType,
    string? Date,
    string? RegistryReference,
    string Status,
    string ChronologicalRelation,
    IReadOnlyList<string> SourceDocuments,
    string Evidence);

public sealed record MasterPaymentReconciliation(
    string ReconciliationKey,
    int? InstallmentNumber,
    decimal? ExpectedAmount,
    decimal? PaidAmount,
    string? DueDate,
    string? PaymentDate,
    string Status,
    IReadOnlyList<string> SourceDocuments,
    string Evidence);

public sealed record MasterBailiffDiligence(
    string? Date,
    string Act,
    string Outcome,
    string? Address,
    string? Recipient,
    string Summary,
    string RelativePath,
    string Evidence);

public sealed record MasterBookReport(
    string SchemaVersion,
    DateTimeOffset GeneratedAt,
    string SelectedRootName,
    string SelectedRootPath,
    bool RootNameProtected,
    int DocumentsAnalyzed,
    IReadOnlyList<string> IdentifiedProcesses,
    IReadOnlyList<MasterSourceDocument> SourceDocuments,
    IReadOnlyList<MasterFinancialEntry> FinancialInventory,
    MasterExecutiveSynthesis ExecutiveSynthesis,
    IReadOnlyList<MasterFinancialConsolidation> FinancialConsolidation,
    IReadOnlyList<MasterLegalThesis> LegalThesisMatrix,
    IReadOnlyList<MasterProceduralRisk> ProceduralRiskMatrix,
    IReadOnlyList<MasterStrategicAction> StrategicActionPlan,
    MasterPropertyRegistryAnalysis PropertyRegistryAnalysis,
    IReadOnlyList<MasterRegistryAuctionEvent> RegistryAuctionChronology,
    IReadOnlyList<MasterPaymentReconciliation> PaymentReconciliation,
    IReadOnlyList<MasterBailiffDiligence> BailiffDiligences,
    IReadOnlyList<MasterBookSection> Sections,
    string ReviewNotice);

public sealed record MasterBookResult(
    string MarkdownPath,
    string JsonPath,
    int DocumentsAnalyzed,
    int ProcessesIdentified);

public sealed record MasterBookProgress(int Current, int Total, string Message);
