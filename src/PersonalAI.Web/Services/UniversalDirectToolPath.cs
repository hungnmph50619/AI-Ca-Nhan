using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public static class UniversalExecutionModes
{
    public const string DirectToolProposal =
        "direct-tool-proposal";

    public const string ExecutionAgentFallback =
        "execution-agent-fallback";

    public const string NeedsFurtherRouting =
        "needs-further-routing";
}

public sealed record UniversalExecutionPrepareRequest(
    string Goal,
    string? PreferredChannel = null,
    string? ReplanContext = null);

public sealed record UniversalExecutionPrepareResult(
    string Mode,
    UniversalTaskRoutePreview Route,
    ToolArgumentPlanResult? ToolPlan,
    ToolCallProposal? ToolProposal,
    string? FallbackChannel,
    string Reason);

public interface IUniversalDirectToolPath
{
    Task<UniversalExecutionPrepareResult> PrepareAsync(
        UniversalExecutionPrepareRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class UniversalDirectToolPath(
    IUniversalTaskRouter router,
    IToolArgumentPlanner argumentPlanner,
    IToolOrchestrationService orchestration)
    : IUniversalDirectToolPath
{
    public async Task<UniversalExecutionPrepareResult> PrepareAsync(
        UniversalExecutionPrepareRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var route = router.Preview(
            new UniversalTaskRouteRequest(
                request.Goal,
                request.PreferredChannel,
                ConfirmExecution: false));

        if (route.SelectedChannel is null)
        {
            return new(
                UniversalExecutionModes.NeedsFurtherRouting,
                route,
                ToolPlan: null,
                ToolProposal: null,
                FallbackChannel: null,
                route.Reason);
        }

        var selectedCandidate =
            route.Candidates.FirstOrDefault(candidate =>
                candidate.Channel.Equals(
                    route.SelectedChannel,
                    StringComparison.OrdinalIgnoreCase));

        if (selectedCandidate is null)
        {
            return new(
                UniversalExecutionModes.NeedsFurtherRouting,
                route,
                ToolPlan: null,
                ToolProposal: null,
                FallbackChannel: null,
                "Router chọn channel nhưng không còn candidate metadata.");
        }

        if (string.IsNullOrWhiteSpace(
                selectedCandidate.PreferredToolName))
        {
            return new(
                UniversalExecutionModes.ExecutionAgentFallback,
                route,
                ToolPlan: null,
                ToolProposal: null,
                FallbackChannel: route.SelectedChannel,
                "Không có direct tool an toàn; giữ execution agent làm fallback.");
        }

        var plan =
            await argumentPlanner.PlanAsync(
                new ToolArgumentPlanRequest(
                    selectedCandidate.PreferredToolName,
                    BuildPlannerGoal(
                        route.Goal,
                        request.ReplanContext)),
                cancellationToken);

        if (!plan.Proposed ||
            plan.Arguments is null)
        {
            return new(
                UniversalExecutionModes.ExecutionAgentFallback,
                route,
                plan,
                ToolProposal: null,
                FallbackChannel: route.SelectedChannel,
                "Direct tool được ưu tiên nhưng AI không tạo được arguments hợp lệ; chưa thực thi và giữ agent fallback.");
        }

        var proposal = orchestration.Prepare(
            new ToolProposalDraft(
                plan.ToolName,
                plan.Arguments.Value,
                Reason:
                    $"Universal Router ưu tiên direct tool {plan.ToolName} trước execution agent {route.SelectedChannel}.",
                AssistantMessage:
                    plan.RequiresConfirmation
                        ? $"Đã chuẩn bị {plan.ToolName}. Hãy kiểm tra arguments và xác nhận trước khi chạy."
                        : $"Đã chuẩn bị {plan.ToolName}. Hãy kiểm tra arguments trước khi chạy."));

        return new(
            UniversalExecutionModes.DirectToolProposal,
            route,
            plan,
            proposal,
            FallbackChannel: route.SelectedChannel,
            "Đã tạo proposal hai pha; tool chưa được thực thi.");
    }

    private static string BuildPlannerGoal(
        string goal,
        string? replanContext)
    {
        var context =
            (replanContext ?? string.Empty)
                .Trim();

        if (context.Length == 0)
            return goal;

        if (context.Length > 1_200)
            context = context[..1_200];

        return $"""
{goal}

Ngữ cảnh từ lần thử trước:
{context}

Hãy lập arguments mới dựa trên lỗi/kết quả trước đó. Không lặp nguyên phương án cũ nếu context cho thấy nó không đạt mục tiêu.
""";
    }
}
