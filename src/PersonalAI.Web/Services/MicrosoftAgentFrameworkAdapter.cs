using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace PersonalAI.Web.Services;

public sealed record MicrosoftAgentFrameworkAdapterStatus(
    string Package,
    string Version,
    int ExecutionAgents,
    bool ApprovalRequiredForSideEffects,
    bool DirectAutonomousExecutionEnabled);

public interface IMicrosoftAgentFrameworkAdapter
{
    MicrosoftAgentFrameworkAdapterStatus GetStatus();

    AIFunction CreateApprovalRequiredTool(
        IExecutionAgent agent);
}

public sealed class MicrosoftAgentFrameworkAdapter(
    IExecutionAgentRegistry agents)
    : IMicrosoftAgentFrameworkAdapter
{
    public MicrosoftAgentFrameworkAdapterStatus GetStatus()
    {
        var version =
            typeof(AIAgent).Assembly
                .GetName()
                .Version?
                .ToString()
            ?? "unknown";

        return new(
            "Microsoft.Agents.AI",
            version,
            agents.GetAll().Count,
            ApprovalRequiredForSideEffects: true,
            DirectAutonomousExecutionEnabled: false);
    }

    public AIFunction CreateApprovalRequiredTool(
        IExecutionAgent agent)
    {
        ArgumentNullException.ThrowIfNull(agent);

        if (!agent.Definition.HasSideEffects)
        {
            throw new InvalidOperationException(
                $"Execution agent {agent.Definition.Id} phải khai báo side effect.");
        }

        var channel = agent.Definition.Channels.Count == 1
            ? agent.Definition.Channels[0]
            : throw new InvalidOperationException(
                $"Execution agent {agent.Definition.Id} phải có đúng một channel ở v3.8.1.");

        var function = AIFunctionFactory.Create(
            async (
                string goal,
                CancellationToken cancellationToken) =>
            {
                var request = new ExecutionAgentRequest(
                    goal,
                    channel,
                    "microsoft-agent-framework-tool-call");

                if (!agent.CanHandle(
                        request,
                        out _,
                        out var reason))
                {
                    throw new InvalidOperationException(
                        reason);
                }

                var result = await agent.ExecuteAsync(
                    request,
                    cancellationToken);

                return JsonSerializer.Serialize(
                    result);
            },
            name: ToFunctionName(
                agent.Definition.Id),
            description:
                agent.Definition.Description);

        return new ApprovalRequiredAIFunction(
            function);
    }

    private static string ToFunctionName(
        string agentId)
    {
        var chars = agentId
            .Trim()
            .ToLowerInvariant()
            .Select(ch =>
                char.IsLetterOrDigit(ch)
                    ? ch
                    : '_')
            .ToArray();

        var name = new string(chars);

        while (name.Contains(
            "__",
            StringComparison.Ordinal))
        {
            name = name.Replace(
                "__",
                "_",
                StringComparison.Ordinal);
        }

        return $"run_{name.Trim('_')}";
    }
}
