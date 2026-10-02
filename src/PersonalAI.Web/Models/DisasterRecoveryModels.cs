namespace PersonalAI.Web.Models;

public static class DisasterRecoveryVerificationStatuses
{
    public const string Verified = "verified";
    public const string Invalid = "invalid";
}

public sealed record DisasterRecoveryBackupVerification(
    string BackupId,
    string Status,
    bool Restorable,
    string? PersonalAiVersion,
    DateTimeOffset? CreatedAt,
    int ManifestFileCount,
    int ArchiveFileCount,
    long ManifestSourceBytes,
    long ArchiveUncompressedBytes,
    long ArchiveSizeBytes,
    string ArchiveSha256,
    string? Error);

public sealed record DisasterRecoveryPlan(
    string Version,
    string WorkspaceId,
    bool RecoveryAvailable,
    string? RecommendedBackupId,
    DateTimeOffset? RecommendedBackupCreatedAt,
    bool RecommendedBackupVerified,
    bool PendingRestore,
    bool PreRestoreBackupEnabled,
    bool StartupRestoreEnabled,
    bool RollbackOnRestoreFailureEnabled,
    bool ExplicitConfirmationRequired,
    string Message);

public sealed record QueueDisasterRecoveryRequest(
    string? BackupId,
    bool ConfirmRestore = false);

public sealed record DisasterRecoveryStatus(
    string Version,
    string WorkspaceId,
    int BackupCount,
    int VerifiedBackups,
    int InvalidBackups,
    bool ArchiveIntegrityVerificationEnabled,
    bool PathTraversalProtectionEnabled,
    bool RestoreSizeLimitsEnforced,
    bool PreRestoreBackupEnabled,
    bool StartupRestoreEnabled,
    bool RollbackOnRestoreFailureEnabled,
    bool ExplicitConfirmationRequired,
    bool PendingRestore);

public sealed class DisasterRecoveryValidationException(string message)
    : Exception(message);
