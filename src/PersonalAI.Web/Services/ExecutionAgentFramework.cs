using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public static class ExecutionAgentChannels
{
    public const string Computer = "computer";
    public const string Browser = "browser";
    public const string Coding = "coding";
    public const string Connector = "connector";
}

public sealed record ExecutionAgentDefinition(
    string Id,
    string Name,
    string Description,
    IReadOnlyList<string> Capabilities,
    IReadOnlyList<string> Channels,
    bool HasSideEffects,
    bool RequiresExplicitInvocation,
    bool SupportsVerification,
    bool SupportsRecovery);

public sealed record ExecutionAgentRequest(
    string Goal,
    string Channel,
    string? Reason = null);

public sealed record ExecutionAgentResult(
    string AgentId,
    string Status,
    string Summary,
    IReadOnlyList<string> Evidence,
    bool ChangedExternalState,
    bool Verified,
    string Provider,
    string Model);

public interface IExecutionAgent
{
    ExecutionAgentDefinition Definition { get; }

    bool CanHandle(
        ExecutionAgentRequest request,
        out double confidence,
        out string reason);

    Task<ExecutionAgentResult> ExecuteAsync(
        ExecutionAgentRequest request,
        CancellationToken cancellationToken = default);
}

public interface IExecutionAgentRegistry
{
    IReadOnlyList<ExecutionAgentDefinition> GetAll();

    bool TryGet(
        string id,
        out IExecutionAgent? agent);

    IReadOnlyList<IExecutionAgent> FindByChannel(
        string channel);
}

public sealed class ExecutionAgentRegistry(
    IEnumerable<IExecutionAgent> agents)
    : IExecutionAgentRegistry
{
    private readonly Dictionary<string, IExecutionAgent> _agents =
        Build(agents);

    public IReadOnlyList<ExecutionAgentDefinition> GetAll() =>
        _agents.Values
            .Select(item => item.Definition)
            .OrderBy(
                item => item.Id,
                StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public bool TryGet(
        string id,
        out IExecutionAgent? agent)
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

    public IReadOnlyList<IExecutionAgent> FindByChannel(
        string channel)
    {
        var normalized = (channel ?? string.Empty)
            .Trim();

        if (normalized.Length == 0)
            return Array.Empty<IExecutionAgent>();

        return _agents.Values
            .Where(agent =>
                agent.Definition.Channels.Any(item =>
                    item.Equals(
                        normalized,
                        StringComparison.OrdinalIgnoreCase)))
            .OrderBy(
                agent => agent.Definition.Id,
                StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static Dictionary<string, IExecutionAgent> Build(
        IEnumerable<IExecutionAgent> agents)
    {
        ArgumentNullException.ThrowIfNull(agents);

        var result = new Dictionary<string, IExecutionAgent>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var agent in agents)
        {
            Validate(agent.Definition);

            if (!result.TryAdd(
                    agent.Definition.Id,
                    agent))
            {
                throw new InvalidOperationException(
                    $"Execution agent id bị trùng: {agent.Definition.Id}.");
            }
        }

        if (result.Count == 0)
        {
            throw new InvalidOperationException(
                "Execution Agent Framework phải có ít nhất một agent.");
        }

        return result;
    }

    private static void Validate(
        ExecutionAgentDefinition definition)
    {
        if (string.IsNullOrWhiteSpace(definition.Id) ||
            !definition.Id.Contains(
                '.',
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Execution agent id không hợp lệ.");
        }

        if (string.IsNullOrWhiteSpace(definition.Name) ||
            string.IsNullOrWhiteSpace(definition.Description))
        {
            throw new InvalidOperationException(
                $"Execution agent {definition.Id} thiếu metadata.");
        }

        if (definition.Capabilities.Count == 0 ||
            definition.Channels.Count == 0)
        {
            throw new InvalidOperationException(
                $"Execution agent {definition.Id} phải khai báo capability và channel.");
        }

        if (!definition.HasSideEffects ||
            !definition.RequiresExplicitInvocation)
        {
            throw new InvalidOperationException(
                $"Execution agent {definition.Id} phải khai báo side effect và explicit invocation.");
        }
    }
}

public sealed class ComputerOperatorExecutionAgent(
    IComputerOperatorTaskService computerOperator)
    : IExecutionAgent
{
    public const string AgentId = "execution.computer-operator";

    public ExecutionAgentDefinition Definition { get; } = new(
        AgentId,
        "Computer Operator",
        "Agent điều khiển desktop tổng quát bằng quan sát, lập kế hoạch, một action mỗi bước, xác minh và recovery.",
        [
            "desktop-observation",
            "visual-targeting",
            "mouse-keyboard-execution",
            "verify-after-action",
            "failure-recovery",
            "anti-loop"
        ],
        [ExecutionAgentChannels.Computer],
        HasSideEffects: true,
        RequiresExplicitInvocation: true,
        SupportsVerification: true,
        SupportsRecovery: true);

    public bool CanHandle(
        ExecutionAgentRequest request,
        out double confidence,
        out string reason)
    {
        ArgumentNullException.ThrowIfNull(request);

        var goal = (request.Goal ?? string.Empty).Trim();
        var channel = (request.Channel ?? string.Empty).Trim();

        if (goal.Length < 2)
        {
            confidence = 0;
            reason = "Goal trống hoặc quá ngắn.";
            return false;
        }

        if (!channel.Equals(
                ExecutionAgentChannels.Computer,
                StringComparison.OrdinalIgnoreCase))
        {
            confidence = 0;
            reason =
                $"Computer Operator chỉ nhận channel={ExecutionAgentChannels.Computer}.";
            return false;
        }

        confidence = 0.99;
        reason =
            "Yêu cầu đã được router/explicit invocation gán cho channel computer.";
        return true;
    }

    public async Task<ExecutionAgentResult> ExecuteAsync(
        ExecutionAgentRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!CanHandle(
                request,
                out _,
                out var reason))
        {
            throw new AgentValidationException(reason);
        }

        var result = await computerOperator.RunAsync(
            request.Goal,
            cancellationToken);

        var evidence = result.Steps
            .Select(step =>
                $"#{step.Index} {step.Action}: {step.Detail}")
            .ToArray();

        return new(
            AgentId,
            result.Completed
                ? AgentExecutionStatuses.Succeeded
                : AgentExecutionStatuses.Failed,
            result.Summary,
            evidence,
            ChangedExternalState: result.Steps.Count > 0,
            Verified: result.Completed,
            result.Provider,
            result.Model);
    }
}
