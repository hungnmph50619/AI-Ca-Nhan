using System.Text.Json;
using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public interface IDeviceDataConflictService
{
    DeviceDataConflictStatus GetStatus();
    IReadOnlyList<DeviceDataLock> GetLocks();
    IReadOnlyList<DeviceDataVersion> GetVersions();
    DeviceDataLock AcquireLock(AcquireDeviceDataLockRequest request);
    bool ReleaseLock(string resourceType, string resourceId, ReleaseDeviceDataLockRequest request);
    DeviceDataConflictResult Apply(ApplyDeviceDataMutationRequest request);
}

public sealed class DeviceDataConflictService(
    IDeviceHubService hub,
    IWorkspaceContextAccessor workspace,
    IConfiguration configuration,
    IAuditRecorder audit) : IDeviceDataConflictService
{
    public const int MinimumLeaseSeconds = 30;
    public const int MaximumLeaseSeconds = 3_600;
    public const int MaximumResourceTypeCharacters = 64;
    public const int MaximumResourceIdCharacters = 180;
    public const int MaximumPayloadHashCharacters = 160;

    private readonly object _gate = new();
    private readonly string _root = ResolveRoot(configuration);

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web)
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

    public DeviceDataConflictStatus GetStatus() =>
        new(
            PersonalAiRelease.Version,
            workspace.CurrentWorkspaceId,
            Persistent: true,
            OptimisticVersionCheckEnabled: true,
            TimestampTracked: true,
            ResourceLockEnabled: true,
            ExpiredLocksIgnored: true,
            ExplicitApplyConfirmationRequired: true,
            MinimumLeaseSeconds,
            MaximumLeaseSeconds,
            DeviceDataConflictPolicies.All.Order(StringComparer.Ordinal).ToArray());

    public IReadOnlyList<DeviceDataLock> GetLocks()
    {
        var workspaceId = workspace.CurrentWorkspaceId;
        lock (_gate)
        {
            var state = Load(workspaceId);
            CleanupExpiredLocks(state);
            Save(workspaceId, state);
            return state.Locks
                .OrderBy(x => x.ExpiresAt)
                .ToArray();
        }
    }

    public IReadOnlyList<DeviceDataVersion> GetVersions()
    {
        var workspaceId = workspace.CurrentWorkspaceId;
        lock (_gate)
        {
            return Load(workspaceId)
                .Versions
                .OrderBy(x => x.ResourceType, StringComparer.Ordinal)
                .ThenBy(x => x.ResourceId, StringComparer.Ordinal)
                .ToArray();
        }
    }

    public DeviceDataLock AcquireLock(
        AcquireDeviceDataLockRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!request.ConfirmAcquire)
            throw new DeviceDataConflictValidationException(
                "Cần ConfirmAcquire=true trước khi giữ khóa dữ liệu.");

        var workspaceId = workspace.CurrentWorkspaceId;
        RequireDevice(workspaceId, request.DeviceId);
        var resourceType = NormalizeResourceType(request.ResourceType);
        var resourceId = NormalizeResourceId(request.ResourceId);

        if (request.LeaseSeconds is < MinimumLeaseSeconds or > MaximumLeaseSeconds)
            throw new DeviceDataConflictValidationException(
                $"LeaseSeconds phải từ {MinimumLeaseSeconds} đến {MaximumLeaseSeconds}.");

        lock (_gate)
        {
            var state = Load(workspaceId);
            CleanupExpiredLocks(state);

            var existing = state.Locks.FirstOrDefault(x =>
                x.ResourceType == resourceType
                && x.ResourceId == resourceId);

            if (existing is not null)
            {
                if (existing.DeviceId == request.DeviceId)
                    return existing;

                throw new DeviceDataConflictValidationException(
                    $"Resource đang bị khóa bởi device {existing.DeviceId:D}.");
            }

            var now = DateTimeOffset.UtcNow;
            var dataLock = new DeviceDataLock(
                workspaceId,
                resourceType,
                resourceId,
                request.DeviceId,
                now,
                now.AddSeconds(request.LeaseSeconds));

            state.Locks.Add(dataLock);
            Save(workspaceId, state);

            audit.Record(
                AuditAgents.System,
                "device-data-conflict.lock.acquire",
                $"{resourceType}:{resourceId}",
                $"device:{request.DeviceId:D};expires:{dataLock.ExpiresAt:O}",
                AuditResults.Succeeded,
                workspaceId: workspaceId);

            return dataLock;
        }
    }

    public bool ReleaseLock(
        string resourceType,
        string resourceId,
        ReleaseDeviceDataLockRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!request.ConfirmRelease)
            throw new DeviceDataConflictValidationException(
                "Cần ConfirmRelease=true trước khi nhả khóa dữ liệu.");

        var workspaceId = workspace.CurrentWorkspaceId;
        var type = NormalizeResourceType(resourceType);
        var id = NormalizeResourceId(resourceId);

        lock (_gate)
        {
            var state = Load(workspaceId);
            CleanupExpiredLocks(state);
            var existing = state.Locks.FirstOrDefault(x =>
                x.ResourceType == type
                && x.ResourceId == id);

            if (existing is null)
                return false;

            if (existing.DeviceId != request.DeviceId)
                throw new DeviceDataConflictValidationException(
                    "Chỉ device đang giữ khóa mới được nhả khóa.");

            state.Locks.Remove(existing);
            Save(workspaceId, state);

            audit.Record(
                AuditAgents.System,
                "device-data-conflict.lock.release",
                $"{type}:{id}",
                $"device:{request.DeviceId:D}",
                AuditResults.Succeeded,
                workspaceId: workspaceId);

            return true;
        }
    }

    public DeviceDataConflictResult Apply(
        ApplyDeviceDataMutationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!request.ConfirmApply)
            throw new DeviceDataConflictValidationException(
                "Cần ConfirmApply=true trước khi áp dụng thay đổi dữ liệu.");

        var workspaceId = workspace.CurrentWorkspaceId;
        RequireDevice(workspaceId, request.DeviceId);
        var resourceType = NormalizeResourceType(request.ResourceType);
        var resourceId = NormalizeResourceId(request.ResourceId);
        var payloadHash = NormalizePayloadHash(request.PayloadHash);
        var policy = NormalizePolicy(request.Policy);

        if (request.BaseVersion < 0)
            throw new DeviceDataConflictValidationException(
                "BaseVersion không được âm.");

        if (request.ClientUpdatedAt > DateTimeOffset.UtcNow.AddMinutes(10))
            throw new DeviceDataConflictValidationException(
                "ClientUpdatedAt không được nằm quá xa trong tương lai.");

        lock (_gate)
        {
            var state = Load(workspaceId);
            CleanupExpiredLocks(state);

            var dataLock = state.Locks.FirstOrDefault(x =>
                x.ResourceType == resourceType
                && x.ResourceId == resourceId);

            var current = state.Versions.FirstOrDefault(x =>
                x.ResourceType == resourceType
                && x.ResourceId == resourceId)
                ?? new DeviceDataVersion(
                    workspaceId,
                    resourceType,
                    resourceId,
                    0,
                    DateTimeOffset.UnixEpoch,
                    request.DeviceId,
                    string.Empty);

            if (dataLock is not null
                && dataLock.DeviceId != request.DeviceId)
            {
                return RecordDecision(
                    state,
                    new DeviceDataConflictResult(
                        workspaceId,
                        resourceType,
                        resourceId,
                        policy,
                        DeviceDataConflictDecisions.RejectedLocked,
                        current.Version,
                        request.BaseVersion,
                        null,
                        current.UpdatedAt,
                        request.ClientUpdatedAt,
                        request.DeviceId,
                        dataLock.DeviceId,
                        "Resource đang bị device khác giữ khóa."));
            }

            var stale = request.BaseVersion != current.Version;
            if (stale && policy == DeviceDataConflictPolicies.RejectStale)
            {
                return RecordDecision(
                    state,
                    new DeviceDataConflictResult(
                        workspaceId,
                        resourceType,
                        resourceId,
                        policy,
                        DeviceDataConflictDecisions.RejectedStale,
                        current.Version,
                        request.BaseVersion,
                        null,
                        current.UpdatedAt,
                        request.ClientUpdatedAt,
                        request.DeviceId,
                        dataLock?.DeviceId,
                        "BaseVersion không còn khớp với phiên bản hiện tại."));
            }

            if (stale && policy == DeviceDataConflictPolicies.ManualReview)
            {
                return RecordDecision(
                    state,
                    new DeviceDataConflictResult(
                        workspaceId,
                        resourceType,
                        resourceId,
                        policy,
                        DeviceDataConflictDecisions.NeedsReview,
                        current.Version,
                        request.BaseVersion,
                        null,
                        current.UpdatedAt,
                        request.ClientUpdatedAt,
                        request.DeviceId,
                        dataLock?.DeviceId,
                        "Phát hiện xung đột phiên bản; policy yêu cầu người duyệt."));
            }

            if (stale
                && policy == DeviceDataConflictPolicies.LastWriteWins
                && request.ClientUpdatedAt <= current.UpdatedAt)
            {
                return RecordDecision(
                    state,
                    new DeviceDataConflictResult(
                        workspaceId,
                        resourceType,
                        resourceId,
                        policy,
                        DeviceDataConflictDecisions.RejectedStale,
                        current.Version,
                        request.BaseVersion,
                        null,
                        current.UpdatedAt,
                        request.ClientUpdatedAt,
                        request.DeviceId,
                        dataLock?.DeviceId,
                        "Timestamp phía client không mới hơn phiên bản server."));
            }

            var nextVersion = current.Version + 1;
            var updated = new DeviceDataVersion(
                workspaceId,
                resourceType,
                resourceId,
                nextVersion,
                request.ClientUpdatedAt,
                request.DeviceId,
                payloadHash);

            state.Versions.RemoveAll(x =>
                x.ResourceType == resourceType
                && x.ResourceId == resourceId);
            state.Versions.Add(updated);

            return RecordDecision(
                state,
                new DeviceDataConflictResult(
                    workspaceId,
                    resourceType,
                    resourceId,
                    policy,
                    DeviceDataConflictDecisions.Accepted,
                    current.Version,
                    request.BaseVersion,
                    nextVersion,
                    current.UpdatedAt,
                    request.ClientUpdatedAt,
                    request.DeviceId,
                    dataLock?.DeviceId,
                    stale
                        ? "Policy last-write-wins chấp nhận bản ghi mới hơn theo timestamp."
                        : "BaseVersion khớp; thay đổi được chấp nhận."));
        }
    }

    private DeviceDataConflictResult RecordDecision(
        ConflictState state,
        DeviceDataConflictResult result)
    {
        Save(result.WorkspaceId, state);

        audit.Record(
            AuditAgents.System,
            "device-data-conflict.resolve",
            $"{result.ResourceType}:{result.ResourceId}",
            $"device:{result.DeviceId:D};policy:{result.Policy};decision:{result.Decision};base:{result.BaseVersion};server:{result.ServerVersion};new:{result.NewVersion?.ToString() ?? "none"}",
            result.Decision == DeviceDataConflictDecisions.Accepted
                ? AuditResults.Succeeded
                : AuditResults.Denied,
            workspaceId: result.WorkspaceId);

        return result;
    }

    private void RequireDevice(string workspaceId, Guid deviceId)
    {
        _ = hub.ReconcileCompanionDevices(workspaceId)
            .FirstOrDefault(x => x.Id == deviceId)
            ?? throw new KeyNotFoundException(
                "Không tìm thấy device trong workspace hiện tại.");
    }

    private static string NormalizeResourceType(string? value)
    {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized.Length is < 1 or > MaximumResourceTypeCharacters)
            throw new DeviceDataConflictValidationException(
                $"ResourceType phải có từ 1 đến {MaximumResourceTypeCharacters} ký tự.");
        return normalized;
    }

    private static string NormalizeResourceId(string? value)
    {
        var normalized = (value ?? string.Empty).Trim();
        if (normalized.Length is < 1 or > MaximumResourceIdCharacters)
            throw new DeviceDataConflictValidationException(
                $"ResourceId phải có từ 1 đến {MaximumResourceIdCharacters} ký tự.");
        return normalized;
    }

    private static string NormalizePayloadHash(string? value)
    {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized.Length is < 8 or > MaximumPayloadHashCharacters)
            throw new DeviceDataConflictValidationException(
                $"PayloadHash phải có từ 8 đến {MaximumPayloadHashCharacters} ký tự.");
        return normalized;
    }

    private static string NormalizePolicy(string? value)
    {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        if (!DeviceDataConflictPolicies.All.Contains(normalized))
            throw new DeviceDataConflictValidationException(
                "Policy chỉ hỗ trợ reject-stale, last-write-wins hoặc manual-review.");
        return normalized;
    }

    private void CleanupExpiredLocks(ConflictState state)
    {
        var now = DateTimeOffset.UtcNow;
        state.Locks.RemoveAll(x => x.ExpiresAt <= now);
    }

    private ConflictState Load(string workspaceId)
    {
        var path = PathFor(workspaceId);
        if (!File.Exists(path))
            return new();

        try
        {
            return JsonSerializer.Deserialize<ConflictState>(
                File.ReadAllText(path),
                JsonOptions) ?? new();
        }
        catch (JsonException exception)
        {
            throw new DeviceDataConflictValidationException(
                $"Conflict state bị hỏng và bị fail-closed: {exception.Message}");
        }
    }

    private void Save(string workspaceId, ConflictState state)
    {
        Directory.CreateDirectory(_root);
        var path = PathFor(workspaceId);
        var temp = path + ".tmp";
        File.WriteAllText(
            temp,
            JsonSerializer.Serialize(state, JsonOptions));
        File.Move(temp, path, overwrite: true);
    }

    private string PathFor(string workspaceId) =>
        Path.Combine(
            _root,
            $"device-conflicts-{SafeFileName(workspaceId)}.json");

    private static string SafeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        return new string(
            value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
    }

    private static string ResolveRoot(IConfiguration configuration)
    {
        var configured = configuration["DeviceConflict:Root"];
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

        return Path.Combine(localData, "PersonalAI", "DeviceConflicts");
    }

    private sealed class ConflictState
    {
        public List<DeviceDataLock> Locks { get; set; } = [];
        public List<DeviceDataVersion> Versions { get; set; } = [];
    }
}
