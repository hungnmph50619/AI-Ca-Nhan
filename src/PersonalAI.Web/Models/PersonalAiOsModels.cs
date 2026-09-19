namespace PersonalAI.Web.Models;

public static class PersonalAiOsCapabilityStates
{
    public const string Ready = "ready";
    public const string Controlled = "controlled";
    public const string Foundation = "foundation";
    public const string Unconfigured = "unconfigured";
    public const string Unavailable = "unavailable";
    public const string Reserved = "reserved";
}

public sealed record PersonalAiOsCapability(
    string Id,
    string Name,
    string Layer,
    string State,
    string Detail,
    IReadOnlyList<string> Interfaces,
    bool RequiresUserConfirmation,
    bool WorkspaceScoped,
    bool SendsDataExternally);

public sealed record PersonalAiOsLayer(
    string Id,
    string Name,
    string Purpose,
    IReadOnlyList<string> CapabilityIds);

public sealed record PersonalAiOsGovernance(
    bool WorkspaceIsolation,
    bool PermissionPolicy,
    bool ExplicitConfirmationForSideEffects,
    bool Audit,
    bool Undo,
    bool Hardening,
    bool BackupRestore,
    bool AutonomousAgentLoop,
    bool ParallelToolCalls,
    bool MultiAgentFramework,
    bool AgentOrchestration);

public sealed record PersonalAiOsReadiness(
    bool OsContractReady,
    bool ReadyForV21Foundation,
    string HealthStatus,
    int CapabilityCount,
    int ReadyCount,
    int ControlledCount,
    int FoundationCount,
    int UnconfiguredCount,
    int UnavailableCount,
    IReadOnlyList<string> Blockers);

public sealed record PersonalAiOsStatusResponse(
    string Version,
    string ApiContractVersion,
    string Edition,
    string Stage,
    string WorkspaceId,
    string WorkspaceName,
    DateTimeOffset CheckedAt,
    PersonalAiOsReadiness Readiness,
    PersonalAiOsGovernance Governance,
    IReadOnlyList<PersonalAiOsLayer> Layers,
    IReadOnlyList<PersonalAiOsCapability> Capabilities,
    string NextStage);

public sealed record PersonalAiOsManifestResponse(
    string Version,
    string Edition,
    string Stage,
    IReadOnlyList<PersonalAiOsLayer> Layers,
    IReadOnlyList<PersonalAiOsCapability> Capabilities,
    PersonalAiOsGovernance Governance,
    IReadOnlyList<string> Boundaries);
