using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace PersonalAI.Web.Services;

public sealed record PersonalAiMcpCapabilityRequest(
    string Channel,
    IReadOnlyList<string>? RequiredCapabilities = null,
    bool AllowHighRisk = false,
    bool RequireVerification = true);

public sealed record PersonalAiMcpCapabilityResponse(
    string Channel,
    IReadOnlyList<string> SelectedTools,
    int ConsideredTools,
    int SelectedToolCount,
    string Reason);

public sealed record PersonalAiMcpExecutionPreviewResponse(
    string Goal,
    string? Channel,
    string? AgentId,
    string? SelectedAgentId,
    bool ConfirmationRequired,
    IReadOnlyList<ExecutionGatewayCandidate> Candidates,
    string Reason);

public interface IPersonalAiMcpCapabilityBridge
{
    PersonalAiMcpCapabilityResponse Discover(
        PersonalAiMcpCapabilityRequest request);

    PersonalAiMcpExecutionPreviewResponse Preview(
        ExecutionGatewayRequest request);

    Task<ExecutionGatewayResult> ExecuteAsync(
        ExecutionGatewayRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// MCP boundary adapter.
/// Chỉ gọi registry/router/gateway hiện có; không gọi Win32, UIA, Playwright hay tool executor trực tiếp.
/// </summary>
public sealed class PersonalAiMcpCapabilityBridge(
    ICapabilityToolRouter capabilityRouter,
    IExecutionGateway executionGateway)
    : IPersonalAiMcpCapabilityBridge
{
    public PersonalAiMcpCapabilityResponse Discover(
        PersonalAiMcpCapabilityRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var routed =
            capabilityRouter.Route(
                new CapabilityToolRouteRequest(
                    request.Channel,
                    request.RequiredCapabilities ??
                        Array.Empty<string>(),
                    request.AllowHighRisk,
                    request.RequireVerification));

        return new(
            routed.Channel,
            routed.Tools
                .Select(tool => tool.Name)
                .ToArray(),
            routed.ConsideredTools,
            routed.SelectedTools,
            routed.Reason);
    }

    public PersonalAiMcpExecutionPreviewResponse Preview(
        ExecutionGatewayRequest request)
    {
        var preview =
            executionGateway.Preview(request);

        return new(
            preview.Goal,
            preview.Channel,
            preview.AgentId,
            preview.SelectedAgentId,
            preview.ConfirmationRequired,
            preview.Candidates,
            preview.Reason);
    }

    public Task<ExecutionGatewayResult> ExecuteAsync(
        ExecutionGatewayRequest request,
        CancellationToken cancellationToken = default) =>
        executionGateway.ExecuteAsync(
            request,
            cancellationToken);
}

[McpServerToolType]
public sealed class PersonalAiMcpTools
{
    [McpServerTool]
    [Description("Liệt kê các tool Personal AI phù hợp với một execution channel thông qua CapabilityToolRouter hiện có. Không thực thi hành động.")]
    public static string DiscoverCapabilities(
        [Description("Execution channel: computer, browser, coding hoặc connector.")]
        string channel,
        [Description("Danh sách capability bắt buộc, phân tách bằng dấu phẩy. Có thể để trống.")]
        string? requiredCapabilities,
        [Description("Cho phép tool high-risk xuất hiện trong kết quả. Mặc định false.")]
        bool allowHighRisk,
        [Description("Chỉ lấy tool hỗ trợ verification. Mặc định true.")]
        bool requireVerification,
        IPersonalAiMcpCapabilityBridge bridge)
    {
        var required =
            (requiredCapabilities ?? string.Empty)
                .Split(
                    ',',
                    StringSplitOptions.RemoveEmptyEntries |
                    StringSplitOptions.TrimEntries);

        var response =
            bridge.Discover(
                new(
                    channel,
                    required,
                    allowHighRisk,
                    requireVerification));

        return JsonSerializer.Serialize(
            response);
    }

    [McpServerTool]
    [Description("Xem trước route execution của Personal AI. Không thực thi và không bypass confirmation.")]
    public static string PreviewExecution(
        [Description("Mục tiêu cần thực hiện.")]
        string goal,
        [Description("Execution channel rõ ràng: computer, browser, coding hoặc connector.")]
        string channel,
        [Description("Agent ID tùy chọn. Để trống để ExecutionGateway chọn.")]
        string? agentId,
        IPersonalAiMcpCapabilityBridge bridge)
    {
        var response =
            bridge.Preview(
                new ExecutionGatewayRequest(
                    goal,
                    channel,
                    agentId,
                    ConfirmExecution: false));

        return JsonSerializer.Serialize(
            response);
    }

    [McpServerTool]
    [Description("Thực thi một goal thông qua ExecutionGateway của Personal AI. Side-effect agent vẫn bắt buộc confirmExecution=true; MCP không được bypass safety/approval.")]
    public static async Task<string> ExecuteGoalAsync(
        [Description("Mục tiêu cần thực hiện.")]
        string goal,
        [Description("Execution channel rõ ràng: computer, browser, coding hoặc connector.")]
        string channel,
        [Description("Agent ID tùy chọn. Để trống để ExecutionGateway chọn.")]
        string? agentId,
        [Description("Phải true nếu ExecutionGateway yêu cầu xác nhận side effect.")]
        bool confirmExecution,
        IPersonalAiMcpCapabilityBridge bridge,
        CancellationToken cancellationToken)
    {
        var result =
            await bridge.ExecuteAsync(
                new ExecutionGatewayRequest(
                    goal,
                    channel,
                    agentId,
                    confirmExecution),
                cancellationToken);

        return JsonSerializer.Serialize(
            result);
    }
}
