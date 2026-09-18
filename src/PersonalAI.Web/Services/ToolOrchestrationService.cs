using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public interface IToolOrchestrationService
{
    Task<ToolCallProposal?> ProposeAsync(
        IReadOnlyList<ChatMessage> messages,
        CancellationToken cancellationToken = default);

    ToolCallProposal Prepare(ToolProposalDraft draft);

    Task<ToolProposalExecutionResponse?> ExecuteAsync(
        Guid proposalId,
        bool confirmed,
        CancellationToken cancellationToken = default);

    Task<ToolResultSynthesisResponse?> SynthesizeAsync(
        Guid invocationId,
        bool confirmedExternal,
        CancellationToken cancellationToken = default);

    Task<ToolNativeContinuationResponse?> ContinueNativeAsync(
        Guid invocationId,
        bool confirmedExternal,
        CancellationToken cancellationToken = default);
}

public sealed class ToolProposalValidationException(string message) : Exception(message);

public sealed class ToolOrchestrationService : IToolOrchestrationService
{
    private static readonly TimeSpan ProposalLifetime = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan CompletedInvocationLifetime = TimeSpan.FromMinutes(10);
    private const int MaximumPlannerConversationCharacters = 24_000;
    private const int MaximumPlannerTextLength = 1_000;
    private const int MaximumNativeResultCharacters = 16_000;
    private const int MaximumNativeAnswerCharacters = 8_000;

    private readonly IToolRegistry _registry;
    private readonly IToolInputValidator _validator;
    private readonly IToolExecutionService _executor;
    private readonly IAiProviderResolver _providerResolver;
    private readonly IToolResultSynthesisService _synthesizer;
    private readonly IToolActivityStore _activityStore;
    private readonly ILogger<ToolOrchestrationService> _logger;
    private static readonly ConcurrentDictionary<Guid, ToolCallProposal> Proposals = new();
    private static readonly ConcurrentDictionary<Guid, ProviderFunctionCallContext> NativeProposalContexts = new();
    private static readonly ConcurrentDictionary<Guid, CompletedToolInvocation> CompletedInvocations = new();
    private static readonly SemaphoreSlim SynthesisGate = new(1, 1);
    private static readonly SemaphoreSlim NativeContinuationGate = new(1, 1);

    public ToolOrchestrationService(
        IToolRegistry registry,
        IToolInputValidator validator,
        IToolExecutionService executor,
        IAiProviderResolver providerResolver,
        IToolResultSynthesisService synthesizer,
        IToolActivityStore activityStore,
        ILogger<ToolOrchestrationService> logger)
    {
        _registry = registry;
        _validator = validator;
        _executor = executor;
        _providerResolver = providerResolver;
        _synthesizer = synthesizer;
        _activityStore = activityStore;
        _logger = logger;
    }

