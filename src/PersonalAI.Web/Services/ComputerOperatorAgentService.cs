using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public interface IComputerOperatorAgentService
{
    Task<string?> RunAsync(
        IReadOnlyList<ChatMessage> messages,
        CancellationToken cancellationToken = default);
}

public sealed class ComputerOperatorAgentService(
    IAiProviderResolver providerResolver,
    IToolRegistry registry,
    IToolExecutionService executor,
    IToolResultSynthesisService synthesizer,
    ComputerControlGate control,
    IAuditRecorder audit,
    ILogger<ComputerOperatorAgentService> logger)
    : IComputerOperatorAgentService
{
    private const int MaximumSteps = 8;
    private const int MaximumHistoryCharacters = 28_000;
    private const int MaximumToolResultCharacters = 8_000;

    public async Task<string?> RunAsync(
        IReadOnlyList<ChatMessage> messages,
        CancellationToken cancellationToken = default)
    {
        var session = control.GetStatus();
        if (session.Paused || !session.DelegatedOperator)
            throw new ToolExecutionInputException(
                "Phiên Computer Operator chưa được ủy quyền.");

        if (!session.AllowExternalAiContext)
            throw new ToolExecutionInputException(
                "Phiên Computer Operator chưa cho phép dùng ngữ cảnh desktop với nhà cung cấp AI.");

        var originalRequest = messages
            .LastOrDefault(message =>
                message.Role.Equals("user", StringComparison.OrdinalIgnoreCase))
            ?.Content?.Trim();

        if (string.IsNullOrWhiteSpace(originalRequest))
            throw new ToolExecutionInputException(
                "Không tìm thấy yêu cầu người dùng cho Computer Operator.");

        var definitions = registry.GetAll()
            .Where(definition =>
                definition.Name.StartsWith(
                    "computer.",
                    StringComparison.OrdinalIgnoreCase)
                && control.CanAutoApproveComputerTool(definition))
            .ToArray();

        if (definitions.Length == 0)
            throw new ToolExecutionInputException(
                "Không có công cụ Computer Operator nào khả dụng trong phiên ủy quyền.");

        var mapped = definitions
            .Select(definition => new
            {
                Definition = definition,
                ProviderName = CreateProviderFunctionName(definition.Name)
            })
            .ToArray();

        var byProviderName = mapped.ToDictionary(
            item => item.ProviderName,
            item => item.Definition,
            StringComparer.Ordinal);

        var functions = mapped
            .Select(item => new ProviderFunctionDefinition(
                item.ProviderName,
                BuildDescription(item.Definition),
                item.Definition.InputSchema))
            .ToArray();

        var provider = providerResolver.GetActive();
        var history = LimitConversation(messages).ToList();
        var completed = new List<string>();

        history.Add(new ChatMessage(
            "user",
            $"COMPUTER_OPERATOR_SESSION: Người dùng đã ủy quyền một phiên Computer Operator có thời hạn. " +
            $"Hãy hoàn thành yêu cầu bằng các bước nhỏ, quan sát kết quả sau mỗi bước và chỉ dùng các hàm computer.* được cung cấp. " +
            $"Yêu cầu gốc: {originalRequest}"));

        for (var step = 1; step <= MaximumSteps; step++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var current = control.GetStatus();
            if (current.Paused || !current.DelegatedOperator)
                return completed.Count == 0
                    ? "Computer Operator đã dừng trước khi thực hiện thao tác."
                    : $"Computer Operator đã dừng. Đã hoàn tất {completed.Count} bước: {string.Join(" → ", completed)}.";

            var decision = await provider.ProposeFunctionCallAsync(
                LimitConversation(history),
                functions,
                cancellationToken);

            if (decision is null)
            {
                return completed.Count == 0
                    ? null
                    : $"Đã hoàn tất chuỗi Computer Operator gồm {completed.Count} bước: {string.Join(" → ", completed)}.";
            }

            if (!byProviderName.TryGetValue(
                    decision.Name,
                    out var definition))
                throw new InvalidOperationException(
                    "Nhà cung cấp AI trả về công cụ ngoài danh sách Computer Operator.");

            var execution = await executor.ExecuteAsync(
                new ToolExecutionRequest(
                    definition.Name,
                    decision.Arguments,
                    definition.RequiredPermissions,
                    Confirmed: true),
                cancellationToken);

            audit.Record(
                AuditAgents.PersonalAi,
                "computer.operator.step",
                $"tool-invocation:{execution.InvocationId:D}",
                "delegated-computer-operator",
                execution.Success
                    ? AuditResults.Succeeded
                    : AuditResults.Failed,
                definition.Name,
                source: "computer-operator",
                correlationId: execution.InvocationId.ToString("D"));

            var proposal = new ToolCallProposal(
                Guid.NewGuid(),
                definition.Name,
                decision.Arguments.Clone(),
                "Computer Operator delegated step.",
                string.Empty,
                definition.RequiredPermissions.ToArray(),
                false,
                DateTimeOffset.UtcNow.AddMinutes(1),
                "delegated-computer-operator",
                provider.Name,
                provider.Model);

            var summary = synthesizer.CreateLocalSummary(
                proposal,
                execution);

            if (!execution.Success)
            {
                logger.LogWarning(
                    "Delegated Computer Operator step {Step} failed for {Tool}: {Error}",
                    step,
                    definition.Name,
                    execution.Error);

                return completed.Count == 0
                    ? $"Computer Operator dừng ở bước {definition.Name}: {execution.Error ?? execution.Status}."
                    : $"Computer Operator dừng sau {completed.Count} bước. Bước {definition.Name} lỗi: {execution.Error ?? execution.Status}.";
            }

            completed.Add(summary);

            var rawOutput = execution.Output?.GetRawText() ?? "{}";
            if (rawOutput.Length > MaximumToolResultCharacters)
                rawOutput = rawOutput[..MaximumToolResultCharacters] + "…";

            history.Add(new ChatMessage(
                "assistant",
                $"Đã thực hiện bước {step}: {definition.Name}. {summary}"));

            history.Add(new ChatMessage(
                "user",
                $"Yêu cầu gốc vẫn là: {originalRequest}\n" +
                $"Kết quả bước vừa chạy ({definition.Name}): {rawOutput}\n" +
                "Hãy kiểm tra kết quả này. Nếu mục tiêu chưa đạt, chọn đúng MỘT công cụ computer.* tiếp theo. " +
                "Nếu mục tiêu đã đạt thì không gọi công cụ nữa."));
        }

        return $"Computer Operator đạt giới hạn {MaximumSteps} bước và đã dừng để tránh vòng lặp. Đã thực hiện: {string.Join(" → ", completed)}.";
    }

    private static IReadOnlyList<ChatMessage> LimitConversation(
        IReadOnlyList<ChatMessage> messages)
    {
        var selected = new List<ChatMessage>();
        var characters = 0;

        foreach (var message in messages.Reverse().Take(16))
        {
            var content = message.Content ?? string.Empty;
            if (content.Length > 8_000)
                content = content[..8_000];

            var remaining = MaximumHistoryCharacters - characters;
            if (remaining <= 0)
                break;

            if (content.Length > remaining)
                content = content[..remaining];

            selected.Add(new ChatMessage(message.Role, content));
            characters += content.Length;
        }

        selected.Reverse();
        return selected;
    }

    private static string BuildDescription(
        ToolDefinition definition)
    {
        var permissions = string.Join(", ", definition.RequiredPermissions);
        return $"{definition.Description} Công cụ nội bộ: {definition.Name}. " +
               $"Permissions: {permissions}. Phiên Computer Operator hiện đã được người dùng ủy quyền có thời hạn; " +
               "hãy chỉ gọi khi cần để hoàn thành yêu cầu gốc và luôn kiểm tra kết quả trước bước tiếp theo.";
    }

    private static string CreateProviderFunctionName(
        string internalName)
    {
        var normalized = new StringBuilder();
        foreach (var character in internalName)
            normalized.Append(
                char.IsAsciiLetterOrDigit(character)
                    ? character
                    : '_');

        var readable = normalized.ToString().Trim('_');
        if (readable.Length == 0)
            readable = "tool";
        if (readable.Length > 42)
            readable = readable[..42];

        var hash = Convert.ToHexString(
                SHA256.HashData(
                    Encoding.UTF8.GetBytes(internalName)))
            .ToLowerInvariant()[..10];

        return $"pai_{readable}_{hash}";
    }
}
