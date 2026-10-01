namespace PersonalAI.Web.Models;

public static class NightlyImprovementStopReasons
{
    public const string CompletedBudget = "completed-budget";
    public const string NoEligibleBacklog = "no-eligible-backlog";
    public const string RuntimeBudgetReached = "runtime-budget-reached";
    public const string ActiveNightlyRunExists = "active-nightly-run-exists";
    public const string Disabled = "disabled";
}

public sealed record RunNightlyImprovementRequest(
    string RepositoryPath,
    string BaseBranch,
    int MaximumItems = 1,
    int MaximumRuntimeMinutes = 60,
    string BranchPrefix = "experiment/nightly",
    bool ConfirmRun = false);

public sealed record NightlyImprovementItemResult(
    Guid BacklogItemId,
    string Title,
    string Priority,
    Guid? DevelopmentRunId,
    string? Branch,
    string Status,
    string Detail);

public sealed record NightlyImprovementRunReport(
    Guid Id,
    string WorkspaceId,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    int MaximumItems,
    int MaximumRuntimeMinutes,
    int EligibleItems,
    int SelectedItems,
    int DevelopmentRunsCreated,
    string StopReason,
    IReadOnlyList<NightlyImprovementItemResult> Items);

public sealed record NightlyImprovementStatus(
    string Version,
    string WorkspaceId,
    bool SchedulerEnabled,
    int ScheduleHourUtc,
    int ScheduleMinuteUtc,
    int MaximumItemsPerNight,
    int MaximumRuntimeMinutes,
    bool LowRiskOnly,
    bool OneActiveNightlyRunAtATime,
    bool ReportsPersisted,
    bool DownstreamPullRequestUsesPolicy,
    bool DownstreamAutoMergeUsesMergePolicy,
    DateTimeOffset? LastRunAt,
    string? LastStopReason);

public sealed class NightlyImprovementValidationException(string message)
    : Exception(message);