    public async Task<ToolCallProposal?> ProposeAsync(
        IReadOnlyList<ChatMessage> messages,
        CancellationToken cancellationToken = default)
    {
        if (messages.Count == 0)
        {
            return null;
        }

        CleanupExpired();
        var provider = _providerResolver.GetActive();
        var definitions = _registry.GetAll();

        var mapped = definitions
            .Select(definition => new
            {
                Definition = definition,
                ProviderName = CreateProviderFunctionName(definition.Name)
            })
            .ToArray();

        var duplicateProviderName = mapped
            .GroupBy(item => item.ProviderName, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateProviderName is not null)
        {
            throw new InvalidOperationException(
                "Không thể ánh xạ danh mục công cụ sang tên hàm an toàn vì có tên bị trùng.");
        }

        var byProviderName = mapped.ToDictionary(
            item => item.ProviderName,
            item => item.Definition,
            StringComparer.Ordinal);

        var providerFunctions = mapped
            .Select(item => new ProviderFunctionDefinition(
                item.ProviderName,
                BuildProviderFunctionDescription(item.Definition),
                item.Definition.InputSchema))
            .ToArray();

        var decision = await provider.ProposeFunctionCallAsync(
            LimitConversation(messages),
            providerFunctions,
            cancellationToken);

        if (decision is null)
        {
            return null;
        }

        if (!byProviderName.TryGetValue(decision.Name, out var definition))
        {
            _logger.LogWarning(
                "{Provider} returned unknown native function {FunctionName}.",
                provider.Name,
                decision.Name);
            return null;
        }

        try
        {
            var proposal = PrepareCore(
                new ToolProposalDraft(
                    definition.Name,
                    decision.Arguments,
                    Reason: $"Đề xuất gọi hàm gốc do {provider.Name} tạo cho yêu cầu hiện tại.",
                    AssistantMessage: null),
                planningMode: "provider-native",
                planningProvider: provider.Name,
                planningModel: provider.Model);
            NativeProposalContexts[proposal.ProposalId] = decision.Context;
            return proposal;
        }
        catch (ToolProposalValidationException exception)
        {
            _logger.LogWarning(
                "{Provider} native function proposal for {ToolName} was rejected by server validation: {Reason}",
                provider.Name,
                definition.Name,
                exception.Message);
            return null;
        }
    }

    public ToolCallProposal Prepare(ToolProposalDraft draft) =>
        PrepareCore(
            draft,
            planningMode: "manual",
            planningProvider: null,
            planningModel: null);

    private ToolCallProposal PrepareCore(
        ToolProposalDraft draft,
        string planningMode,
        string? planningProvider,
        string? planningModel)
    {
        CleanupExpired();

        var toolName = (draft.ToolName ?? string.Empty).Trim();
        if (!_registry.TryGet(toolName, out var tool) || tool is null)
        {
            throw new ToolProposalValidationException(
                "Công cụ được đề xuất không tồn tại trong danh mục công cụ.");
        }

        if (draft.Arguments.ValueKind != JsonValueKind.Object)
        {
            throw new ToolProposalValidationException(
                "Tham số của đề xuất phải là một đối tượng JSON.");
        }

        var validation = _validator.Validate(
            tool.Definition.InputSchema,
            draft.Arguments);
        if (!validation.IsValid)
        {
            throw new ToolProposalValidationException(
                string.Join(" ", validation.Errors));
        }

        var requiresConfirmation = tool.Definition.RequiresConfirmation
            || tool.Definition.RequiredPermissions.Any(
                ToolPermissions.RequiresExplicitConfirmation);

        var reason = NormalizeText(
            draft.Reason,
            $"Đề xuất dùng {GetToolDisplayName(tool.Definition.Name)} cho yêu cầu hiện tại.");
        var assistantMessage = NormalizeText(
            draft.AssistantMessage,
            requiresConfirmation
                ? $"Tôi có thể dùng {GetToolDisplayName(tool.Definition.Name)}. Hãy xem chi tiết và xác nhận trước khi chạy."
                : $"Tôi có thể dùng {GetToolDisplayName(tool.Definition.Name)}. Hãy xem chi tiết trước khi chạy.");

        var proposal = new ToolCallProposal(
            Guid.NewGuid(),
            tool.Definition.Name,
            draft.Arguments.Clone(),
            reason,
            assistantMessage,
            tool.Definition.RequiredPermissions.ToArray(),
            requiresConfirmation,
            DateTimeOffset.UtcNow.Add(ProposalLifetime),
            planningMode,
            planningProvider,
            planningModel);

        Proposals[proposal.ProposalId] = proposal;
        TryRecordActivity(new ToolActivityEvent(
            ToolActivityEventTypes.ProposalCreated,
            proposal.ToolName,
            proposal.ProposalId,
            null,
            proposal.PlanningMode,
            proposal.PlanningProvider,
            proposal.PlanningModel,
            proposal.RequiredPermissions,
            null,
            null,
            "prepared",
            proposal.Arguments));
        return proposal;
    }

