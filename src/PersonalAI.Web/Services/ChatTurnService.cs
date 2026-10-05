using System.Net;
using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public static class ChatRequestRules
{
    private static readonly HashSet<string> AllowedRoles =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "user",
            "assistant"
        };

    public static string? Validate(ChatRequest? request)
    {
        if (request?.Messages is null || request.Messages.Count == 0)
        {
            return "Hãy nhập một câu hỏi.";
        }

        if (request.Messages.Count > 40)
        {
            return "Cuộc trò chuyện quá dài. Hãy tạo cuộc trò chuyện mới.";
        }

        if (request.Messages.Any(message =>
            !AllowedRoles.Contains(message.Role)
            || string.IsNullOrWhiteSpace(message.Content)
            || message.Content.Length > 12_000))
        {
            return "Nội dung hội thoại không hợp lệ.";
        }

        if (request.Messages.Any(message =>
            (message.Attachments?.Count ?? 0) >
                ChatAttachmentStore.MaximumAttachmentsPerMessage))
        {
            return $"Mỗi tin nhắn chỉ được đính kèm tối đa {ChatAttachmentStore.MaximumAttachmentsPerMessage} tệp.";
        }

        if (request.Messages.Any(message =>
            message.Role.Equals(
                "assistant",
                StringComparison.OrdinalIgnoreCase) &&
            (message.Attachments?.Count ?? 0) > 0))
        {
            return "Chỉ tin nhắn người dùng mới được chứa tệp đính kèm.";
        }

        var knowledgeMode = NormalizeKnowledgeMode(request.KnowledgeMode);
        if (knowledgeMode is not ("normal" or "documents-only"))
        {
            return "Chế độ trả lời theo dữ liệu không hợp lệ.";
        }

        if (!request.UseKnowledge
            && knowledgeMode == "documents-only")
        {
            return "Hãy bật dữ liệu riêng để sử dụng chế độ chỉ trả lời theo tài liệu.";
        }

        return null;
    }

    public static string NormalizeKnowledgeMode(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? "normal"
            : value.Trim().ToLowerInvariant();
}

public interface IChatTurnService
{
    Task<ChatResponse> ExecuteAsync(
        ChatRequest request,
        bool allowToolProposal,
        string auditAgent,
        string auditReason,
        CancellationToken cancellationToken = default);
}

