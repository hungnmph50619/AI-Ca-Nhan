namespace PersonalAI.Web.Models;

public sealed record RecoverDevelopmentRunRequest(
    Guid DevelopmentRunId,
    bool ConfirmRecovery = false);

public sealed record DevelopmentRecoveryEvidence(
    string Stage,
    string EvidenceType,
    Guid EvidenceId,
    bool Valid,
    string Detail);

public sealed record DevelopmentRecoveryResult(
    string Version,
    string WorkspaceId,
    Guid DevelopmentRunId,
    string StageBefore,
    string StageAfter,
    bool Advanced,
    bool SideEffectReplayed,
    bool RecoveryRequired,
    string NextAction,
    IReadOnlyList<DevelopmentRecoveryEvidence> Evidence,
    DateTimeOffset RecoveredAt);

public sealed record DevelopmentRecoveryStatus(
    string Version,
    string WorkspaceId,
    int ActiveRuns,
    int RunsNeedingRecovery,
    bool PersistedStateUsed,
    bool SideEffectReplayEnabled,
    bool SafeReconciliationEnabled,
    IReadOnlyList<string> ReconciliableStages);

public sealed class DevelopmentRecoveryValidationException(string message)
    : Exception(message);
