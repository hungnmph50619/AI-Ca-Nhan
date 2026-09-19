namespace PersonalAI.Web.Models;

public static class HardeningStatuses
{
    public const string Healthy = "healthy";
    public const string Degraded = "degraded";
    public const string PendingRestart = "pending-restart";
    public const string Failed = "failed";
}

public sealed record HardeningRuntimeStatus(
    bool Running,
    bool PreviousUncleanShutdown,
    DateTimeOffset? PreviousStartedAt,
    DateTimeOffset? PreviousHeartbeatAt,
    DateTimeOffset CurrentStartedAt,
    DateTimeOffset LastHeartbeatAt,
    string? LastRestoreStatus,
    DateTimeOffset? LastRestoreAt);

public sealed record HardeningPermissionFinding(
    string ToolName,
    IReadOnlyList<string> RiskPermissions,
    bool ConfirmationBlockedWithoutUserApproval,
    string Detail);

public sealed record HardeningPermissionAudit(
    string Status,
    int ToolCount,
    int RiskyToolCount,
    int ViolationCount,
    DateTimeOffset CheckedAt,
    IReadOnlyList<HardeningPermissionFinding> Findings);

public sealed record HardeningBackupInfo(
    string BackupId,
    DateTimeOffset CreatedAt,
    long SizeBytes,
    int FileCount,
    string Version);

public sealed record HardeningStatusResponse(
    string Version,
    string Status,
    DateTimeOffset CheckedAt,
    HardeningRuntimeStatus Runtime,
    HardeningPermissionAudit PermissionAudit,
    IReadOnlyList<HardeningBackupInfo> Backups,
    int MaximumApiRequestsPerMinute,
    int MaximumConcurrentApiRequests,
    long MaximumApiRequestBytes,
    int MaximumBackupCount,
    long MaximumBackupSourceBytes,
    bool PendingRestore,
    bool CrossProcessMaintenanceLock);

public sealed record CreateHardeningBackupRequest(
    bool Confirmed = false);

public sealed record RestoreHardeningBackupRequest(
    string? BackupId,
    bool Confirmed = false);

public sealed record HardeningRestoreResponse(
    string BackupId,
    string Status,
    bool RestartRequired,
    string Message);
