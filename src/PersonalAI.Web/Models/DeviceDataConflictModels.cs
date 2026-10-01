namespace PersonalAI.Web.Models;

public static class DeviceDataConflictPolicies
{
    public const string RejectStale = "reject-stale";
    public const string LastWriteWins = "last-write-wins";
    public const string ManualReview = "manual-review";

    public static readonly IReadOnlySet<string> All =
        new HashSet<string>(
            [RejectStale, LastWriteWins, ManualReview],
            StringComparer.Ordinal);
}

public static class DeviceDataConflictDecisions
{
    public const string Accepted = "accepted";
    public const string RejectedStale = "rejected-stale";
    public const string RejectedLocked = "rejected-locked";
    public const string NeedsReview = "needs-review";
}

public sealed record AcquireDeviceDataLockRequest(
    string ResourceType,
    string ResourceId,
    Guid DeviceId,
    int LeaseSeconds = 300,
    bool ConfirmAcquire = false);

public sealed record ReleaseDeviceDataLockRequest(
    Guid DeviceId,
    bool ConfirmRelease = false);

public sealed record ApplyDeviceDataMutationRequest(
    string ResourceType,
    string ResourceId,
    Guid DeviceId,
    long BaseVersion,
    DateTimeOffset ClientUpdatedAt,
    string PayloadHash,
    string Policy,
    bool ConfirmApply = false);

public sealed record DeviceDataLock(
    string WorkspaceId,
    string ResourceType,
    string ResourceId,
    Guid DeviceId,
    DateTimeOffset AcquiredAt,
    DateTimeOffset ExpiresAt);

public sealed record DeviceDataVersion(
    string WorkspaceId,
    string ResourceType,
    string ResourceId,
    long Version,
    DateTimeOffset UpdatedAt,
    Guid UpdatedByDeviceId,
    string PayloadHash);

public sealed record DeviceDataConflictResult(
    string WorkspaceId,
    string ResourceType,
    string ResourceId,
    string Policy,
    string Decision,
    long ServerVersion,
    long BaseVersion,
    long? NewVersion,
    DateTimeOffset ServerUpdatedAt,
    DateTimeOffset ClientUpdatedAt,
    Guid DeviceId,
    Guid? LockOwnerDeviceId,
    string Reason);

public sealed record DeviceDataConflictStatus(
    string Version,
    string WorkspaceId,
    bool Persistent,
    bool OptimisticVersionCheckEnabled,
    bool TimestampTracked,
    bool ResourceLockEnabled,
    bool ExpiredLocksIgnored,
    bool ExplicitApplyConfirmationRequired,
    int MinimumLeaseSeconds,
    int MaximumLeaseSeconds,
    IReadOnlyList<string> SupportedPolicies);

public sealed class DeviceDataConflictValidationException(string message)
    : Exception(message);
