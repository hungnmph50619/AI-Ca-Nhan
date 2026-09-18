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

public sealed record CreatePersonalTaskRequest(string Goal);

public sealed record ExecutePersonalTaskStepRequest(bool Confirmed = false);

public sealed record PreparePersonalTaskRequest(
    string Goal,
    string Plan,
    IReadOnlyList<PersonalTaskStepDraft> Steps);

public sealed record PersonalTaskStepDraft(
    string Title,
    string Description,
    string ToolName,
    JsonElement Arguments);



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
    DateTimeOffset? CompletedAt = null);

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
    string PlanningModel);

public sealed record PersonalTaskListResponse(
    bool Persistent,
    int MaximumTasks,
    IReadOnlyList<PersonalTask> Tasks);

public sealed record PersonalTaskStepExecutionResponse(
    PersonalTask Task,
    ToolExecutionResponse Execution,
    string LocalSummary);