    public async Task<ToolProposalExecutionResponse?> ExecuteAsync(
        Guid proposalId,
        bool confirmed,
        CancellationToken cancellationToken = default)
    {
        CleanupExpired();

        if (!Proposals.TryGetValue(proposalId, out var proposal))
        {
            return null;
        }

        if (!_registry.TryGet(proposal.ToolName, out var tool) || tool is null)
        {
            Proposals.TryRemove(proposalId, out _);
            NativeProposalContexts.TryRemove(proposalId, out _);
            return null;
        }

        if (proposal.RequiresConfirmation && !confirmed)
        {
            var denied = await _executor.ExecuteAsync(
                new ToolExecutionRequest(
                    proposal.ToolName,
                    proposal.Arguments,
                    tool.Definition.RequiredPermissions,
                    Confirmed: false),
                cancellationToken);
            TryRecordActivity(new ToolActivityEvent(
                ToolActivityEventTypes.ExecutionDenied,
                proposal.ToolName,
                proposal.ProposalId,
                denied.InvocationId,
                proposal.PlanningMode,
                proposal.PlanningProvider,
                proposal.PlanningModel,
                proposal.RequiredPermissions,
                false,
                null,
                denied.Status,
                proposal.Arguments,
                denied.Output,
                denied.DurationMs));
            return new ToolProposalExecutionResponse(
                proposal,
                denied,
                _synthesizer.CreateLocalSummary(proposal, denied),
                false);
        }

        if (!Proposals.TryRemove(proposalId, out proposal))
        {
            return null;
        }

        NativeProposalContexts.TryRemove(proposalId, out var nativeContext);

        var execution = await _executor.ExecuteAsync(
            new ToolExecutionRequest(
                proposal.ToolName,
                proposal.Arguments,
                tool.Definition.RequiredPermissions,
                Confirmed: confirmed),
            cancellationToken);

        TryRecordActivity(new ToolActivityEvent(
            ToolActivityEventTypes.ExecutionCompleted,
            proposal.ToolName,
            proposal.ProposalId,
            execution.InvocationId,
            proposal.PlanningMode,
            proposal.PlanningProvider,
            proposal.PlanningModel,
            proposal.RequiredPermissions,
            confirmed,
            null,
            execution.Status,
            proposal.Arguments,
            execution.Output,
            execution.DurationMs));

        var localSummary = _synthesizer.CreateLocalSummary(proposal, execution);
        var nonSensitiveOutput = execution.Success
            && execution.Output is not null
            && !proposal.RequiredPermissions.Any(permission =>
                permission.Equals(ToolPermissions.Sensitive, StringComparison.OrdinalIgnoreCase));
        var canAiSynthesize = nonSensitiveOutput;
        var canNativeContinue = nonSensitiveOutput
            && nativeContext is not null
            && proposal.PlanningMode.Equals("provider-native", StringComparison.OrdinalIgnoreCase);

        DateTimeOffset? synthesisExpiresAt = null;
        DateTimeOffset? nativeContinueExpiresAt = null;

        if (canAiSynthesize || canNativeContinue)
        {
            var expiresAt = DateTimeOffset.UtcNow.Add(CompletedInvocationLifetime);
            synthesisExpiresAt = canAiSynthesize ? expiresAt : null;
            nativeContinueExpiresAt = canNativeContinue ? expiresAt : null;
            CompletedInvocations[execution.InvocationId] = new CompletedToolInvocation(
                proposal,
                execution,
                localSummary,
                expiresAt,
                null,
                nativeContext,
                null);
        }

        return new ToolProposalExecutionResponse(
            proposal,
            execution,
            localSummary,
            canAiSynthesize,
            synthesisExpiresAt,
            canNativeContinue,
            nativeContinueExpiresAt);
    }

