using System.Text.Json;
using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public sealed record ToolArgumentPlanRequest(
    string ToolName,
    string Goal);

public sealed record ToolArgumentPlanResult(
    bool Proposed,
    string ToolName,
    JsonElement? Arguments,
    bool RequiresConfirmation,
    IReadOnlyList<string> RequiredPermissions,
    string? Provider,
    string? Model,
    string Reason);

public interface IToolArgumentPlanner
{
    Task<ToolArgumentPlanResult> PlanAsync(
        ToolArgumentPlanRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class ToolArgumentPlanner(
    IToolRegistry registry,
    IToolInputValidator validator,
    IAiProviderResolver providers)
    : IToolArgumentPlanner
{
    private const string ProviderFunctionName =
        "pai_selected_tool";

    public async Task<ToolArgumentPlanResult> PlanAsync(
        ToolArgumentPlanRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var toolName =
            (request.ToolName ?? string.Empty).Trim();
        var goal =
            (request.Goal ?? string.Empty).Trim();

        if (toolName.Length == 0)
        {
            throw new ToolProposalValidationException(
                "Tool Argument Planner cần ToolName.");
        }

        if (goal.Length < 2)
        {
            throw new ToolProposalValidationException(
                "Tool Argument Planner cần goal rõ ràng.");
        }

        if (!registry.TryGet(
                toolName,
                out var tool) ||
            tool is null)
        {
            throw new ToolProposalValidationException(
                "Tool được chọn không tồn tại trong registry.");
        }

        var provider = providers.GetActive();

        if (!provider.IsConfigured)
        {
            throw new InvalidOperationException(
                "Nhà cung cấp AI hiện tại chưa được cấu hình.");
        }

        var definition =
            new ProviderFunctionDefinition(
                ProviderFunctionName,
                BuildDescription(
                    tool.Definition),
                tool.Definition.InputSchema);

        var decision =
            await provider.ProposeFunctionCallAsync(
                [
                    new ChatMessage(
                        "user",
                        goal)
                ],
                [definition],
                cancellationToken);

        if (decision is null)
        {
            return new(
                Proposed: false,
                tool.Definition.Name,
                Arguments: null,
                RequiresConfirmation:
                    RequiresConfirmation(
                        tool.Definition),
                tool.Definition.RequiredPermissions,
                provider.Name,
                provider.Model,
                "AI không đề xuất gọi tool đã chọn.");
        }

        if (!decision.Name.Equals(
                ProviderFunctionName,
                StringComparison.Ordinal))
        {
            return new(
                Proposed: false,
                tool.Definition.Name,
                Arguments: null,
                RequiresConfirmation:
                    RequiresConfirmation(
                        tool.Definition),
                tool.Definition.RequiredPermissions,
                provider.Name,
                provider.Model,
                "AI trả về function name không khớp tool đã khóa.");
        }

        if (decision.Arguments.ValueKind !=
            JsonValueKind.Object)
        {
            return new(
                Proposed: false,
                tool.Definition.Name,
                Arguments: null,
                RequiresConfirmation:
                    RequiresConfirmation(
                        tool.Definition),
                tool.Definition.RequiredPermissions,
                provider.Name,
                provider.Model,
                "AI trả về arguments không phải object JSON.");
        }

        var validation =
            validator.Validate(
                tool.Definition.InputSchema,
                decision.Arguments);

        if (!validation.IsValid)
        {
            return new(
                Proposed: false,
                tool.Definition.Name,
                Arguments: null,
                RequiresConfirmation:
                    RequiresConfirmation(
                        tool.Definition),
                tool.Definition.RequiredPermissions,
                provider.Name,
                provider.Model,
                "Arguments bị schema validator từ chối: " +
                string.Join(
                    " ",
                    validation.Errors));
        }

        return new(
            Proposed: true,
            tool.Definition.Name,
            decision.Arguments.Clone(),
            RequiresConfirmation(
                tool.Definition),
            tool.Definition.RequiredPermissions,
            provider.Name,
            provider.Model,
            "AI chỉ lập arguments cho tool đã được router chọn; chưa thực thi.");
    }

    private static bool RequiresConfirmation(
        ToolDefinition definition) =>
        definition.RequiresConfirmation ||
        definition.RequiredPermissions.Any(
            ToolPermissions.RequiresExplicitConfirmation);

    private static string BuildDescription(
        ToolDefinition definition) =>
        $"""
Bạn chỉ được lập tham số cho đúng tool này.
Tool: {definition.Name}
Mô tả: {definition.Description}
Đây chỉ là đề xuất arguments, không được giả định tool đã chạy.
Không thêm field ngoài schema.
""";
}
