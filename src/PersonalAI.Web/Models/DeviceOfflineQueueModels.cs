namespace PersonalAI.Web.Models;

public static class DeviceOfflineQueueKinds
{
    public const string Task = "task";
    public const string Event = "event";

    public static readonly IReadOnlySet<string> All =
        new HashSet<string>(
            [Task, Event],
            StringComparer.Ordinal);
}

public static class DeviceOfflineQueueStatuses
{
    public const string Queued = "queued";
    public const string Delivered = "delivered";
}

public sealed record EnqueueDeviceOfflineItemRequest(
    string Kind,
    string ReferenceId,
    string Payload,
    bool ConfirmEnqueue = false);

public sealed record SyncDeviceOfflineQueueRequest(
    bool ConfirmSync = false);

public sealed record DeviceOfflineQueueItem(
    Guid Id,
    string WorkspaceId,
    Guid DeviceId,
    string Kind,
    string ReferenceId,
    string Payload,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? DeliveredAt);

public sealed record DeviceOfflineQueueSyncResult(
    Guid DeviceId,
    int DeliveredCount,
    DateTimeOffset SyncedAt,
    IReadOnlyList<DeviceOfflineQueueItem> Items);

public sealed record DeviceOfflineQueueStatus(
    string Version,
    string WorkspaceId,
    bool Persistent,
    bool ExplicitEnqueueConfirmationRequired,
    bool ExplicitSyncConfirmationRequired,
    bool OnlineDevicesRejectedForEnqueue,
    bool OfflineOrUnknownDevicesQueueable,
    bool DeliveryRequiresOnlineDevice,
    bool IdempotentReferenceKeys,
    int MaximumItemsPerWorkspace,
    int MaximumPayloadCharacters,
    IReadOnlyList<string> SupportedKinds);

public sealed class DeviceOfflineQueueValidationException(string message)
    : Exception(message);