    public async Task<ToolResultSynthesisResponse?> SynthesizeAsync(
        Guid invocationId,
        bool confirmedExternal,
        CancellationToken cancellationToken = default)
    {
        CleanupExpired();

        if (!CompletedInvocations.TryGetValue(invocationId, out var completed))
        {
            return null;
        }

        if (completed.AiSynthesis is not null)
        {
            return completed.AiSynthesis;
        }

        if (!confirmedExternal)
        {
            TryRecordActivity(new ToolActivityEvent(
                ToolActivityEventTypes.AiSynthesisDenied,
                completed.Proposal.ToolName,
                completed.Proposal.ProposalId,
                invocationId,
                completed.Proposal.PlanningMode,
                completed.Proposal.PlanningProvider,
                completed.Proposal.PlanningModel,
                completed.Proposal.RequiredPermissions,
                null,
                false,
                "denied"));
            throw new ToolExternalConfirmationRequiredException(
                "Diễn giải bằng AI sẽ gửi kết quả công cụ tới nhà cung cấp AI đang cấu hình và cần xác nhận rõ ràng.");
        }

        await SynthesisGate.WaitAsync(cancellationToken);
        try
        {
            if (!CompletedInvocations.TryGetValue(invocationId, out completed))
            {
                return null;
            }

            if (completed.AiSynthesis is not null)
            {
                return completed.AiSynthesis;
            }

            var synthesis = await _synthesizer.SynthesizeWithAiAsync(
                completed.Proposal,
                completed.Execution,
                cancellationToken);

            CompletedInvocations[invocationId] = completed with
            {
                AiSynthesis = synthesis
            };
            TryRecordActivity(new ToolActivityEvent(
                ToolActivityEventTypes.AiSynthesisCompleted,
                completed.Proposal.ToolName,
                completed.Proposal.ProposalId,
                invocationId,
                completed.Proposal.PlanningMode,
                synthesis.Provider,
                synthesis.Model,
                completed.Proposal.RequiredPermissions,
                null,
                true,
                "succeeded"));
            return synthesis;
        }
        finally
        {
            SynthesisGate.Release();
        }
    }


    public async Task<ToolNativeContinuationResponse?> ContinueNativeAsync(
        Guid invocationId,
        bool confirmedExternal,
        CancellationToken cancellationToken = default)
    {
        CleanupExpired();

        if (!CompletedInvocations.TryGetValue(invocationId, out var completed))
        {
            return null;
        }

        if (completed.NativeContinuation is not null)
        {
            return completed.NativeContinuation;
        }

        if (completed.NativeContext is null)
        {
            throw new ToolProposalValidationException(
                "Kết quả này không có ngữ cảnh gọi hàm gốc để tiếp tục.");
        }

        if (!confirmedExternal)
        {
            TryRecordActivity(new ToolActivityEvent(
                ToolActivityEventTypes.NativeContinuationDenied,
                completed.Proposal.ToolName,
                completed.Proposal.ProposalId,
                invocationId,
                completed.Proposal.PlanningMode,
                completed.NativeContext.Provider,
                completed.NativeContext.Model,
                completed.Proposal.RequiredPermissions,
                null,
                false,
                "denied"));
            throw new ToolExternalConfirmationRequiredException(
                "Hoàn tất lời gọi hàm gốc sẽ gửi kết quả công cụ tới đúng nhà cung cấp AI đã tạo đề xuất và cần xác nhận rõ ràng.");
        }

        await NativeContinuationGate.WaitAsync(cancellationToken);
        try
        {
            if (!CompletedInvocations.TryGetValue(invocationId, out completed))
            {
                return null;
            }

            if (completed.NativeContinuation is not null)
            {
                return completed.NativeContinuation;
            }

            if (completed.NativeContext is null)
            {
                throw new ToolProposalValidationException(
                    "Ngữ cảnh gọi hàm gốc không còn khả dụng.");
            }

            var (payload, outputTruncated) = BuildNativeToolResultPayload(completed);
            var provider = _providerResolver.GetByName(completed.NativeContext.Provider);
            var answer = await provider.ContinueFunctionCallAsync(
                completed.NativeContext,
                payload,
                cancellationToken);

            answer = (answer ?? string.Empty).Trim();
            if (answer.Length == 0)
            {
                throw new InvalidOperationException(
                    "Nhà cung cấp AI không trả về câu trả lời cuối từ kết quả gọi hàm gốc.");
            }

            if (answer.Length > MaximumNativeAnswerCharacters)
            {
                answer = answer[..MaximumNativeAnswerCharacters].TrimEnd() + "…";
            }

            var continuation = new ToolNativeContinuationResponse(
                invocationId,
                "provider-native-roundtrip",
                answer,
                completed.NativeContext.Provider,
                completed.NativeContext.Model,
                outputTruncated,
                DateTimeOffset.UtcNow);

            CompletedInvocations[invocationId] = completed with
            {
                NativeContinuation = continuation
            };

            TryRecordActivity(new ToolActivityEvent(
                ToolActivityEventTypes.NativeContinuationCompleted,
                completed.Proposal.ToolName,
                completed.Proposal.ProposalId,
                invocationId,
                completed.Proposal.PlanningMode,
                completed.NativeContext.Provider,
                completed.NativeContext.Model,
                completed.Proposal.RequiredPermissions,
                null,
                true,
                "succeeded"));

            _logger.LogInformation(
                "Tool result {InvocationId} continued through native function protocol with {Provider}/{Model}. Truncated output: {OutputTruncated}.",
                invocationId,
                completed.NativeContext.Provider,
                completed.NativeContext.Model,
                outputTruncated);

            return continuation;
        }
        finally
        {
            NativeContinuationGate.Release();
        }
    }

