namespace PersonalAI.Web.ModelLab;

public static class RolloutStages
{
    public const string Prepared = "prepared";
    public const string Staging = "staging";
    public const string Canary = "canary";
    public const string Completed = "completed";
    public const string Blocked = "blocked";
}

public sealed record StartModelRolloutRequest(
    Guid PromotionGateDecisionId,
    int StagingTrafficPercent = 0,
    bool ConfirmStartRollout = false);

public sealed record AdvanceModelRolloutRequest(
    string TargetStage,
    int? CanaryTrafficPercent = null,
    bool ConfirmAdvance = false);

public sealed record ModelRolloutState(
    Guid Id,
    string WorkspaceId,
    Guid PromotionGateDecisionId,
    Guid CandidateModelVersionId,
    Guid ProductionModelVersionId,
    string Family,
    string Stage,
    int TrafficPercent,
    bool ProductionMutationPerformed,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record ModelRolloutStatus(
    string Version,
    string WorkspaceId,
    int Rollouts,
    int Staging,
    int Canary,
    int Completed,
    int Blocked,
    bool DirectProductionPromotionEnabled,
    bool Persisted,
    bool ExplicitConfirmationRequired);

public sealed class ModelRolloutValidationException(string message)
    : Exception(message);
