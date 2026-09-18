using System.Text.Json;

namespace PersonalAI.Web.Models;

public static class PersonalTaskStatuses
{
    public const string Planned = "planned";
    public const string Running = "running";
    public const string Completed = "completed";
    public const string Failed = "failed";
    public const string Interrupted = "interrupted";
    public const string Cancelled = "cancelled";
}

public static class PersonalTaskStepStatuses
{
    public const string Pending = "pending";
    public const string Running = "running";
    public const string Completed = "completed";
    public const string Failed = "failed";
    public const string Interrupted = "interrupted";
}

public static class PersonalTaskRetrySafety
{
    public const string Safe = "safe";
    public const string ReviewRequired = "review-required";
    public const string Blocked = "blocked";
}

public sealed record CreatePersonalTaskRequest(
    string Goal,
    IReadOnlyList<Guid>? DependsOnTaskIds = null);

public sealed record ExecutePersonalTaskStepRequest(bool Confirmed = false);

public sealed record RetryPersonalTaskStepRequest(bool ConfirmedReview = false);

public sealed record PreparePersonalTaskRequest(
    string Goal,
    string Plan,
    IReadOnlyList<PersonalTaskStepDraft> Steps,
    IReadOnlyList<Guid>? DependsOnTaskIds = null);

public sealed record PersonalTaskStepDraft(
    string Title,
    string Description,
    string ToolName,
    JsonElement Arguments,
    IReadOnlyList<int>? DependsOn = null);



public sealed record PersonalTaskStep(
    int Index,
    string Title,
    string Description,
    string ToolName,
    JsonElement Arguments,
    IReadOnlyList<string> RequiredPermissions,
    bool RequiresConfirmation,
    string Status,
    Guid? InvocationId = null,
    string? LocalSummary = null,
    DateTimeOffset? StartedAt = null,
    DateTimeOffset? CompletedAt = null,
    int AttemptCount = 0,
    IReadOnlyList<int>? DependsOn = null);

public sealed record PersonalTask(
    Guid Id,
    string Goal,
    string Status,
    string Plan,
    IReadOnlyList<PersonalTaskStep> Steps,
    int CurrentStep,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    string? Result,
    string PlanningProvider,
    string PlanningModel,
    IReadOnlyList<Guid>? DependsOnTaskIds = null);

public sealed record PersonalTaskListResponse(
    bool Persistent,
    int MaximumTasks,
    IReadOnlyList<PersonalTask> Tasks);

public sealed record PersonalTaskStepExecutionResponse(
    PersonalTask Task,
    ToolExecutionResponse Execution,
    string LocalSummary);


public sealed record PersonalTaskRetryAssessment(
    Guid TaskId,
    int StepIndex,
    string ToolName,
    string Safety,
    bool CanRetry,
    bool RequiresReviewConfirmation,
    string Message);

public sealed record PersonalTaskRetryResponse(
    PersonalTask Task,
    PersonalTaskRetryAssessment Assessment);