    private static (string Payload, bool OutputTruncated) BuildNativeToolResultPayload(
        CompletedToolInvocation completed)
    {
        var rawOutput = completed.Execution.Output?.GetRawText() ?? "null";
        var outputTruncated = rawOutput.Length > MaximumNativeResultCharacters;
        if (outputTruncated)
        {
            var length = MaximumNativeResultCharacters;
            if (length > 0
                && length < rawOutput.Length
                && char.IsHighSurrogate(rawOutput[length - 1])
                && char.IsLowSurrogate(rawOutput[length]))
            {
                length--;
            }

            rawOutput = rawOutput[..length];
        }

        var payload = JsonSerializer.Serialize(new
        {
            toolName = completed.Execution.ToolName,
            status = completed.Execution.Status,
            localSummary = completed.LocalSummary,
            outputTruncated,
            outputJson = rawOutput
        });

        return (payload, outputTruncated);
    }

    private static string GetToolDisplayName(string toolName) =>
        toolName switch
        {
            "app.summary" => "Tổng quan ứng dụng",
            "documents.search" => "Tìm trong tài liệu",
            "local.calculate" => "Máy tính",
            "local.clock" => "Đồng hồ hệ thống",
            "local.date_math" => "Tính toán ngày giờ",
            "local.text_stats" => "Thống kê văn bản",
            "memory.search" => "Tìm trong trí nhớ",
            "workspace.create_directory" => "Tạo thư mục",
            "workspace.delete" => "Xóa tệp hoặc thư mục",
            "workspace.list" => "Liệt kê thư mục làm việc",
            "workspace.move" => "Di chuyển hoặc đổi tên",
            "workspace.read_text" => "Đọc tệp văn bản",
            "workspace.write_text" => "Ghi tệp văn bản",
            _ => "công cụ"
        };

