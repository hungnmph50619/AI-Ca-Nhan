namespace PersonalAI.Web.Models;

public static class AuditAgents
{
    public const string User = "user";
    public const string PersonalAi = "personal-ai";
    public const string TaskEngine = "task-engine";
    public const string System = "system";
    public const string Companion = "companion";
    public const string Automation = "automation";
}

public static class AuditResults
{
    public const string Succeeded = "succeeded";
    public const string Failed = "failed";
    public const string Denied = "denied";
    public const string Cancelled = "cancelled";
    public const string Prepared = "prepared";
    public const string Proposed = "proposed";
    public const string Blocked = "blocked";
    public const string Interrupted = "interrupted";
}

public static class SystemLogLevels
{
    public const string Info = "info";
    public const string Warning = "warning";
    public const string Error = "error";
    public const string Security = "security";

    public static readonly IReadOnlySet<string> All =
        new HashSet<string>(
            [Info, Warning, Error, Security],
            StringComparer.Ordinal);
}

public sealed record AuditEvent(
    string WorkspaceId,
    string Agent,
    string Action,
    string Target,
    string Reason,
    string Result,
    string? Tool = null,
    string? Level = null,
    string? Source = null,
    string? CorrelationId = null);

public sealed record AuditItem(
    Guid AuditId,
    DateTimeOffset Time,
    string WorkspaceId,
    string Agent,
    string Action,
    string? Tool,
    string Target,
    string Reason,
    string Result,
    string? Level = null,
    string? Source = null,
    string? CorrelationId = null);

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
    IReadOnlyDictionary<string, int> ByAction,
    IReadOnlyDictionary<string, int>? ByLevel = null,
    IReadOnlyDictionary<string, int>? BySource = null);

public sealed record SystemLogStatus(
    string Version,
    string WorkspaceId,
    string Storage,
    int RetentionDays,
    int MaximumEntries,
    int MaximumQueryLimit,
    bool RequestCorrelationEnabled,
    bool RequestBodyLoggingEnabled,
    bool AuthorizationHeaderLoggingEnabled,
    bool QueryStringLoggingEnabled,
    bool BackwardCompatibleAuditApi,
    IReadOnlyList<string> Levels);
