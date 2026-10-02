using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public interface IActionExplanationService
{
    ActionExplanationStatus GetStatus();
    ActionExplanation ExplainAudit(Guid auditId);
    ActionExplanation ExplainCorrelation(string correlationId);
}

public sealed class ActionExplanationService(
    IAuditStore auditStore,
    IWorkspaceContextAccessor workspace) : IActionExplanationService
{
    public const int MaximumCorrelationEvents = 50;
    public const int MaximumCorrelationIdCharacters = 160;

    public ActionExplanationStatus GetStatus() =>
        new(
            PersonalAiRelease.Version,
            workspace.CurrentWorkspaceId,
            AuditGrounded: true,
            CorrelationTimelineSupported: true,
            AiGenerated: false,
            HiddenReasoningExposed: false,
            SideEffectsEnabled: false,
            MaximumCorrelationEvents);

    public ActionExplanation ExplainAudit(Guid auditId)
    {
        var item = auditStore.GetById(
            workspace.CurrentWorkspaceId,
            auditId)
            ?? throw new KeyNotFoundException(
                "Không tìm thấy audit event trong workspace hiện tại.");

        return Build(
            "audit-event",
            [item],
            item.CorrelationId);
    }

    public ActionExplanation ExplainCorrelation(string correlationId)
    {
        var normalized = NormalizeCorrelationId(correlationId);
        var response = auditStore.GetRecent(
            workspace.CurrentWorkspaceId,
            MaximumCorrelationEvents,
            correlationId: normalized);

        if (response.Items.Count == 0)
            throw new KeyNotFoundException(
                "Không tìm thấy system log cho correlation ID này.");

        var ordered = response.Items
            .OrderBy(x => x.Time)
            .ThenBy(x => x.AuditId)
            .ToArray();

        return Build(
            "correlation-timeline",
            ordered,
            normalized);
    }

    private ActionExplanation Build(
        string type,
        IReadOnlyList<AuditItem> items,
        string? correlationId)
    {
        var last = items[^1];
        var actor = last.Agent;
        var action = last.Action;
        var target = last.Target;
        var result = last.Result;
        var reason = last.Reason;

        var summary = items.Count == 1
            ? $"{actor} thực hiện '{action}' trên '{target}'. Kết quả: {result}. Lý do/evidence đã ghi: {reason}."
            : $"Correlation {correlationId} có {items.Count} event đã ghi. Event cuối: {actor} thực hiện '{action}' trên '{target}', kết quả {result}. Lý do/evidence cuối: {reason}.";

        return new(
            PersonalAiRelease.Version,
            workspace.CurrentWorkspaceId,
            type,
            summary,
            actor,
            action,
            target,
            result,
            reason,
            correlationId,
            SourceGrounded: true,
            AiGenerated: false,
            HiddenReasoningExposed: false,
            items.Select(ToEvidence).ToArray());
    }

    private static ActionExplanationEvidence ToEvidence(AuditItem item) =>
        new(
            item.AuditId,
            item.Time,
            item.Agent,
            item.Action,
            item.Target,
            item.Reason,
            item.Result,
            item.Level,
            item.Source,
            item.CorrelationId);

    private static string NormalizeCorrelationId(string? value)
    {
        var normalized = (value ?? string.Empty).Trim();
        if (normalized.Length is < 1 or > MaximumCorrelationIdCharacters)
            throw new ActionExplanationValidationException(
                $"CorrelationId phải có từ 1 đến {MaximumCorrelationIdCharacters} ký tự.");

        return normalized;
    }
}