public sealed class ChatTurnService(
    IAiProviderResolver providerResolver,
    IContextManagerService contextManager,
    IToolOrchestrationService toolOrchestration,
    IToolExecutionService toolExecution,
    IChatAttachmentStore attachmentStore,
    IAuditRecorder audit) : IChatTurnService
{
    public async Task<ChatResponse> ExecuteAsync(
        ChatRequest request,
        bool allowToolProposal,
        string auditAgent,
        string auditReason,
        CancellationToken cancellationToken = default)
    {
        var validationError = ChatRequestRules.Validate(request);
        if (validationError is not null)
        {
            throw new ChatValidationException(validationError);
        }

        var aiProvider = providerResolver.GetActive();
        var knowledgeMode =
            ChatRequestRules.NormalizeKnowledgeMode(
                request.KnowledgeMode);

        var canonicalMessages =
            CanonicalizeAttachments(
                request.Messages,
                attachmentStore);

        var hasAttachments =
            canonicalMessages.Any(message =>
                (message.Attachments?.Count ?? 0) > 0);

        if (allowToolProposal
            && request.UseTools
            && !hasAttachments
            && knowledgeMode == "normal"
            && IsDirectLeaguePracticeCommand(
                canonicalMessages.Last().Content))
        {
            using var argumentsDocument =
                System.Text.Json.JsonDocument.Parse("{}");
            var execution = await toolExecution.ExecuteAsync(
                new ToolExecutionRequest(
                    "league.practice.open",
                    argumentsDocument.RootElement.Clone(),
                    [
                        ToolPermissions.Write,
                        ToolPermissions.External,
                        ToolPermissions.Computer
                    ],
                    Confirmed: true),
                cancellationToken);

            audit.Record(
                auditAgent,
                "tool.direct-execution",
                "tool:league.practice.open",
                "explicit-chat-command",
                execution.Success
                    ? AuditResults.Succeeded
                    : AuditResults.Failed,
                execution.Error ?? execution.Status);

            var message = BuildLeaguePracticeChatMessage(execution);
            return new ChatResponse(
                message,
                aiProvider.Model,
                aiProvider.Name,
                [],
                null,
                null);
        }

        if (allowToolProposal
            && request.UseTools
            && !hasAttachments
            && knowledgeMode == "normal")
        {
            var proposal = await toolOrchestration.ProposeAsync(
                canonicalMessages,
                cancellationToken);
            if (proposal is not null)
            {
                audit.Record(
                    auditAgent,
                    "tool.proposed",
                    $"tool-proposal:{proposal.ProposalId:D}",
                    "chat-tool-selection",
                    AuditResults.Proposed,
                    proposal.ToolName);

                return new ChatResponse(
                    proposal.AssistantMessage,
                    aiProvider.Model,
                    aiProvider.Name,
                    [],
                    proposal);
            }
        }

        var managedContext = await contextManager.BuildAsync(
            canonicalMessages,
            request.UseKnowledge,
            knowledgeMode,
            request.UseMemory,
            request.UseTaskContext,
            request.UseLifeContext,
            cancellationToken);

        if (knowledgeMode == "documents-only"
            && managedContext.Sources.Count == 0)
        {
            audit.Record(
                auditAgent,
                "chat.completed",
                "chat:turn",
                auditReason.Equals(
                    "context-managed-chat",
                    StringComparison.Ordinal)
                    ? "documents-only-no-source"
                    : $"{auditReason}-documents-only-no-source",
                AuditResults.Succeeded);

            return new ChatResponse(
                "Tôi chưa tìm thấy thông tin này trong kho dữ liệu.",
                aiProvider.Model,
                aiProvider.Name,
                [],
                null,
                managedContext.Report);
        }

        var answer = await aiProvider.ReplyAsync(
            managedContext.Messages,
            cancellationToken);

        audit.Record(
            auditAgent,
            "chat.completed",
            "chat:turn",
            auditReason,
            AuditResults.Succeeded);

        return new ChatResponse(
            answer,
            aiProvider.Model,
            aiProvider.Name,
            managedContext.Sources,
            null,
            managedContext.Report);
    }
    private static IReadOnlyList<ChatMessage> CanonicalizeAttachments(
        IReadOnlyList<ChatMessage> messages,
        IChatAttachmentStore attachmentStore)
    {
        return messages
            .Select(message =>
            {
                if (message.Attachments is not
                    {
                        Count: > 0
                    })
                {
                    return message;
                }

                var attachments =
                    message.Attachments
                        .Select(reference =>
                        {
                            var stored =
                                attachmentStore.GetRequired(
                                    reference.Id);

                            return new ChatAttachmentReference(
                                stored.Id,
                                stored.FileName,
                                stored.MimeType,
                                stored.Size,
                                stored.Kind,
                                stored.Route);
                        })
                        .ToArray();

                return message with
                {
                    Attachments =
                        attachments
                };
            })
            .ToArray();
    }

    private static bool IsDirectLeaguePracticeCommand(
        string value)
    {
        var normalized = string.Join(
            " ",
            (value ?? string.Empty)
                .Trim()
                .ToLowerInvariant()
                .Split(
                    [' ', '\t', '\r', '\n'],
                    StringSplitOptions.RemoveEmptyEntries));

        if (normalized.Length is < 8 or > 180)
            return false;

        var asksLeague =
            normalized.Contains("mở liên minh", StringComparison.Ordinal) ||
            normalized.Contains("mo lien minh", StringComparison.Ordinal) ||
            normalized.Contains("mở league", StringComparison.Ordinal) ||
            normalized.Contains("open league", StringComparison.Ordinal);

        var asksPractice =
            normalized.Contains("phòng tập", StringComparison.Ordinal) ||
            normalized.Contains("phong tap", StringComparison.Ordinal) ||
            normalized.Contains("practice tool", StringComparison.Ordinal);

        return asksLeague && asksPractice;
    }

    private static string BuildLeaguePracticeChatMessage(
        ToolExecutionResponse execution)
    {
        if (execution.Success &&
            execution.Output is System.Text.Json.JsonElement output &&
            output.ValueKind == System.Text.Json.JsonValueKind.Object)
        {
            var status = output.TryGetProperty("status", out var statusValue)
                ? statusValue.GetString()
                : null;
            var phase = output.TryGetProperty("phase", out var phaseValue)
                ? phaseValue.GetString()
                : null;
            var detail = output.TryGetProperty("detail", out var detailValue)
                ? detailValue.GetString()
                : null;

            return status == "completed"
                ? "Đã mở Liên Minh và hoàn tất chuỗi thao tác vào Practice Tool."
                : $"Đã chạy tự động hóa Liên Minh nhưng dừng ở bước {phase ?? "không xác định"}: {detail ?? "không có chi tiết."}";
        }

        return $"Không chạy được lệnh mở Practice Tool: {execution.Error ?? execution.Status}.";
    }

}

public sealed class ChatValidationException(string message)
    : Exception(message);