    private static string CreateProviderFunctionName(string internalName)
    {
        var normalized = new StringBuilder();
        foreach (var character in internalName)
        {
            normalized.Append(char.IsAsciiLetterOrDigit(character) ? character : '_');
        }

        var readable = normalized.ToString().Trim('_');
        if (readable.Length == 0)
        {
            readable = "tool";
        }
        if (readable.Length > 42)
        {
            readable = readable[..42];
        }

        var hash = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(internalName)))
            .ToLowerInvariant()[..10];

        return $"pai_{readable}_{hash}";
    }

    private static string BuildProviderFunctionDescription(ToolDefinition definition)
    {
        var permissions = string.Join(", ", definition.RequiredPermissions);
        var confirmation = definition.RequiresConfirmation
            || definition.RequiredPermissions.Any(ToolPermissions.RequiresExplicitConfirmation);

        return $"{definition.Description} Công cụ nội bộ: {definition.Name}. "
            + $"Permissions: {permissions}. "
            + (confirmation
                ? "Đây chỉ là đề xuất; ứng dụng sẽ yêu cầu xác nhận riêng trước khi thực thi."
                : "Đây chỉ là đề xuất; ứng dụng sẽ chỉ thực thi sau thao tác riêng của người dùng.");
    }

    private static IReadOnlyList<ChatMessage> LimitConversation(
        IReadOnlyList<ChatMessage> messages)
    {
        var selected = new List<ChatMessage>();
        var characters = 0;

        foreach (var message in messages.Reverse().Take(12))
        {
            var content = message.Content ?? string.Empty;
            if (content.Length > 8_000)
            {
                content = content[..8_000];
            }

            if (characters + content.Length > MaximumPlannerConversationCharacters)
            {
                var remaining = MaximumPlannerConversationCharacters - characters;
                if (remaining <= 0)
                {
                    break;
                }

                content = content[..Math.Min(content.Length, remaining)];
            }

            selected.Add(new ChatMessage(message.Role, content));
            characters += content.Length;
            if (characters >= MaximumPlannerConversationCharacters)
            {
                break;
            }
        }

        selected.Reverse();
        return selected;
    }

    private void CleanupExpired()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var pair in Proposals)
        {
            if (pair.Value.ExpiresAt <= now)
            {
                Proposals.TryRemove(pair.Key, out _);
                NativeProposalContexts.TryRemove(pair.Key, out _);
            }
        }

        foreach (var pair in CompletedInvocations)
        {
            if (pair.Value.ExpiresAt <= now)
            {
                CompletedInvocations.TryRemove(pair.Key, out _);
            }
        }

        if (Proposals.Count > 100)
        {
            foreach (var proposal in Proposals.Values
                         .OrderBy(value => value.ExpiresAt)
                         .Take(Proposals.Count - 100))
            {
                Proposals.TryRemove(proposal.ProposalId, out _);
                NativeProposalContexts.TryRemove(proposal.ProposalId, out _);
            }
        }

        if (CompletedInvocations.Count > 100)
        {
            foreach (var completed in CompletedInvocations.Values
                         .OrderBy(value => value.ExpiresAt)
                         .Take(CompletedInvocations.Count - 100))
            {
                CompletedInvocations.TryRemove(completed.Execution.InvocationId, out _);
            }
        }
    }

    private void TryRecordActivity(ToolActivityEvent activity)
    {
        try
        {
            _activityStore.Record(activity);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Không thể ghi nhật ký hoạt động công cụ {EventType} cho {ToolName}.",
                activity.EventType,
                activity.ToolName);
        }
    }

    private static string NormalizeText(string? value, string fallback)
    {
        var text = string.IsNullOrWhiteSpace(value)
            ? fallback
            : value.Trim();
        if (text.Length > MaximumPlannerTextLength)
        {
            text = text[..MaximumPlannerTextLength];
        }

        return text;
    }

    private sealed record CompletedToolInvocation(
        ToolCallProposal Proposal,
        ToolExecutionResponse Execution,
        string LocalSummary,
        DateTimeOffset ExpiresAt,
        ToolResultSynthesisResponse? AiSynthesis,
        ProviderFunctionCallContext? NativeContext,
        ToolNativeContinuationResponse? NativeContinuation);
}
