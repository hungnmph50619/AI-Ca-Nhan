using System.Text.RegularExpressions;
using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public static class AgentFrameworkLimits
{
    public const int MaximumGoalCharacters = 4_000;
    public const int MaximumConversationMessages = 20;
    public const int MaximumConcurrentExecutions = 1;
}

public sealed class AgentValidationException(string message)
    : Exception(message);

public sealed class AgentBusyException(string message)
    : Exception(message);

public interface IAgent
{
    AgentDefinition Definition { get; }

    Task<AgentResult> ExecuteAsync(
        AgentExecutionContext context,
        CancellationToken cancellationToken = default);
}

public interface IAgentRegistry
{
    IReadOnlyList<AgentDefinition> GetAll();
    bool TryGet(string id, out IAgent? agent);
}

public sealed class AgentRegistry : IAgentRegistry
{
    private static readonly Regex ValidAgentId = new(
        @"^[a-z][a-z0-9-]*(.[a-z][a-z0-9-]*)+$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly IReadOnlySet<string> ValidContextKinds =
        new HashSet<string>(
            [
                AgentContextKinds.Memory,
                AgentContextKinds.Knowledge,
                AgentContextKinds.Tasks,
                AgentContextKinds.LifeContext
            ],
            StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, IAgent> _agents;
    private readonly IReadOnlyList<AgentDefinition> _definitions;

    public AgentRegistry(
        IEnumerable<IAgent> agents,
        IToolRegistry tools)
    {
        _agents = new Dictionary<string, IAgent>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var agent in agents)
        {
            Validate(agent.Definition, tools);
            if (!_agents.TryAdd(agent.Definition.Id, agent))
            {
                throw new InvalidOperationException(
                    $"Agent id bị trùng: {agent.Definition.Id}.");
            }
        }

        if (_agents.Count == 0)
        {
            throw new InvalidOperationException(
                "Agent Framework phải có ít nhất một agent đã đăng ký.");
        }

        _definitions = _agents.Values
            .Select(agent => agent.Definition)
            .OrderBy(
                definition => definition.Id,
                StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public IReadOnlyList<AgentDefinition> GetAll() =>
        _definitions;

    public bool TryGet(
        string id,
        out IAgent? agent)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            agent = null;
            return false;
        }

        return _agents.TryGetValue(
            id.Trim(),
            out agent);
    }

    private static void Validate(
        AgentDefinition definition,
        IToolRegistry tools)
    {
        if (string.IsNullOrWhiteSpace(definition.Id)
            || !ValidAgentId.IsMatch(definition.Id))
        {
            throw new InvalidOperationException(
                $"Agent id không hợp lệ: {definition.Id}.");
        }

        if (string.IsNullOrWhiteSpace(definition.Name)
            || string.IsNullOrWhiteSpace(definition.Role)
            || string.IsNullOrWhiteSpace(definition.Description))
        {
            throw new InvalidOperationException(
                $"Agent {definition.Id} thiếu name/role/description.");
        }

        if (!definition.RequiresExplicitInvocation)
        {
            throw new InvalidOperationException(
                $"v2.1.0 yêu cầu agent {definition.Id} chỉ được chạy khi có explicit invocation.");
        }

        if (definition.ToolExecutionEnabled)
        {
            throw new InvalidOperationException(
                $"v2.1.0 chưa cho phép agent {definition.Id} tự thực thi tool.");
        }

        if (definition.Capabilities.Count == 0)
        {
            throw new InvalidOperationException(
                $"Agent {definition.Id} phải khai báo ít nhất một capability.");
        }

        var capabilitySet = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var capability in definition.Capabilities)
        {
            if (string.IsNullOrWhiteSpace(capability)
                || !capabilitySet.Add(capability.Trim()))
            {
                throw new InvalidOperationException(
                    $"Agent {definition.Id} có capability trống hoặc trùng.");
            }
        }

        var contextSet = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var contextKind in definition.ContextKinds)
        {
            if (!ValidContextKinds.Contains(contextKind)
                || !contextSet.Add(contextKind))
            {
                throw new InvalidOperationException(
                    $"Agent {definition.Id} có context kind không hợp lệ hoặc trùng: {contextKind}.");
            }
        }

