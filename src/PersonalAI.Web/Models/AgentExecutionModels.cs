namespace PersonalAI.Web.Models;

public static class AgentExecutionStatuses
{
    public const string Pending = "pending";
    public const string Running = "running";
    public const string Completed = "completed";
    public const string Failed = "failed";
}

public sealed record AgentExecutionTask(
    Guid Id,
    string Goal,
    string Status,
    DateTimeOffset CreatedAt);

public sealed record CreateAgentExecutionRequest(string Goal);
