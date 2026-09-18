namespace PersonalAI.Web.Models;

public static class PersonalAiRelease
{
    public const string Version = "1.4.0";
    public const string ApiContractVersion = "1";
    public const string Channel = "controlled";
}

public static class CoreHealthStatuses
{
    public const string Healthy = "healthy";
    public const string Degraded = "degraded";
    public const string Unavailable = "unavailable";
    public const string Unconfigured = "unconfigured";
}

public sealed record CoreModuleHealth(
    string Module,
    string Status,
    string Detail,
    int? ItemCount = null);

public sealed record SystemHealthResponse(
    string Version,
    string ApiContractVersion,
    string Channel,
    bool Ready,
    string Status,
    string WorkspaceId,
    DateTimeOffset CheckedAt,
    IReadOnlyList<CoreModuleHealth> Modules);

public sealed record CoreSafetyContract(
    bool WorkspaceIsolation,
    bool WriteRequiresConfirmation,
    bool DeleteRequiresConfirmation,
    bool ExternalRequiresConfirmation,
    bool UndoRequiresConfirmation,
    bool ComputerControlRequiresConfirmation,
    bool SensitiveComputerObservationRequiresConfirmation,
    bool BrowserUseRequiresConfirmation,
    bool BrowserPrivateNetworkAccess,
    bool BrowserSideEffectsEnabled,
    bool ConnectorUseRequiresConfirmation,
    bool ConnectorSecretsEncrypted,
    bool ConnectorWriteActionsEnabled,
    bool DevelopmentUseRequiresConfirmation,
    bool DevelopmentArbitraryShellEnabled,
    bool DevelopmentGitWriteActionsEnabled,
    bool DevelopmentArbitraryProcessEnabled,
    bool AutomaticMultiStepExecution,
    bool BackgroundScheduler,
    bool AutonomousAgentLoop,
    bool ParallelToolCalls);

public sealed record CoreLimitsContract(
    int MaximumWorkspaces,
    int MaximumTasksPerWorkspace,
    int MaximumContextCharacters,
    int MaximumWorkspaceFileBytes,
    int AuditRetentionDays,
    int MaximumAuditEntries,
    int UndoAvailabilityDays,
    int MaximumUndoEntries,
    int MaximumUndoSnapshotBytes,
    int BrowserMaximumResponseBytes,
    int BrowserMaximumPageTextCharacters,
    int BrowserMaximumLinks,
    int BrowserMaximumRedirects,
    int MaximumConnectorConnections,
    int ConnectorMaximumResponseBytes,
    int ConnectorMaximumReturnedCharacters,
    int ConnectorMaximumRedirects,
    int DevelopmentMaximumScannedFiles,
    int DevelopmentMaximumSearchHits,
    int DevelopmentMaximumProcessOutputCharacters);

public sealed record SystemCapabilitiesResponse(
    string Version,
    string ApiContractVersion,
    string Channel,
    IReadOnlyList<string> StableModules,
    IReadOnlyList<string> ControlledModules,
    IReadOnlyList<string> ReservedModules,
    CoreSafetyContract Safety,
    CoreLimitsContract Limits,
    string WorkspaceHeader,
    string RequestIdHeader);
