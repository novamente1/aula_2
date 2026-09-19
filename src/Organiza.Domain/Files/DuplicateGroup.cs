namespace Organiza.Domain.Files;

public sealed record DuplicateCandidate(FileItem File, bool IsSuggestedPrincipal);

public sealed record DuplicateGroup(long SizeBytes, string Sha256, IReadOnlyList<DuplicateCandidate> Files)
{
    public DuplicateCandidate Principal => Files.Single(file => file.IsSuggestedPrincipal);
}
