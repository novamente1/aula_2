namespace Organiza.Domain.Organization;

public sealed record DocumentCatalogEntry(
    string DocumentId,
    string RelativePath,
    string OriginalName,
    string SuggestedName,
    string ContentSha256,
    long SizeBytes,
    string DocumentType,
    string DestinationFolder,
    string ClassificationRule,
    string ClassificationExplanation,
    string StructureProfile,
    TextExtractionMethod ExtractionMethod,
    IReadOnlyList<string> ProcessIdentifiers,
    IReadOnlyList<string> DocumentDates,
    bool ExtractionComplete,
    IReadOnlyList<string> Warnings,
    DateTimeOffset AnalyzedAt);

public sealed record DocumentCatalogSnapshot(
    string SchemaVersion,
    string RootPath,
    DateTimeOffset GeneratedAt,
    IReadOnlyList<DocumentCatalogEntry> Documents);
