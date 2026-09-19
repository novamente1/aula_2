namespace Organiza.Domain.Files;

public enum PathRisk
{
    Safe,
    Warning,
    Blocked
}

public sealed record PathAssessment(string FullPath, int Length, PathRisk Risk)
{
    public bool CanProceed => Risk is not PathRisk.Blocked;
}
