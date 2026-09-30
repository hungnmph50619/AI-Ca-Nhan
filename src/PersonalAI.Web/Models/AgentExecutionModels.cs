namespace PersonalAI.Web.Models;

public sealed record AgentExecutionTask(
    Guid Id,
    string Goal,
    string Status,
    DateTimeOffset CreatedAt);

public sealed record CreateAgentExecutionRequest(string Goal);
