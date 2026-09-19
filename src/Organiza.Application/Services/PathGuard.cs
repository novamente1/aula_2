using Organiza.Application.Common;
using Organiza.Domain.Files;

namespace Organiza.Application.Services;

public sealed class PathGuard
{
    public const int WarningLength = 220;
    public const int BlockingLength = 240;

    public PathAssessment Assess(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var risk = fullPath.Length > BlockingLength
            ? PathRisk.Blocked
            : fullPath.Length > WarningLength
                ? PathRisk.Warning
                : PathRisk.Safe;

        return new PathAssessment(fullPath, fullPath.Length, risk);
    }

    public void EnsureAllowed(string path)
    {
        var result = Assess(path);
        if (!result.CanProceed)
        {
            throw new UnsafePathException(result.FullPath, result.Length);
        }
    }
}
