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

        if (allowToolProposal
            && request.UseTools
            && knowledgeMode == "normal")
        {
            var proposal = await toolOrchestration.ProposeAsync(
                request.Messages,
                cancellationToken);
            if (proposal is not null)
            {
                audit.Record(
                    auditAgent,
                    "tool.proposed",
                    $"tool-proposal:{proposal.ProposalId:D}",
                    auditReason,
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
            request.Messages,
            request.UseKnowledge,
            knowledgeMode,
            request.UseMemory,
            request.UseTaskContext,
            cancellationToken);

        if (knowledgeMode == "documents-only"
            && managedContext.Sources.Count == 0)
        {
            audit.Record(
                auditAgent,
                "chat.completed",
                "chat:turn",
                $"{auditReason}-documents-only-no-source",
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
}

public sealed class ChatValidationException(string message)
    : Exception(message);
