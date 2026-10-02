namespace PersonalAI.Web.Models;

public sealed record ActionExplanationEvidence(
    Guid AuditId,
    DateTimeOffset Time,
    string Agent,
    string Action,
    string Target,
    string Reason,
    string Result,
    string? Level,
    string? Source,
    string? CorrelationId);

public sealed record ActionExplanation(
    string Version,
    string WorkspaceId,
    string ExplanationType,
    string Summary,
    string Actor,
    string Action,
    string Target,
    string Result,
    string Reason,
    string? CorrelationId,
    bool SourceGrounded,
    bool AiGenerated,
    bool HiddenReasoningExposed,
    IReadOnlyList<ActionExplanationEvidence> Evidence);

public sealed record ActionExplanationStatus(
    string Version,
    string WorkspaceId,
    bool AuditGrounded,
    bool CorrelationTimelineSupported,
    bool AiGenerated,
    bool HiddenReasoningExposed,
    bool SideEffectsEnabled,
    int MaximumCorrelationEvents);

public sealed class ActionExplanationValidationException(string message)
    : Exception(message);
