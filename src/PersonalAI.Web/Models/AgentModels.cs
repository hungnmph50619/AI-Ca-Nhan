namespace PersonalAI.Web.Models;

public static class AgentExecutionStatuses
{
    public const string Succeeded = "succeeded";
    public const string Busy = "busy";
    public const string InvalidInput = "invalid-input";
    public const string NotFound = "not-found";
    public const string Failed = "failed";
}

public static class AgentContextKinds
{
    public const string Memory = "memory";
    public const string Knowledge = "knowledge";
    public const string Tasks = "tasks";
    public const string LifeContext = "life-context";
}

public sealed record AgentDefinition(
    string Id,
    string Name,
    string Role,
    string Description,
    IReadOnlyList<string> Capabilities,
    IReadOnlyList<string> Tools,
    IReadOnlyList<string> ContextKinds,
    bool UsesAiProvider,
    bool ToolExecutionEnabled,
    bool RequiresExplicitInvocation);

public sealed record AgentFrameworkStatusResponse(
    string Version,
    string FrameworkVersion,
    int RegisteredAgents,
    int MaximumGoalCharacters,
    int MaximumConversationMessages,
    int MaximumConcurrentExecutions,
    bool AutomaticDelegationEnabled,
    bool AgentMessagingEnabled,
    bool SharedTaskQueueEnabled,
    bool ParallelAgentExecutionEnabled,
    bool ToolExecutionEnabled,
    string NextStage);

public sealed record AgentCatalogResponse(
    string Version,
    string FrameworkVersion,
    IReadOnlyList<AgentDefinition> Agents);

public sealed record AgentExecutionRequest(
    string? Goal,
    IReadOnlyList<ChatMessage>? Conversation = null,
    bool UseKnowledge = true,
    bool UseMemory = true,
    bool UseTaskContext = true,
    bool UseLifeContext = true);

public sealed record AgentExecutionResponse(
    Guid ExecutionId,
    string AgentId,
    string AgentName,
    string Status,
    string WorkspaceId,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    string? Message,
    string? Provider,
    string? Model,
    IReadOnlyList<ChatSource> Sources,
    ContextSelectionReport? Context,
    IReadOnlyList<string> BoundTools,
    bool ToolExecutionEnabled,
    PlannerPlan? Plan = null,
    ResearchReport? Research = null,
    string? Error = null);

public sealed record AgentExecutionContext(
    Guid ExecutionId,
    string WorkspaceId,
    AgentExecutionRequest Request);

public sealed record AgentResult(
    string Message,
    string Provider,
    string Model,
    IReadOnlyList<ChatSource> Sources,
    ContextSelectionReport? Context,
    PlannerPlan? Plan = null,
    ResearchReport? Research = null);
