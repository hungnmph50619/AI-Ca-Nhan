namespace PersonalAI.Web.Services;

public sealed record ExecutionGatewayRequest(
    string Goal,
    string? Channel = null,
    string? AgentId = null,
    bool ConfirmExecution = false);

public sealed record ExecutionGatewayCandidate(
    string AgentId,
    string Name,
    double Confidence,
    string Reason);

public sealed record ExecutionGatewayPreview(
    string Goal,
    string? Channel,
    string? AgentId,
    IReadOnlyList<ExecutionGatewayCandidate> Candidates,
    string? SelectedAgentId,
    bool ConfirmationRequired,
    string Reason);

public sealed record ExecutionGatewayResult(
    ExecutionGatewayPreview Route,
    ExecutionAgentResult Result);

public interface IExecutionGateway
{
    ExecutionGatewayPreview Preview(
        ExecutionGatewayRequest request);

    Task<ExecutionGatewayResult> ExecuteAsync(
        ExecutionGatewayRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class ExecutionGateway(
    IExecutionAgentRegistry agents)
    : IExecutionGateway
{
    private const double MinimumConfidence = 0.60;
    private const double AmbiguityDelta = 0.02;

    public ExecutionGatewayPreview Preview(
        ExecutionGatewayRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var goal = (request.Goal ?? string.Empty).Trim();

        if (goal.Length < 2)
            throw new AgentValidationException(
                "Execution Gateway cần goal rõ ràng.");

        var channel = Normalize(
            request.Channel);
        var agentId = Normalize(
            request.AgentId);

        IReadOnlyList<IExecutionAgent> pool;

        if (agentId.Length > 0)
        {
            if (!agents.TryGet(
                    agentId,
                    out var explicitAgent) ||
                explicitAgent is null)
            {
                throw new AgentValidationException(
                    $"Không tìm thấy execution agent: {agentId}.");
            }

            pool = new[] { explicitAgent };

            if (channel.Length > 0 &&
                !explicitAgent.Definition.Channels.Any(item =>
                    item.Equals(
                        channel,
                        StringComparison.OrdinalIgnoreCase)))
            {
                throw new AgentValidationException(
                    $"Agent {agentId} không thuộc channel {channel}.");
            }
        }
        else
        {
            if (channel.Length == 0)
            {
                throw new AgentValidationException(
                    "Execution Gateway v3.10.1 yêu cầu Channel hoặc AgentId rõ ràng; chưa tự đoán channel từ natural language.");
            }

            pool = agents.FindByChannel(channel);

            if (pool.Count == 0)
            {
                throw new AgentValidationException(
                    $"Không có execution agent cho channel {channel}.");
            }
        }

        var candidates = pool
            .Select(agent =>
            {
                var canHandle = agent.CanHandle(
                    new ExecutionAgentRequest(
                        goal,
                        channel.Length > 0
                            ? channel
                            : agent.Definition.Channels[0],
                        "execution-gateway"),
                    out var confidence,
                    out var reason);

                return new
                {
                    Agent = agent,
                    CanHandle = canHandle,
                    Candidate = new ExecutionGatewayCandidate(
                        agent.Definition.Id,
                        agent.Definition.Name,
                        canHandle
                            ? confidence
                            : 0,
                        reason)
                };
            })
            .Where(item =>
                item.CanHandle &&
                item.Candidate.Confidence >= MinimumConfidence)
            .OrderByDescending(item =>
                item.Candidate.Confidence)
            .ThenBy(item =>
                item.Agent.Definition.Id,
                StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (candidates.Length == 0)
        {
            return new(
                goal,
                channel.Length == 0
                    ? null
                    : channel,
                agentId.Length == 0
                    ? null
                    : agentId,
                Array.Empty<ExecutionGatewayCandidate>(),
                null,
                ConfirmationRequired: false,
                "Không agent nào đủ confidence để xử lý yêu cầu.");
        }

        var ambiguous =
            candidates.Length > 1 &&
            Math.Abs(
                candidates[0].Candidate.Confidence -
                candidates[1].Candidate.Confidence) <= AmbiguityDelta;

        var selected = ambiguous
            ? null
            : candidates[0].Agent;

        return new(
            goal,
            channel.Length == 0
                ? null
                : channel,
            agentId.Length == 0
                ? null
                : agentId,
            candidates
                .Select(item =>
                    item.Candidate)
                .ToArray(),
            selected?.Definition.Id,
            selected?.Definition.HasSideEffects == true ||
            selected?.Definition.RequiresExplicitInvocation == true,
            ambiguous
                ? "Có nhiều execution agent có confidence gần nhau; cần chỉ định AgentId."
                : $"Đã route tới {selected!.Definition.Id}.");
    }

    public async Task<ExecutionGatewayResult> ExecuteAsync(
        ExecutionGatewayRequest request,
        CancellationToken cancellationToken = default)
    {
        var route = Preview(request);

        if (route.SelectedAgentId is null)
        {
            throw new AgentValidationException(
                route.Reason);
        }

        if (!agents.TryGet(
                route.SelectedAgentId,
                out var agent) ||
            agent is null)
        {
            throw new AgentValidationException(
                "Execution agent đã biến mất khỏi registry.");
        }

        if (route.ConfirmationRequired &&
            !request.ConfirmExecution)
        {
            throw new AgentValidationException(
                $"Cần ConfirmExecution=true trước khi chạy {agent.Definition.Id} vì agent có side effect.");
        }

        var channel =
            route.Channel ??
            agent.Definition.Channels[0];

        var result = await agent.ExecuteAsync(
            new ExecutionAgentRequest(
                route.Goal,
                channel,
                "execution-gateway"),
            cancellationToken);

        return new(
            route,
            result);
    }

    private static string Normalize(
        string? value) =>
        (value ?? string.Empty)
            .Trim()
            .ToLowerInvariant();
}
