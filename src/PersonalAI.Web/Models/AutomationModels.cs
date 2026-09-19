namespace PersonalAI.Web.Models;

public static class AutomationScheduleKinds
{
    public const string Once = "once";
    public const string Interval = "interval";

    public static readonly IReadOnlySet<string> All =
        new HashSet<string>(
            [Once, Interval],
            StringComparer.OrdinalIgnoreCase);
}

public static class AutomationStates
{
    public const string Scheduled = "scheduled";
    public const string Running = "running";
    public const string Paused = "paused";
    public const string AwaitingConfirmation = "awaiting-confirmation";
    public const string Completed = "completed";
    public const string Failed = "failed";
    public const string Interrupted = "interrupted";
}

public static class AutomationRunStatuses
{
    public const string None = "none";
    public const string Succeeded = "succeeded";
    public const string Blocked = "blocked";
    public const string AwaitingConfirmation = "awaiting-confirmation";
    public const string Failed = "failed";
    public const string Interrupted = "interrupted";
}

public sealed record AutomationStatusResponse(
    string Version,
    bool Supported,
    bool BackgroundSchedulerEnabled,
    bool AutoConfirmationEnabled,
    bool DecisionRecommendationAutoExecutionEnabled,
    int MaximumAutomationsPerWorkspace,
    int MinimumIntervalMinutes,
    int MaximumIntervalMinutes,
    int SchedulerPollSeconds,
    int MaximumDueBatchSize,
    IReadOnlyList<string> Limitations);

public sealed record PersonalAutomation(
    Guid Id,
    string WorkspaceId,
    string Name,
    Guid TaskId,
    string ScheduleKind,
    DateTimeOffset StartAt,
    int? IntervalMinutes,
    DateTimeOffset? NextRunAt,
    bool Enabled,
    string State,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? LastRunAt,
    string LastRunStatus,
    string? LastMessage,
    int RunCount);

public sealed record AutomationListResponse(
    string WorkspaceId,
    int MaximumAutomations,
    IReadOnlyList<PersonalAutomation> Automations);

public sealed record CreateAutomationRequest(
    string Name,
    Guid TaskId,
    string ScheduleKind,
    DateTimeOffset? StartAt = null,
    int? IntervalMinutes = null,
    bool Confirmed = false);

public sealed record SetAutomationEnabledRequest(
    bool Enabled,
    bool Confirmed = false);

public sealed record AutomationConfirmedRequest(
    bool Confirmed = false);

public sealed record AutomationRunResult(
    PersonalAutomation Automation,
    PersonalTask? Task,
    PersonalTaskStepExecutionResponse? Execution,
    bool ExecutedStep,
    bool RequiresConfirmation,
    string Message);
