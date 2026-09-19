namespace PersonalAI.Web.Models;

public sealed record WorkflowObservation(
    Guid WorkflowId,
    string WorkspaceId,
    string Status,
    int RequestedSteps,
    int CompletedSteps,
    int? FailedStep,
    string? StopReason,
    bool AwaitingHumanReview,
    DateTimeOffset StartedAt,
    DateTimeOffset LastUpdatedAt,
    long ElapsedMilliseconds);

public sealed record WorkflowObservabilityStatusResponse(
    string Version,
    int MaximumRecentWorkflows,
    int RetentionMinutes,
    bool MemoryOnly,
    bool WorkspaceIsolated,
    bool ContainsGoals,
    bool ContainsOutputs,
    bool ContainsReviewTokens,
    bool ContainsCredentialValues,
    bool GrantsExecutionRights,
    string NextStage);
