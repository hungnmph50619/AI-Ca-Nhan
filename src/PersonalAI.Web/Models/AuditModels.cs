namespace PersonalAI.Web.Models;

public static class AuditAgents
{
    public const string User = "user";
    public const string PersonalAi = "personal-ai";
    public const string TaskEngine = "task-engine";
    public const string System = "system";
    public const string Companion = "companion";
}

public static class AuditResults
{
    public const string Succeeded = "succeeded";
    public const string Failed = "failed";
    public const string Denied = "denied";
    public const string Cancelled = "cancelled";
    public const string Prepared = "prepared";
    public const string Proposed = "proposed";
}

public sealed record AuditEvent(
    string WorkspaceId,
    string Agent,
    string Action,
    string Target,
    string Reason,
    string Result,
    string? Tool = null);

public sealed record AuditItem(
    Guid AuditId,
    DateTimeOffset Time,
    string WorkspaceId,
    string Agent,
    string Action,
    string? Tool,
    string Target,
    string Reason,
    string Result);

public sealed record AuditResponse(
    string Storage,
    int RetentionDays,
    int MaximumEntries,
    IReadOnlyList<AuditItem> Items);

public sealed record AuditSummary(
    string WorkspaceId,
    int TotalEvents,
    IReadOnlyDictionary<string, int> ByAgent,
    IReadOnlyDictionary<string, int> ByResult,
    IReadOnlyDictionary<string, int> ByAction);
