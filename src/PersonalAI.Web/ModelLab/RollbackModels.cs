namespace PersonalAI.Web.ModelLab;

public sealed record EvaluateRollbackRequest(
    Guid RolloutId,
    double QualityScore,
    double ErrorRate,
    double AverageLatencyMilliseconds,
    int SafetyIncidents,
    double MinimumQualityScore = 0.85,
    double MaximumErrorRate = 0.05,
    double MaximumLatencyMilliseconds = 500,
    int MaximumSafetyIncidents = 0);

public sealed record RollbackTrigger(
    string Metric,
    bool Triggered,
    string Detail);

public sealed record RollbackDecision(
    Guid Id,
    string WorkspaceId,
    Guid RolloutId,
    Guid CandidateModelVersionId,
    Guid ProductionModelVersionId,
    EvaluateRollbackRequest Evaluation,
    IReadOnlyList<RollbackTrigger> Triggers,
    bool RollbackRequired,
    bool RollbackPerformed,
    string ResultingRolloutStage,
    int ResultingTrafficPercent,
    DateTimeOffset CreatedAt);

public sealed record RollbackStatus(
    string Version,
    string WorkspaceId,
    int Decisions,
    int RollbacksPerformed,
    bool AutomaticRollbackEnabled,
    bool ProductionBaselineMustRemainActive,
    bool AuditRequired);

public sealed class RollbackValidationException(string message)
    : Exception(message);
