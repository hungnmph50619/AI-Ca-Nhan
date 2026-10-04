namespace PersonalAI.Web.Services;

public sealed record CapabilityToolRouteRequest(
    string Channel,
    IReadOnlyList<string> RequiredCapabilities,
    bool AllowHighRisk = false,
    bool RequireVerification = false);

public sealed record CapabilityToolRouteResult(
    string Channel,
    IReadOnlyList<UnifiedToolCapability> Tools,
    int ConsideredTools,
    int SelectedTools,
    string Reason);

public interface ICapabilityToolRouter
{
    CapabilityToolRouteResult Route(
        CapabilityToolRouteRequest request);
}

public sealed class CapabilityToolRouter(
    IToolCapabilityRegistry capabilities)
    : ICapabilityToolRouter
{
    public CapabilityToolRouteResult Route(
        CapabilityToolRouteRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var channel = (request.Channel ?? string.Empty)
            .Trim()
            .ToLowerInvariant();

        if (channel.Length == 0)
            throw new AgentValidationException(
                "Capability router yêu cầu channel.");

        var required = (request.RequiredCapabilities
                ?? Array.Empty<string>())
            .Where(value =>
                !string.IsNullOrWhiteSpace(value))
            .Select(value =>
                value.Trim().ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var all = capabilities.GetAll();

        var candidates = all
            .Where(tool =>
                BelongsToChannel(
                    tool,
                    channel))
            .ToArray();

        var selected = candidates
            .Where(tool =>
                required.All(requiredCapability =>
                    tool.Capabilities.Contains(
                        requiredCapability,
                        StringComparer.OrdinalIgnoreCase)))
            .Where(tool =>
                request.AllowHighRisk ||
                !tool.RiskLevel.Equals(
                    ToolRiskLevels.High,
                    StringComparison.OrdinalIgnoreCase))
            .Where(tool =>
                !request.RequireVerification ||
                tool.SupportsVerification)
            .OrderBy(tool =>
                RiskRank(tool.RiskLevel))
            .ThenBy(tool =>
                tool.RequiresConfirmation)
            .ThenBy(tool =>
                tool.Name,
                StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var reason =
            $"channel={channel}; required=[{string.Join(",", required)}]; " +
            $"considered={candidates.Length}; selected={selected.Length}; " +
            $"allowHighRisk={request.AllowHighRisk}; " +
            $"requireVerification={request.RequireVerification}";

        return new(
            channel,
            selected,
            candidates.Length,
            selected.Length,
            reason);
    }

    private static bool BelongsToChannel(
        UnifiedToolCapability tool,
        string channel) =>
        channel switch
        {
            ExecutionAgentChannels.Computer =>
                tool.Capabilities.Contains(
                    "computer",
                    StringComparer.OrdinalIgnoreCase),

            ExecutionAgentChannels.Browser =>
                tool.Capabilities.Contains(
                    "browser",
                    StringComparer.OrdinalIgnoreCase),

            ExecutionAgentChannels.Coding =>
                tool.Capabilities.Contains(
                    "coding",
                    StringComparer.OrdinalIgnoreCase),

            ExecutionAgentChannels.Connector =>
                tool.Capabilities.Contains(
                    "connector",
                    StringComparer.OrdinalIgnoreCase),

            _ => false
        };

    private static int RiskRank(
        string risk) =>
        risk.ToLowerInvariant() switch
        {
            ToolRiskLevels.Low => 0,
            ToolRiskLevels.Medium => 1,
            ToolRiskLevels.High => 2,
            _ => 3
        };
}
