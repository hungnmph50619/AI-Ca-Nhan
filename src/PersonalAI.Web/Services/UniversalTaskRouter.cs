using System.Text.RegularExpressions;

namespace PersonalAI.Web.Services;

public static class UniversalRouteRisk
{
    public const string Low = "low";
    public const string Medium = "medium";
    public const string High = "high";
}

public sealed record UniversalTaskRouteRequest(
    string Goal,
    string? PreferredChannel = null,
    bool ConfirmExecution = false);

public sealed record UniversalTaskRouteCandidate(
    string Channel,
    double Confidence,
    double UtilityScore,
    string Risk,
    int RelativeCost,
    int RelativeLatency,
    int AvailableAgents,
    int DirectToolCount,
    string? PreferredToolName,
    string Reason);

public sealed record UniversalTaskRoutePreview(
    string Goal,
    IReadOnlyList<UniversalTaskRouteCandidate> Candidates,
    string? SelectedChannel,
    bool NeedsFurtherRouting,
    string Reason);

public sealed record UniversalTaskRouteExecution(
    UniversalTaskRoutePreview Route,
    ExecutionGatewayResult Gateway);

public interface IUniversalTaskRouter
{
    UniversalTaskRoutePreview Preview(
        UniversalTaskRouteRequest request);

    Task<UniversalTaskRouteExecution> ExecuteAsync(
        UniversalTaskRouteRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class UniversalTaskRouter(
    IExecutionAgentRegistry executionAgents,
    IExecutionGateway gateway,
    IToolCapabilityRegistry? toolCapabilities = null,
    IUniversalCapabilityDiscoveryService? capabilityDiscovery = null)
    : IUniversalTaskRouter
{
    private const double MinimumSelectionConfidence = 0.66;
    private const double MinimumWinningMargin = 0.06;
    private const double CostPenaltyWeight = 0.025;
    private const double LatencyPenaltyWeight = 0.020;
    private const double MediumRiskPenalty = 0.015;
    private const double HighRiskPenalty = 0.040;
    private const double DirectToolUtilityBonus = 0.080;

    private static readonly Regex UrlRegex = new(
        @"https?://[^\s]+",
        RegexOptions.IgnoreCase |
        RegexOptions.CultureInvariant |
        RegexOptions.Compiled);

    private static readonly string[] CodingTerms =
    [
        "code",
        "coding",
        "repository",
        "repo",
        "project",
        "build",
        "test",
        "compile",
        "git ",
        "github",
        "csproj",
        "sln",
        "dotnet",
        "bug",
        "lỗi code",
        "sửa code",
        "viết code",
        "pull request",
        "commit"
    ];

    private static readonly string[] BrowserTerms =
    [
        "website",
        "web ",
        "trang web",
        "browser",
        "trình duyệt",
        "url",
        "http",
        "đăng nhập trang",
        "mở trang"
    ];

    private static readonly string[] ComputerTerms =
    [
        "desktop",
        "màn hình",
        "chuột",
        "bàn phím",
        "click",
        "kéo thả",
        "ứng dụng",
        "phần mềm",
        "cửa sổ",
        "windows",
        "chương trình",
        "giao diện",
        "foreground",
        "window"
    ];

    private static readonly string[] ConnectorTerms =
    [
        "gmail",
        "email",
        "calendar",
        "lịch",
        "google drive",
        "onedrive",
        "slack",
        "connector",
        "mcp"
    ];

    public UniversalTaskRoutePreview Preview(
        UniversalTaskRouteRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var goal = (request.Goal ?? string.Empty).Trim();

        if (goal.Length < 2)
            throw new AgentValidationException(
                "Universal Router cần goal rõ ràng.");

        var preferred = Normalize(
            request.PreferredChannel);

        var runtimeCapabilities =
            capabilityDiscovery?.Discover();

        var candidates = new[]
        {
            Score(
                ExecutionAgentChannels.Browser,
                goal,
                preferred,
                runtimeCapabilities),
            Score(
                ExecutionAgentChannels.Coding,
                goal,
                preferred,
                runtimeCapabilities),
            Score(
                ExecutionAgentChannels.Computer,
                goal,
                preferred,
                runtimeCapabilities),
            Score(
                ExecutionAgentChannels.Connector,
                goal,
                preferred,
                runtimeCapabilities)
        }
        .OrderByDescending(item =>
            item.UtilityScore)
        .ThenByDescending(item =>
            item.Confidence)
        .ThenBy(item =>
            item.RelativeCost)
        .ThenBy(item =>
            item.RelativeLatency)
        .ToArray();

        var selectable = candidates
            .Where(item =>
                item.AvailableAgents > 0)
            .ToArray();

        if (selectable.Length == 0)
        {
            return new(
                goal,
                candidates,
                null,
                NeedsFurtherRouting: true,
                "Không có execution agent khả dụng cho bất kỳ channel nào.");
        }

        var top = selectable[0];
        var runnerUp = selectable.Length > 1
            ? selectable[1]
            : null;

        var margin = runnerUp is null
            ? 1.0
            : top.UtilityScore -
              runnerUp.UtilityScore;

        var selected =
            top.Confidence >= MinimumSelectionConfidence &&
            margin >= MinimumWinningMargin
                ? top.Channel
                : null;

        return new(
            goal,
            candidates,
            selected,
            NeedsFurtherRouting: selected is null,
            selected is null
                ? $"Router chưa đủ chắc: top={top.Channel}; confidence={top.Confidence:0.00}; utility={top.UtilityScore:0.00}; margin={margin:0.00}. Cần model/user chọn channel."
                : $"Chọn {top.Channel}: confidence={top.Confidence:0.00}; utility={top.UtilityScore:0.00}; margin={margin:0.00}; risk={top.Risk}; cost={top.RelativeCost}; latency={top.RelativeLatency}.");
    }

    public async Task<UniversalTaskRouteExecution> ExecuteAsync(
        UniversalTaskRouteRequest request,
        CancellationToken cancellationToken = default)
    {
        var route = Preview(request);

        if (route.SelectedChannel is null)
        {
            throw new AgentValidationException(
                route.Reason);
        }

        var gatewayResult = await gateway.ExecuteAsync(
            new ExecutionGatewayRequest(
                route.Goal,
                Channel: route.SelectedChannel,
                ConfirmExecution: request.ConfirmExecution),
            cancellationToken);

        return new(
            route,
            gatewayResult);
    }

    private UniversalTaskRouteCandidate Score(
        string channel,
        string goal,
        string preferred,
        UniversalCapabilitySnapshot? runtimeCapabilities)
    {
        var normalized = goal.ToLowerInvariant();
        var confidence = 0.20;
        var reasons = new List<string>();

        if (preferred.Length > 0 &&
            preferred.Equals(
                channel,
                StringComparison.OrdinalIgnoreCase))
        {
            confidence += 0.35;
            reasons.Add("preferred-channel");
        }

        switch (channel)
        {
            case ExecutionAgentChannels.Browser:
                if (UrlRegex.IsMatch(goal))
                {
                    confidence += 0.65;
                    reasons.Add("explicit-url");
                }

                AddTerms(
                    normalized,
                    BrowserTerms,
                    0.12,
                    reasons,
                    ref confidence);
                break;

            case ExecutionAgentChannels.Coding:
                AddTerms(
                    normalized,
                    CodingTerms,
                    0.13,
                    reasons,
                    ref confidence);

                if (normalized.Contains(
                        ".cs",
                        StringComparison.OrdinalIgnoreCase) ||
                    normalized.Contains(
                        ".js",
                        StringComparison.OrdinalIgnoreCase) ||
                    normalized.Contains(
                        ".py",
                        StringComparison.OrdinalIgnoreCase))
                {
                    confidence += 0.18;
                    reasons.Add("source-file");
                }
                break;

            case ExecutionAgentChannels.Computer:
                AddTerms(
                    normalized,
                    ComputerTerms,
                    0.12,
                    reasons,
                    ref confidence);

                if (normalized.Contains(
                        "mở ",
                        StringComparison.OrdinalIgnoreCase) &&
                    !UrlRegex.IsMatch(goal))
                {
                    confidence += 0.08;
                    reasons.Add("open-local-context");
                }
                break;

            case ExecutionAgentChannels.Connector:
                AddTerms(
                    normalized,
                    ConnectorTerms,
                    0.16,
                    reasons,
                    ref confidence);
                break;
        }

        if (runtimeCapabilities is not null)
        {
            var runtimeSignals =
                runtimeCapabilities.ForChannel(channel)
                    .Where(item => item.Available)
                    .ToArray();

            var runtimeBonus =
                runtimeCapabilities.ConfidenceBonusFor(
                    channel);

            if (runtimeBonus > 0)
            {
                confidence += runtimeBonus;
                reasons.Add(
                    $"runtime-capability-bonus:{runtimeBonus:0.00}");
            }

            foreach (var signal in runtimeSignals.Take(3))
            {
                reasons.Add(
                    $"capability:{signal.Key}");
            }
        }

        confidence = Math.Clamp(
            confidence,
            0,
            0.99);

        var available =
            executionAgents.FindByChannel(
                channel).Count;

        var directTools = FindDirectTools(
            channel);

        var preferredTool =
            directTools.FirstOrDefault();

        if (preferredTool is not null)
        {
            reasons.Add(
                $"direct-tool:{preferredTool.Name}");
        }

        var (risk, cost, latency) =
            channel switch
            {
                ExecutionAgentChannels.Browser =>
                    (UniversalRouteRisk.Medium, 2, 2),

                ExecutionAgentChannels.Coding =>
                    (UniversalRouteRisk.High, 4, 4),

                ExecutionAgentChannels.Computer =>
                    (UniversalRouteRisk.High, 5, 5),

                ExecutionAgentChannels.Connector =>
                    (UniversalRouteRisk.Medium, 1, 1),

                _ =>
                    (UniversalRouteRisk.High, 5, 5)
            };

        if (available == 0)
            reasons.Add("no-agent-available");

        var utility = Math.Clamp(
            confidence -
            (cost * CostPenaltyWeight) -
            (latency * LatencyPenaltyWeight) -
            RiskPenalty(risk) +
            (preferredTool is null
                ? 0
                : DirectToolUtilityBonus),
            0,
            0.99);

        reasons.Add(
            $"utility:{utility:0.00}");

        return new(
            channel,
            confidence,
            utility,
            risk,
            cost,
            latency,
            available,
            directTools.Count,
            preferredTool?.Name,
            reasons.Count == 0
                ? "no-strong-signal"
                : string.Join(",", reasons));
    }

    private IReadOnlyList<UnifiedToolCapability> FindDirectTools(
        string channel)
    {
        if (toolCapabilities is null)
            return Array.Empty<UnifiedToolCapability>();

        return toolCapabilities
            .FindByCapability(channel)
            .Where(tool =>
                !IsUmbrellaAgentTool(tool.Name))
            .Where(tool =>
                !tool.RiskLevel.Equals(
                    ToolRiskLevels.High,
                    StringComparison.OrdinalIgnoreCase))
            .OrderBy(tool =>
                tool.RiskLevel.Equals(
                    ToolRiskLevels.Low,
                    StringComparison.OrdinalIgnoreCase)
                    ? 0
                    : 1)
            .ThenBy(tool =>
                tool.RequiresConfirmation)
            .ThenByDescending(tool =>
                tool.SupportsVerification)
            .ThenBy(tool =>
                tool.Name,
                StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool IsUmbrellaAgentTool(
        string name) =>
        name.Equals(
            "computer.operator.run-task",
            StringComparison.OrdinalIgnoreCase);

    private static double RiskPenalty(
        string risk) =>
        risk switch
        {
            UniversalRouteRisk.Medium => MediumRiskPenalty,
            UniversalRouteRisk.High => HighRiskPenalty,
            _ => 0
        };

    private static void AddTerms(
        string goal,
        IReadOnlyList<string> terms,
        double weight,
        ICollection<string> reasons,
        ref double confidence)
    {
        var hits = terms
            .Where(term =>
                goal.Contains(
                    term,
                    StringComparison.OrdinalIgnoreCase))
            .Take(4)
            .ToArray();

        if (hits.Length == 0)
            return;

        confidence += weight *
            hits.Length;

        foreach (var hit in hits)
            reasons.Add($"term:{hit.Trim()}");
    }

    private static string Normalize(
        string? value) =>
        (value ?? string.Empty)
            .Trim()
            .ToLowerInvariant();
}
