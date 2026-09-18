using System.Text;
using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public interface IDecisionEngineService
{
    DecisionEngineStatusResponse GetStatus();

    Task<DecisionPreviewResponse> PreviewAsync(
        DecisionAnalyzeRequest request,
        CancellationToken cancellationToken = default);

    Task<DecisionAnalysisResponse> AnalyzeAsync(
        DecisionAnalyzeRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class DecisionValidationException(string message)
    : Exception(message);

public sealed class DecisionEngineService(
    IContextManagerService contextManager,
    IAiProviderResolver providerResolver,
    IWorkspaceContextAccessor workspaceContext,
    IAuditRecorder audit) : IDecisionEngineService
{
    public const int MaximumQuestionCharacters = 4_000;
    public const int MaximumOptions = 8;
    public const int MinimumOptions = 2;
    public const int MaximumCriteria = 8;
    public const int MaximumOptionCharacters = 240;
    public const int MaximumCriterionCharacters = 180;
    public const int MaximumConstraintsCharacters = 2_000;

    public DecisionEngineStatusResponse GetStatus() =>
        new(
            PersonalAiRelease.Version,
            Supported: true,
            RequiresUserDecision: true,
            AutoActionEnabled: false,
            ToolExecutionEnabled: false,
            PersistsAnalyses: false,
            ContextGroundingEnabled: true,
            MaximumQuestionCharacters,
            MaximumOptions,
            MaximumCriteria,
            MaximumOptionCharacters,
            MaximumCriterionCharacters,
            MaximumConstraintsCharacters,
            [
                ContextSelectionKinds.Memory,
                ContextSelectionKinds.Document,
                ContextSelectionKinds.Task,
                ContextSelectionKinds.LifeContext
            ],
            [
                "Decision Engine chỉ phân tích và đề xuất; không thực thi recommendation.",
                "Không đăng ký Decision Engine như một tool và không gọi Tool Orchestration.",
                "Phân tích không được lưu mặc định; Audit chỉ lưu metadata decision ID, không lưu câu hỏi hoặc nội dung phân tích.",
                "Private context chỉ được dùng theo các cờ Memory/Documents/Tasks/Life Context của request và consent hiện có.",
                "Kết quả AI có thể sai hoặc thiếu; response luôn yêu cầu người dùng tự đưa ra quyết định cuối cùng."
            ]);

    public async Task<DecisionPreviewResponse> PreviewAsync(
        DecisionAnalyzeRequest request,
        CancellationToken cancellationToken = default)
    {
        var normalized = Normalize(request);
        var decisionId = Guid.NewGuid();

        var managed = await BuildContextAsync(
            normalized,
            cancellationToken);

        audit.Record(
            AuditAgents.PersonalAi,
            "decision.preview",
            $"decision:{decisionId:D}",
            "user-request",
            AuditResults.Succeeded);

        return new DecisionPreviewResponse(
            decisionId,
            workspaceContext.CurrentWorkspaceId,
            normalized.Options,
            normalized.Criteria,
            managed.Report,
            managed.Sources,
            RequiresUserDecision: true,
            AutoActionEnabled: false,
            ToolExecutionEnabled: false);
    }

    public async Task<DecisionAnalysisResponse> AnalyzeAsync(
        DecisionAnalyzeRequest request,
        CancellationToken cancellationToken = default)
    {
        var normalized = Normalize(request);
        var decisionId = Guid.NewGuid();

        var managed = await BuildContextAsync(
            normalized,
            cancellationToken);

        var provider = providerResolver.GetActive();
        if (!provider.IsConfigured)
        {
            throw new InvalidOperationException(
                $"Nhà cung cấp {provider.Name} chưa được cấu hình để phân tích quyết định.");
        }

        var decisionMessages = managed.Messages.ToArray();
        var lastUserIndex = FindLastUserMessage(decisionMessages);
        if (lastUserIndex < 0)
        {
            throw new DecisionValidationException(
                "Không tạo được yêu cầu phân tích quyết định.");
        }

        decisionMessages[lastUserIndex] = new ChatMessage(
            "user",
            AddDecisionGuardrails(
                decisionMessages[lastUserIndex].Content));

        var analysis = await provider.ReplyAsync(
            decisionMessages,
            cancellationToken);

        audit.Record(
            AuditAgents.PersonalAi,
            "decision.analyze",
            $"decision:{decisionId:D}",
            "context-grounded-user-request",
            AuditResults.Succeeded);

        return new DecisionAnalysisResponse(
            decisionId,
            analysis,
            provider.Name,
            provider.Model,
            managed.Sources,
            managed.Report,
            RequiresUserDecision: true,
            AutoActionEnabled: false,
            ToolExecutionEnabled: false,
            Persisted: false);
    }

    private Task<ContextManagerResult> BuildContextAsync(
        NormalizedDecisionRequest request,
        CancellationToken cancellationToken) =>
        contextManager.BuildAsync(
            [
                new ChatMessage(
                    "user",
                    BuildDecisionQuestion(request))
            ],
            request.UseKnowledge,
            "normal",
            request.UseMemory,
            request.UseTaskContext,
            request.UseLifeContext,
            cancellationToken);

    private static string BuildDecisionQuestion(
        NormalizedDecisionRequest request)
    {
        var builder = new StringBuilder();
        builder.AppendLine("DECISION REQUEST:");
        builder.AppendLine(request.Question);
        builder.AppendLine();
        builder.AppendLine("OPTIONS:");

        for (var index = 0; index < request.Options.Count; index++)
        {
            builder.Append(index + 1)
                .Append(". ")
                .AppendLine(request.Options[index]);
        }

        if (request.Criteria.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("USER CRITERIA:");
            foreach (var criterion in request.Criteria)
            {
                builder.Append("- ")
                    .AppendLine(criterion);
            }
        }

        if (!string.IsNullOrWhiteSpace(request.Constraints))
        {
            builder.AppendLine();
            builder.AppendLine("USER CONSTRAINTS:");
            builder.AppendLine(request.Constraints);
        }

        return builder.ToString().Trim();
    }

    private static string AddDecisionGuardrails(
        string managedQuestion)
    {
        var builder = new StringBuilder();
        builder.AppendLine("DECISION ENGINE v1.7 — ANALYSIS CONTRACT:");
        builder.AppendLine("- Hỗ trợ người dùng suy nghĩ; không tuyên bố đã hành động, không gọi tool và không tự biến recommendation thành action.");
        builder.AppendLine("- Phân biệt rõ dữ kiện từ context, suy luận, giả định và phần còn thiếu.");
        builder.AppendLine("- Không bịa dữ kiện để lấp khoảng trống. Nếu evidence chưa đủ, nói rõ uncertainty.");
        builder.AppendLine("- So sánh tất cả lựa chọn theo các tiêu chí người dùng cung cấp trước; chỉ bổ sung tiêu chí nếu thực sự cần và phải ghi rõ là tiêu chí bổ sung.");
        builder.AppendLine("- Trình bày trade-off và rủi ro chính. Không dùng điểm số giả tạo nếu không có dữ liệu hỗ trợ.");
        builder.AppendLine("- Có thể đưa recommendation khi evidence đủ, nhưng phải kết thúc bằng việc quyết định cuối cùng thuộc về người dùng.");
        builder.AppendLine("- Không đưa ra chuỗi hành động tự động sau recommendation.");
        builder.AppendLine();
        builder.AppendLine("FORMAT:");
        builder.AppendLine("1. Tóm tắt quyết định");
        builder.AppendLine("2. Bằng chứng/context liên quan");
        builder.AppendLine("3. So sánh các lựa chọn theo tiêu chí");
        builder.AppendLine("4. Trade-off, rủi ro và uncertainty");
        builder.AppendLine("5. Recommendation (nếu đủ căn cứ) + điều gì có thể làm thay đổi recommendation");
        builder.AppendLine();
        builder.AppendLine(managedQuestion);
        return builder.ToString();
    }

    private static NormalizedDecisionRequest Normalize(
        DecisionAnalyzeRequest? request)
    {
        if (request is null)
        {
            throw new DecisionValidationException(
                "Yêu cầu Decision Engine không hợp lệ.");
        }

        var question = NormalizeText(
            request.Question,
            MaximumQuestionCharacters,
            "Câu hỏi quyết định");

        var options = (request.Options ?? [])
            .Select(value => NormalizeText(
                value,
                MaximumOptionCharacters,
                "Lựa chọn"))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (options.Length < MinimumOptions
            || options.Length > MaximumOptions)
        {
            throw new DecisionValidationException(
                $"Decision Engine cần từ {MinimumOptions} đến {MaximumOptions} lựa chọn khác nhau.");
        }

        var criteria = (request.Criteria ?? [])
            .Select(value => NormalizeText(
                value,
                MaximumCriterionCharacters,
                "Tiêu chí"))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (criteria.Length > MaximumCriteria)
        {
            throw new DecisionValidationException(
                $"Decision Engine nhận tối đa {MaximumCriteria} tiêu chí.");
        }

        var constraints = string.IsNullOrWhiteSpace(
            request.Constraints)
            ? null
            : NormalizeText(
                request.Constraints,
                MaximumConstraintsCharacters,
                "Ràng buộc");

        return new NormalizedDecisionRequest(
            question,
            options,
            criteria,
            constraints,
            request.UseKnowledge,
            request.UseMemory,
            request.UseTaskContext,
            request.UseLifeContext);
    }

    private static string NormalizeText(
        string? value,
        int maximum,
        string field)
    {
        var normalized = (value ?? string.Empty).Trim();
        if (normalized.Length == 0)
        {
            throw new DecisionValidationException(
                $"{field} không được để trống.");
        }

        if (normalized.Length > maximum)
        {
            throw new DecisionValidationException(
                $"{field} vượt quá giới hạn {maximum} ký tự.");
        }

        return normalized;
    }

    private static int FindLastUserMessage(
        IReadOnlyList<ChatMessage> messages)
    {
        for (var index = messages.Count - 1; index >= 0; index--)
        {
            if (messages[index].Role.Equals(
                "user",
                StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return -1;
    }

    private sealed record NormalizedDecisionRequest(
        string Question,
        IReadOnlyList<string> Options,
        IReadOnlyList<string> Criteria,
        string? Constraints,
        bool UseKnowledge,
        bool UseMemory,
        bool UseTaskContext,
        bool UseLifeContext);
}