        var toolSet = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var toolName in definition.Tools)
        {
            if (!toolSet.Add(toolName)
                || !tools.TryGet(toolName, out var tool)
                || tool is null)
            {
                throw new InvalidOperationException(
                    $"Agent {definition.Id} bind tool không tồn tại hoặc bị trùng: {toolName}.");
            }
        }
    }
}

public sealed class PersonalAssistantFrameworkAgent(
    IChatTurnService chat) : IAgent
{
    public AgentDefinition Definition { get; } = new(
        "core.personal-assistant",
        "Personal Assistant",
        "general-assistant",
        "Agent nền v2.1.x dùng Context Manager để phân tích và trả lời theo mục tiêu người dùng, không tự chạy tool.",
        [
            "conversation",
            "memory-grounding",
            "knowledge-grounding",
            "task-context",
            "life-context"
        ],
        [
            "local.clock",
            "local.text_stats"
        ],
        [
            AgentContextKinds.Memory,
            AgentContextKinds.Knowledge,
            AgentContextKinds.Tasks,
            AgentContextKinds.LifeContext
        ],
        UsesAiProvider: true,
        ToolExecutionEnabled: false,
        RequiresExplicitInvocation: true);

    public async Task<AgentResult> ExecuteAsync(
        AgentExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        var request = context.Request;
        var messages = new List<ChatMessage>();

        if (request.Conversation is not null)
        {
            messages.AddRange(request.Conversation);
        }

        messages.Add(new ChatMessage(
            "user",
            BuildGoalPrompt(request.Goal ?? string.Empty)));

        var response = await chat.ExecuteAsync(
            new ChatRequest(
                messages,
                request.UseKnowledge,
                "normal",
                request.UseMemory,
                UseTools: false,
                UseTaskContext: request.UseTaskContext,
                UseLifeContext: request.UseLifeContext),
            allowToolProposal: false,
            auditAgent: Definition.Id,
            auditReason: "agent-framework-v2.1.1-explicit-invocation",
            cancellationToken);

        return new AgentResult(
            response.Message,
            response.Provider,
            response.Model,
            response.Sources,
            response.Context);
    }

    private string BuildGoalPrompt(string goal) =>
        $"""
        AGENT FRAMEWORK v2.1.1

        Agent: {Definition.Name}
        Role: {Definition.Role}

        Boundary:
        - Đây là một lần gọi agent do người dùng chủ động yêu cầu.
        - Chỉ phân tích và tạo câu trả lời.
        - Không tự chạy tool, không tạo agent khác, không gửi message cho agent khác.
        - Không tự tạo automation, không tự chạy task và không tự xác nhận side effect.
        - Các tool binding của agent chỉ là allowlist metadata cho các phiên bản orchestration sau.

        MỤC TIÊU:
        {goal}
        """;
}

public interface IAgentFrameworkService
{
    AgentFrameworkStatusResponse GetStatus();
    AgentCatalogResponse GetCatalog();
    AgentDefinition? GetAgent(string id);

