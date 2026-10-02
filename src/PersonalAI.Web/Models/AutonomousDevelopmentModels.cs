namespace PersonalAI.Web.Models;

public static class AutonomousDevelopmentStopReasons
{
    public const string Completed = "completed";
    public const string EmergencyStop = "emergency-stop";
    public const string NoEligibleImprovement = "no-eligible-improvement";
    public const string AwaitingDiagnosis = "awaiting-diagnosis";
    public const string CodingFailed = "coding-failed";
    public const string GateFailed = "gate-failed";
    public const string CiPending = "ci-pending";
    public const string CiRepairRequested = "ci-repair-requested";
    public const string UserConfirmationRequired = "user-confirmation-required";
    public const string DirtyTreeDeferred = "dirty-tree-deferred";
    public const string TransitionBudgetReached = "transition-budget-reached";
}

public sealed record RunAutonomousDevelopmentRequest(
    Guid? DevelopmentRunId,
    string RepositoryPath,
    string BaseBranch,
    string GitHubRepository,
    string CredentialRef,
    string DotnetTargetPath = "PersonalAI.sln",
    int MaximumTransitions = 12,
    int MaximumRegressionCases = 50,
    int MaximumBenchmarkCases = 50,
    bool ConfirmAutonomousRun = false,
    bool ConfirmExternalAi = false,
    bool ConfirmGitHubSideEffects = false,
    bool AllowLowRiskAutomaticMerge = false);

public sealed record AutonomousDevelopmentStep(
    string StageBefore,
    string StageAfter,
    string Action,
    string Result,
    Guid? EvidenceId,
    DateTimeOffset At);

public sealed record AutonomousDevelopmentRunResult(
    string Version,
    string WorkspaceId,
    Guid? DevelopmentRunId,
    string? Stage,
    string? Status,
    int Transitions,
    bool Completed,
    bool EmergencyStopped,
    bool PolicyBypassed,
    string StopReason,
    IReadOnlyList<AutonomousDevelopmentStep> Steps,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt);

public sealed record AutonomousDevelopmentStatus(
    string Version,
    string WorkspaceId,
    bool FullPipelineControllerEnabled,
    bool EmergencyStopActive,
    string? EmergencyStopReason,
    bool PolicyBypassAllowed,
    int MaximumTransitionsPerInvocation,
    int MaximumCodingEdits,
    int MaximumCiRepairAttempts,
    bool DirectMainPushAllowed,
    bool HighRiskAutoMergeAllowed,
    IReadOnlyList<string> Pipeline);

public sealed record SetAutonomousEmergencyStopRequest(
    bool Stop,
    string Reason,
    bool Confirm = false);

public sealed record AutonomousEmergencyStopState(
    string WorkspaceId,
    bool Stopped,
    string? Reason,
    DateTimeOffset UpdatedAt);

public sealed record AutonomousCodingResult(
    Guid Id,
    string WorkspaceId,
    Guid DevelopmentRunId,
    string WorktreePath,
    int FilesChanged,
    IReadOnlyList<string> Paths,
    string Summary,
    DateTimeOffset CompletedAt);

public sealed class AutonomousDevelopmentValidationException(string message)
    : Exception(message);
