namespace Organiza.Domain.Operations;

public sealed record ExplicitApproval(bool Granted, DateTimeOffset GrantedAt, string Scope)
{
    public static ExplicitApproval Denied(string scope) => new(false, DateTimeOffset.UtcNow, scope);
    public static ExplicitApproval Grant(string scope) => new(true, DateTimeOffset.UtcNow, scope);
}

public enum OperationStatus
{
    Planned,
    Completed,
    Skipped,
    Failed,
    Blocked
}

public sealed record OperationLogEntry(
    DateTimeOffset Timestamp,
    string Operation,
    string Source,
    string? Destination,
    OperationStatus Status,
    string? Details = null);
