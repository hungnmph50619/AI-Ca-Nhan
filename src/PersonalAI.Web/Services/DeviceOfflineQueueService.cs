using System.Text.Json;
using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public interface IDeviceOfflineQueueService
{
    DeviceOfflineQueueStatus GetStatus();
    IReadOnlyList<DeviceOfflineQueueItem> GetAll(Guid? deviceId = null);
    DeviceOfflineQueueItem Enqueue(
        Guid deviceId,
        EnqueueDeviceOfflineItemRequest request);
    DeviceOfflineQueueSyncResult Sync(
        Guid deviceId,
        SyncDeviceOfflineQueueRequest request);
}

public sealed class DeviceOfflineQueueService(
    IDeviceHubService hub,
    IWorkspaceContextAccessor workspace,
    IConfiguration configuration,
    IAuditRecorder audit) : IDeviceOfflineQueueService
{
    public const int MaximumItemsPerWorkspace = 512;
    public const int MaximumPayloadCharacters = 4_000;
    public const int MaximumReferenceIdCharacters = 160;

    private readonly object _gate = new();
    private readonly string _root = ResolveRoot(configuration);

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web)
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

    public DeviceOfflineQueueStatus GetStatus() =>
        new(
            PersonalAiRelease.Version,
            workspace.CurrentWorkspaceId,
            Persistent: true,
            ExplicitEnqueueConfirmationRequired: true,
            ExplicitSyncConfirmationRequired: true,
            OnlineDevicesRejectedForEnqueue: true,
            OfflineOrUnknownDevicesQueueable: true,
            DeliveryRequiresOnlineDevice: true,
            IdempotentReferenceKeys: true,
            MaximumItemsPerWorkspace,
            MaximumPayloadCharacters,
            DeviceOfflineQueueKinds.All.Order(StringComparer.Ordinal).ToArray());

    public IReadOnlyList<DeviceOfflineQueueItem> GetAll(Guid? deviceId = null)
    {
        var workspaceId = workspace.CurrentWorkspaceId;
        lock (_gate)
        {
            return Load(workspaceId)
                .Where(x => deviceId is null || x.DeviceId == deviceId)
                .OrderByDescending(x => x.CreatedAt)
                .ToArray();
        }
    }

    public DeviceOfflineQueueItem Enqueue(
        Guid deviceId,
        EnqueueDeviceOfflineItemRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!request.ConfirmEnqueue)
            throw new DeviceOfflineQueueValidationException(
                "Cần ConfirmEnqueue=true trước khi xếp task/event cho thiết bị offline.");

        var workspaceId = workspace.CurrentWorkspaceId;
        var device = RequireDevice(workspaceId, deviceId);

        if (device.ConnectionStatus == DeviceHubConnectionStatuses.Online)
            throw new DeviceOfflineQueueValidationException(
                "Thiết bị đang online; không được đưa vào offline queue.");

        var kind = NormalizeKind(request.Kind);
        var referenceId = NormalizeReferenceId(request.ReferenceId);
        var payload = NormalizePayload(request.Payload);

        lock (_gate)
        {
            var items = Load(workspaceId);
            var duplicate = items.FirstOrDefault(x =>
                x.DeviceId == deviceId
                && x.Kind == kind
                && x.ReferenceId.Equals(referenceId, StringComparison.Ordinal)
                && x.Status == DeviceOfflineQueueStatuses.Queued);

            if (duplicate is not null)
                return duplicate;

            if (items.Count(x => x.Status == DeviceOfflineQueueStatuses.Queued)
                >= MaximumItemsPerWorkspace)
            {
                throw new DeviceOfflineQueueValidationException(
                    $"Offline queue đã đạt giới hạn {MaximumItemsPerWorkspace} item đang chờ.");
            }

            var item = new DeviceOfflineQueueItem(
                Guid.NewGuid(),
                workspaceId,
                deviceId,
                kind,
                referenceId,
                payload,
                DeviceOfflineQueueStatuses.Queued,
                DateTimeOffset.UtcNow,
                DeliveredAt: null);

            items.Add(item);
            Save(workspaceId, items);

            audit.Record(
                AuditAgents.System,
                "device-offline-queue.enqueue",
                $"offline-queue:{item.Id:D}",
                $"device:{deviceId:D};kind:{kind};reference:{referenceId};status:queued",
                AuditResults.Prepared,
                workspaceId: workspaceId);

            return item;
        }
    }

    public DeviceOfflineQueueSyncResult Sync(
        Guid deviceId,
        SyncDeviceOfflineQueueRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!request.ConfirmSync)
            throw new DeviceOfflineQueueValidationException(
                "Cần ConfirmSync=true trước khi đồng bộ offline queue.");

        var workspaceId = workspace.CurrentWorkspaceId;
        var device = RequireDevice(workspaceId, deviceId);

        if (device.ConnectionStatus != DeviceHubConnectionStatuses.Online)
            throw new DeviceOfflineQueueValidationException(
                "Thiết bị chưa online nên chưa thể đồng bộ offline queue.");

        lock (_gate)
        {
            var items = Load(workspaceId);
            var now = DateTimeOffset.UtcNow;
            var delivered = new List<DeviceOfflineQueueItem>();

            for (var i = 0; i < items.Count; i++)
            {
                var current = items[i];
                if (current.DeviceId != deviceId
                    || current.Status != DeviceOfflineQueueStatuses.Queued)
                {
                    continue;
                }

                var updated = current with
                {
                    Status = DeviceOfflineQueueStatuses.Delivered,
                    DeliveredAt = now
                };

                items[i] = updated;
                delivered.Add(updated);
            }

            Save(workspaceId, items);

            audit.Record(
                AuditAgents.System,
                "device-offline-queue.sync",
                $"device:{deviceId:D}",
                $"delivered:{delivered.Count}",
                AuditResults.Succeeded,
                workspaceId: workspaceId);

            return new(
                deviceId,
                delivered.Count,
                now,
                delivered);
        }
    }

    private DeviceHubDevice RequireDevice(
        string workspaceId,
        Guid deviceId)
    {
        return hub.ReconcileCompanionDevices(workspaceId)
            .FirstOrDefault(x => x.Id == deviceId)
            ?? throw new KeyNotFoundException(
                "Không tìm thấy thiết bị trong workspace hiện tại.");
    }

    private static string NormalizeKind(string? value)
    {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        if (!DeviceOfflineQueueKinds.All.Contains(normalized))
            throw new DeviceOfflineQueueValidationException(
                "Kind chỉ hỗ trợ task hoặc event.");

        return normalized;
    }

    private static string NormalizeReferenceId(string? value)
    {
        var normalized = (value ?? string.Empty).Trim();
        if (normalized.Length is < 1 or > MaximumReferenceIdCharacters)
            throw new DeviceOfflineQueueValidationException(
                $"ReferenceId phải có từ 1 đến {MaximumReferenceIdCharacters} ký tự.");

        return normalized;
    }

    private static string NormalizePayload(string? value)
    {
        var normalized = (value ?? string.Empty).Trim();
        if (normalized.Length is < 1 or > MaximumPayloadCharacters)
            throw new DeviceOfflineQueueValidationException(
                $"Payload phải có từ 1 đến {MaximumPayloadCharacters:N0} ký tự.");

        return normalized;
    }

    private List<DeviceOfflineQueueItem> Load(string workspaceId)
    {
        var path = PathFor(workspaceId);
        if (!File.Exists(path))
            return [];

        try
        {
            return JsonSerializer.Deserialize<List<DeviceOfflineQueueItem>>(
                File.ReadAllText(path),
                JsonOptions) ?? [];
        }
        catch (JsonException exception)
        {
            throw new DeviceOfflineQueueValidationException(
                $"Offline queue bị hỏng và bị fail-closed: {exception.Message}");
        }
    }

    private void Save(
        string workspaceId,
        IReadOnlyList<DeviceOfflineQueueItem> items)
    {
        Directory.CreateDirectory(_root);
        var path = PathFor(workspaceId);
        var temp = path + ".tmp";
        File.WriteAllText(
            temp,
            JsonSerializer.Serialize(items, JsonOptions));
        File.Move(temp, path, overwrite: true);
    }

    private string PathFor(string workspaceId) =>
        Path.Combine(
            _root,
            $"offline-queue-{SafeFileName(workspaceId)}.json");

    private static string SafeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        return new string(
            value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
    }

    private static string ResolveRoot(IConfiguration configuration)
    {
        var configured = configuration["OfflineQueue:Root"];
        if (!string.IsNullOrWhiteSpace(configured))
            return Path.GetFullPath(configured);

        var localData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localData))
        {
            localData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".personalai");
        }

        return Path.Combine(
            localData,
            "PersonalAI",
            "OfflineQueue");
    }
}
