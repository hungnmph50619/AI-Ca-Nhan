namespace PersonalAI.Web.Models;

public static class LoopGuardScopes
{
    public const string Agent = "agent";
    public const string Tool = "tool";
    public const string Ci = "ci";
    public const string Delegation = "delegation";
    public const string Retry = "retry";
    public const string Task = "task";
    public const string Branch = "branch";
    public const string Automation = "automation";
    public const string Development = "development";

    public static readonly IReadOnlySet<string> All =
        new HashSet<string>(
            [Agent, Tool, Ci, Delegation, Retry, Task, Branch, Automation, Development],
            StringComparer.Ordinal);
}

public sealed record LoopGuardCheckRequest(
    string Scope,
    string RunId,
    string Fingerprint,
    int CostUnits = 1);

public sealed record LoopGuardDecision(
    string WorkspaceId,
    string Scope,
    string RunId,
    string Fingerprint,
    bool Allowed,
    bool Blocked,
    string Reason,
    string? StopReason,
    int FingerprintOccurrences,
    int RunTransitions,
    int RunCostUnits,
    DateTimeOffset WindowStartedAt,
    DateTimeOffset CheckedAt);

public sealed record LoopGuardStatus(
    string Version,
    string WorkspaceId,
    int MaximumRepeatedFingerprint,
    int MaximumTransitionsPerRun,
    int MaximumCostUnitsPerRun,
    int WindowMinutes,
    int MaximumTrackedRuns,
    int MaximumTrackedFingerprints,
    bool DuplicateFingerprintDetectionEnabled,
    bool RunawayCostDetectionEnabled,
    bool BranchExplosionGuardEnabled,
    bool DuplicateTaskGuardEnabled,
    bool StopReasonAudited,
    bool FailClosed,
    IReadOnlyList<string> SupportedScopes,
    int TrackedRuns,
    int TrackedFingerprints,
    long BlockedChecks);

public sealed class LoopGuardValidationException(string message)
    : Exception(message);
