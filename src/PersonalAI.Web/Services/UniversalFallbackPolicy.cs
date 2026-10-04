using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public static class UniversalFallbackActions
{
    public const string Complete = "complete";
    public const string ReplanArguments = "replan-arguments";
    public const string Reroute = "reroute";
    public const string AgentFallback = "agent-fallback";
    public const string Stop = "stop";
}

public sealed record UniversalFallbackDecision(
    string Action,
    bool AllowAgentFallback,
    string? FallbackChannel,
    string Reason);

public interface IUniversalFallbackPolicy
{
    UniversalFallbackDecision Evaluate(
        UniversalTaskRoutePreview route,
        ToolExecutionResponse execution);
}

public sealed class UniversalFallbackPolicy
    : IUniversalFallbackPolicy
{
    public UniversalFallbackDecision Evaluate(
        UniversalTaskRoutePreview route,
        ToolExecutionResponse execution)
    {
        ArgumentNullException.ThrowIfNull(route);
        ArgumentNullException.ThrowIfNull(execution);

        var fallbackChannel =
            string.IsNullOrWhiteSpace(
                route.SelectedChannel)
                ? null
                : route.SelectedChannel;

        return execution.Status switch
        {
            ToolExecutionStatuses.Succeeded =>
                new(
                    UniversalFallbackActions.Complete,
                    AllowAgentFallback: false,
                    FallbackChannel: null,
                    "Direct tool đã chạy thành công; không cần fallback."),

            ToolExecutionStatuses.InvalidInput =>
                new(
                    UniversalFallbackActions.ReplanArguments,
                    AllowAgentFallback: false,
                    FallbackChannel: null,
                    "Arguments không hợp lệ; phải lập lại arguments thay vì đổi sang agent khác."),

            ToolExecutionStatuses.NotFound =>
                new(
                    UniversalFallbackActions.Reroute,
                    AllowAgentFallback: false,
                    FallbackChannel: null,
                    "Tool không còn trong registry; cần route lại từ catalog hiện tại."),

            ToolExecutionStatuses.Denied =>
                new(
                    UniversalFallbackActions.Stop,
                    AllowAgentFallback: false,
                    FallbackChannel: null,
                    "Tool bị policy/permission/confirmation từ chối; cấm dùng execution agent để lách gate."),

            ToolExecutionStatuses.TimedOut =>
                BuildTechnicalFallback(
                    fallbackChannel,
                    "Direct tool timeout."),

            ToolExecutionStatuses.Failed =>
                BuildTechnicalFallback(
                    fallbackChannel,
                    "Direct tool thất bại kỹ thuật."),

            _ =>
                new(
                    UniversalFallbackActions.Stop,
                    AllowAgentFallback: false,
                    FallbackChannel: null,
                    $"Trạng thái tool không hỗ trợ fallback: {execution.Status}.")
        };
    }

    private static UniversalFallbackDecision BuildTechnicalFallback(
        string? fallbackChannel,
        string reason)
    {
        if (string.IsNullOrWhiteSpace(
                fallbackChannel))
        {
            return new(
                UniversalFallbackActions.Reroute,
                AllowAgentFallback: false,
                FallbackChannel: null,
                reason +
                " Không còn selected channel; cần route lại.");
        }

        return new(
            UniversalFallbackActions.AgentFallback,
            AllowAgentFallback: true,
            fallbackChannel,
            reason +
            $" Có thể đề xuất execution agent channel={fallbackChannel}, nhưng không tự execute.");
    }
}
