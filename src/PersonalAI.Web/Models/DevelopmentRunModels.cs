namespace PersonalAI.Web.Models;

public static class DevelopmentRunStages
{
    public const string Analysis = "analysis";
    public const string Coding = "coding";
    public const string Testing = "testing";
    public const string Review = "review";
    public const string Security = "security";
    public const string Benchmark = "benchmark";
    public const string Push = "push";
    public const string Ci = "ci";
    public const string MergePolicy = "merge-policy";
    public const string LocalSync = "local-sync";
    public const string Completed = "completed";
    public const string Failed = "failed";
    public const string Cancelled = "cancelled";

    public static readonly IReadOnlyList<string> Ordered =
    [
        Analysis,
        Coding,
        Testing,
        Review,
        Security,
        Benchmark,
        Push,
        Ci,
        MergePolicy,
        LocalSync,
        Completed
    ];
}

public sealed record CreateDevelopmentRunRequest(
    string Goal,
    string RepositoryPath,
    string Branch,
    string? RoadmapVersion = null,
    bool ConfirmCreate = false,
    Guid? ImprovementItemId = null,
    Guid? DiagnosisId = null);

public sealed record AdvanceDevelopmentRunRequest(
    string ExpectedCurrentStage,
    string Result,
    string? Evidence = null,
    Guid? CiRunId = null);

public sealed record FailDevelopmentRunRequest(
    string Reason,
    string? Evidence = null);

public sealed record CancelDevelopmentRunRequest(
    string Reason);

public sealed record DevelopmentRunTransition(
    string FromStage,
    string ToStage,
    string Result,
    string? Evidence,
    DateTimeOffset At);

public sealed record DevelopmentRun(
    Guid Id,
    string WorkspaceId,
    string Goal,
    string RepositoryPath,
    string Branch,
    string? RoadmapVersion,
    string Stage,
    string Status,
    Guid? CiRunId,
    IReadOnlyList<DevelopmentRunTransition> History,
    Guid? ImprovementItemId = null,
    Guid? DiagnosisId = null,
    DateTimeOffset CreatedAt = default,
    DateTimeOffset UpdatedAt = default,
    DateTimeOffset? CompletedAt = null);

public sealed record DevelopmentRunStatus(
    string Version,
    string WorkspaceId,
    int Runs,
    int ActiveRuns,
    int CompletedRuns,
    int FailedRuns,
    int CancelledRuns,
    bool Persisted,
    bool Resumable,
    bool SideEffectsAutomatic,
    IReadOnlyList<string> OrderedStages);

public sealed class DevelopmentRunValidationException(string message)
    : Exception(message);

public sealed class DevelopmentRunConflictException(string message)
    : Exception(message);