    Task<AgentExecutionResponse> ExecuteAsync(
        string agentId,
        AgentExecutionRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class AgentFrameworkService(
    IAgentRegistry registry,
    IWorkspaceContextAccessor workspaceContext,
    IAuditRecorder audit,
    ILogger<AgentFrameworkService> logger) : IAgentFrameworkService
{
    public const string FrameworkVersion = "2.2.0";
    public const string NextStage = "v2.2.1-workflow-hardening";

    private static readonly SemaphoreSlim ExecutionGate =
        new(
            AgentFrameworkLimits.MaximumConcurrentExecutions,
            AgentFrameworkLimits.MaximumConcurrentExecutions);

    public AgentFrameworkStatusResponse GetStatus() =>
        new(
            PersonalAiRelease.Version,
            FrameworkVersion,
            registry.GetAll().Count,
            AgentFrameworkLimits.MaximumGoalCharacters,
            AgentFrameworkLimits.MaximumConversationMessages,
            AgentFrameworkLimits.MaximumConcurrentExecutions,
            AutomaticDelegationEnabled: false,
            AgentMessagingEnabled: true,
            SharedTaskQueueEnabled: false,
            ParallelAgentExecutionEnabled: false,
            ToolExecutionEnabled: false,
            NextStage,
            UserApprovedSequentialWorkflowsEnabled: true,
            ExplicitResultHandoffEnabled: true);

    public AgentCatalogResponse GetCatalog() =>
        new(
            PersonalAiRelease.Version,
            FrameworkVersion,
            registry.GetAll());

    public AgentDefinition? GetAgent(string id) =>
        registry.TryGet(id, out var agent)
            ? agent?.Definition
            : null;

    public async Task<AgentExecutionResponse> ExecuteAsync(
        string agentId,
        AgentExecutionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequest(request);

        if (!registry.TryGet(agentId, out var agent)
            || agent is null)
        {
            throw new KeyNotFoundException(
                "Không tìm thấy agent đã đăng ký.");
        }

        if (!await ExecutionGate.WaitAsync(
            0,
            cancellationToken))
        {
            audit.Record(
                agent.Definition.Id,
                "agent.execute",
                $"agent:{agent.Definition.Id}",
                "parallel-agent-execution-disabled",
                AuditResults.Blocked);

            throw new AgentBusyException(
                "Một agent execution khác đang chạy. v2.1.1 chưa cho phép chạy agent song song.");
        }

        var executionId = Guid.NewGuid();
        var startedAt = DateTimeOffset.UtcNow;
        var workspaceId =
            workspaceContext.CurrentWorkspaceId;

        audit.Record(
            agent.Definition.Id,
            "agent.execute",
            $"agent-execution:{executionId:D}",
            "explicit-user-invocation",
            AuditResults.Prepared);

        try
        {
            var result = await agent.ExecuteAsync(
                new AgentExecutionContext(
                    executionId,
                    workspaceId,
                    request),
                cancellationToken);

            var completedAt = DateTimeOffset.UtcNow;
            audit.Record(
                agent.Definition.Id,
                "agent.execute",
                $"agent-execution:{executionId:D}",
                "explicit-user-invocation",
                AuditResults.Succeeded);

            return new AgentExecutionResponse(
                executionId,
                agent.Definition.Id,
                agent.Definition.Name,
                AgentExecutionStatuses.Succeeded,
                workspaceId,
                startedAt,
                completedAt,
                result.Message,
                result.Provider,
                result.Model,
                result.Sources,
                result.Context,
                agent.Definition.Tools,
                ToolExecutionEnabled: false,
                Plan: result.Plan,
                Research: result.Research,
                Developer: result.Developer,
                Office: result.Office,
                Operator: result.Operator,
                Reviewer: result.Reviewer,
                Security: result.Security);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            audit.Record(
                agent.Definition.Id,
                "agent.execute",
                $"agent-execution:{executionId:D}",
                "request-cancelled",
                AuditResults.Cancelled);
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Agent {AgentId} execution {ExecutionId} failed.",
                agent.Definition.Id,
                executionId);

            audit.Record(
                agent.Definition.Id,
                "agent.execute",
                $"agent-execution:{executionId:D}",
                "execution-failed",
                AuditResults.Failed);
            throw;
        }
        finally
        {
            ExecutionGate.Release();
        }
    }

    private static void ValidateRequest(
        AgentExecutionRequest request)
    {
        var goal = (request.Goal ?? string.Empty).Trim();
        if (goal.Length == 0)
        {
            throw new AgentValidationException(
                "Mục tiêu agent không được để trống.");
        }

        if (goal.Length
            > AgentFrameworkLimits.MaximumGoalCharacters)
        {
            throw new AgentValidationException(
                $"Mục tiêu agent không được dài hơn {AgentFrameworkLimits.MaximumGoalCharacters:N0} ký tự.");
        }

        if (request.Conversation is null)
        {
            return;
        }

        if (request.Conversation.Count
            > AgentFrameworkLimits.MaximumConversationMessages)
        {
            throw new AgentValidationException(
                $"Agent chỉ nhận tối đa {AgentFrameworkLimits.MaximumConversationMessages} message lịch sử.");
        }

        foreach (var message in request.Conversation)
        {
            if (message is null
                || (message.Role != "user"
                    && message.Role != "assistant")
                || string.IsNullOrWhiteSpace(message.Content)
                || message.Content.Length > 12_000)
            {
                throw new AgentValidationException(
                    "Conversation của agent không hợp lệ.");
            }
        }
    }
}
