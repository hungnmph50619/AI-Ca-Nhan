using System.Text.Json;
using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public interface IDeviceHubService
{
    DeviceHubStatus GetStatus();
    DeviceHubWorkspaceStatus GetWorkspaceStatus(string workspaceId);
    IReadOnlyList<DeviceHubDevice> GetDevices(string workspaceId);
    DeviceHubDevice RegisterLocalDevice(
        string workspaceId,
        RegisterDeviceHubDeviceRequest request);
    DeviceHubDevice RecordCompanionHeartbeat(CompanionDevice companionDevice);
    IReadOnlyList<DeviceHubDevice> ReconcileCompanionDevices(string workspaceId);
}

public sealed class DeviceHubService(
    ICompanionService companion,
    IConfiguration configuration,
    IAuditRecorder audit) : IDeviceHubService
{
    public const int OnlineWindowMinutes = 5;
    public const int MaximumDevicesPerWorkspace = 32;
    public const int MaximumDeviceNameCharacters = 80;

    private readonly object _gate = new();
    private readonly string _root = ResolveRoot(configuration);
    private static readonly JsonSerializerOptions Options =
        new(JsonSerializerDefaults.Web)
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

    public DeviceHubStatus GetStatus()
    {
        var workspaceId = PersonalWorkspaceIds.Personal;
        var all = GetDevices(workspaceId);
        return BuildStatus(workspaceId, all);
    }

    public DeviceHubWorkspaceStatus GetWorkspaceStatus(string workspaceId)
    {
        var normalized = NormalizeWorkspaceId(workspaceId);
        var all = ReconcileCompanionDevices(normalized);
        return new(
            normalized,
            all.Count,
            all.Count(x => x.ConnectionStatus == DeviceHubConnectionStatuses.Online),
            all.Count(x => x.ConnectionStatus == DeviceHubConnectionStatuses.Offline),
            all.Count(x => x.ConnectionStatus == DeviceHubConnectionStatuses.Unknown),
            all);
    }

    public IReadOnlyList<DeviceHubDevice> GetDevices(string workspaceId)
    {
        var normalized = NormalizeWorkspaceId(workspaceId);
        lock (_gate)
        {
            var all = Load(normalized);
            var refreshed = all.Select(RefreshStatus).ToList();
            Save(normalized, refreshed);
            return refreshed
                .OrderByDescending(x => x.LastSeenAt)
                .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
    }

    public DeviceHubDevice RegisterLocalDevice(
        string workspaceId,
        RegisterDeviceHubDeviceRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!request.ConfirmRegister)
            throw new DeviceHubValidationException(
                "Cần ConfirmRegister=true để thêm thiết bị vào Device Hub.");

        var normalizedWorkspace = NormalizeWorkspaceId(workspaceId);
        var name = NormalizeName(request.Name);
        var type = NormalizeDeviceType(request.DeviceType);

        if (type == DeviceHubDeviceTypes.Android)
            throw new DeviceHubValidationException(
                "Android device phải đi qua Companion pairing hiện có; Device Hub không tự cấp quyền Android.");

        lock (_gate)
        {
            var all = Load(normalizedWorkspace);
            if (all.Count >= MaximumDevicesPerWorkspace)
                throw new DeviceHubValidationException(
                    $"Workspace đã đạt giới hạn {MaximumDevicesPerWorkspace} thiết bị hub.");

            var duplicate = all.FirstOrDefault(x =>
                x.Source == DeviceHubSources.LocalAdmin &&
                x.Name.Equals(name, StringComparison.OrdinalIgnoreCase) &&
                x.DeviceType == type);

            if (duplicate is not null)
                return RefreshStatus(duplicate);

            var now = DateTimeOffset.UtcNow;
            var device = new DeviceHubDevice(
                Guid.NewGuid(),
                normalizedWorkspace,
                name,
                type,
                DeviceHubSources.LocalAdmin,
                CompanionDeviceId: null,
                DeviceHubConnectionStatuses.Unknown,
                PermissionsGranted: false,
                RemoteExecutionEnabled: false,
                now,
                now,
                now);

            all.Add(device);
            Save(normalizedWorkspace, all);

            audit.Record(
                AuditAgents.User,
                "device-hub.device.register",
                $"device:{device.Id:D}",
                $"type:{type};source:{DeviceHubSources.LocalAdmin};permissions-granted:false;remote-execution:false",
                AuditResults.Prepared,
                workspaceId: normalizedWorkspace);

            return device;
        }
    }

    public DeviceHubDevice RecordCompanionHeartbeat(
        CompanionDevice companionDevice)
    {
        ArgumentNullException.ThrowIfNull(companionDevice);
        var workspaceId = NormalizeWorkspaceId(companionDevice.WorkspaceId);

        lock (_gate)
        {
            var all = Load(workspaceId);
            var index = all.FindIndex(x =>
                x.Source == DeviceHubSources.Companion &&
                x.CompanionDeviceId == companionDevice.Id);

            var now = DateTimeOffset.UtcNow;
            DeviceHubDevice device;
            if (index >= 0)
            {
                var current = all[index];
                device = current with
                {
                    Name = NormalizeName(companionDevice.Name),
                    DeviceType = DeviceHubDeviceTypes.Android,
                    ConnectionStatus = DeviceHubConnectionStatuses.Online,
                    PermissionsGranted = false,
                    RemoteExecutionEnabled = false,
                    LastSeenAt = now,
                    UpdatedAt = now
                };
                all[index] = device;
            }
            else
            {
                if (all.Count >= MaximumDevicesPerWorkspace)
                    throw new DeviceHubValidationException(
                        $"Workspace đã đạt giới hạn {MaximumDevicesPerWorkspace} thiết bị hub.");

                device = new DeviceHubDevice(
                    Guid.NewGuid(),
                    workspaceId,
                    NormalizeName(companionDevice.Name),
                    DeviceHubDeviceTypes.Android,
                    DeviceHubSources.Companion,
                    companionDevice.Id,
                    DeviceHubConnectionStatuses.Online,
                    PermissionsGranted: false,
                    RemoteExecutionEnabled: false,
                    companionDevice.CreatedAt,
                    now,
                    now);
                all.Add(device);
            }

            Save(workspaceId, all);
            return device;
        }
    }

    public IReadOnlyList<DeviceHubDevice> ReconcileCompanionDevices(
        string workspaceId)
    {
        var normalized = NormalizeWorkspaceId(workspaceId);
        var paired = companion.GetDevices(normalized).Devices;

        lock (_gate)
        {
            var all = Load(normalized);
            foreach (var pairedDevice in paired)
            {
                var index = all.FindIndex(x =>
                    x.Source == DeviceHubSources.Companion &&
                    x.CompanionDeviceId == pairedDevice.Id);

                var seen = pairedDevice.LastSeenAt;
                var status = IsOnline(seen)
                    ? DeviceHubConnectionStatuses.Online
                    : DeviceHubConnectionStatuses.Offline;

                if (index >= 0)
                {
                    var current = all[index];
                    all[index] = current with
                    {
                        Name = NormalizeName(pairedDevice.Name),
                        DeviceType = DeviceHubDeviceTypes.Android,
                        ConnectionStatus = status,
                        PermissionsGranted = false,
                        RemoteExecutionEnabled = false,
                        LastSeenAt = seen,
                        UpdatedAt = DateTimeOffset.UtcNow
                    };
                }
                else if (all.Count < MaximumDevicesPerWorkspace)
                {
                    all.Add(new DeviceHubDevice(
                        Guid.NewGuid(),
                        normalized,
                        NormalizeName(pairedDevice.Name),
                        DeviceHubDeviceTypes.Android,
                        DeviceHubSources.Companion,
                        pairedDevice.Id,
                        status,
                        PermissionsGranted: false,
                        RemoteExecutionEnabled: false,
                        pairedDevice.CreatedAt,
                        seen,
                        DateTimeOffset.UtcNow));
                }
            }

            var activeCompanionIds = paired
                .Select(x => x.Id)
                .ToHashSet();

            all = all
                .Where(x =>
                    x.Source != DeviceHubSources.Companion ||
                    (x.CompanionDeviceId is not null &&
                     activeCompanionIds.Contains(x.CompanionDeviceId.Value)))
                .Select(RefreshStatus)
                .ToList();

            Save(normalized, all);
            return all
                .OrderByDescending(x => x.LastSeenAt)
                .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
    }

    private DeviceHubStatus BuildStatus(
        string workspaceId,
        IReadOnlyList<DeviceHubDevice> devices) =>
        new(
            PersonalAiRelease.Version,
            workspaceId,
            devices.Count,
            devices.Count(x => x.ConnectionStatus == DeviceHubConnectionStatuses.Online),
            devices.Count(x => x.ConnectionStatus == DeviceHubConnectionStatuses.Offline),
            devices.Count(x => x.ConnectionStatus == DeviceHubConnectionStatuses.Unknown),
            DeviceStatePersisted: true,
            CompanionReconciliationEnabled: true,
            AutomaticPermissionGrantEnabled: false,
            RemoteExecutionEnabled: false,
            OnlineWindowMinutes,
            DeviceHubDeviceTypes.All.Order(StringComparer.Ordinal).ToArray());

    private static DeviceHubDevice RefreshStatus(DeviceHubDevice device)
    {
        if (device.Source == DeviceHubSources.LocalAdmin)
        {
            return device with
            {
                ConnectionStatus = DeviceHubConnectionStatuses.Unknown,
                PermissionsGranted = false,
                RemoteExecutionEnabled = false
            };
        }

        return device with
        {
            ConnectionStatus = IsOnline(device.LastSeenAt)
                ? DeviceHubConnectionStatuses.Online
                : DeviceHubConnectionStatuses.Offline,
            PermissionsGranted = false,
            RemoteExecutionEnabled = false
        };
    }

    private static bool IsOnline(DateTimeOffset lastSeenAt) =>
        DateTimeOffset.UtcNow - lastSeenAt <=
        TimeSpan.FromMinutes(OnlineWindowMinutes);

    private List<DeviceHubDevice> Load(string workspaceId)
    {
        var path = PathForWorkspace(workspaceId);
        if (!File.Exists(path)) return [];

        try
        {
            return JsonSerializer.Deserialize<List<DeviceHubDevice>>(
                File.ReadAllText(path),
                Options) ?? [];
        }
        catch (JsonException)
        {
            throw new DeviceHubValidationException(
                "Device Hub state bị hỏng; từ chối suy đoán trạng thái thiết bị.");
        }
    }

    private void Save(
        string workspaceId,
        List<DeviceHubDevice> devices)
    {
        Directory.CreateDirectory(_root);
        var path = PathForWorkspace(workspaceId);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(devices, Options));
        File.Move(temp, path, true);
    }

    private string PathForWorkspace(string workspaceId)
    {
        var safe = string.Concat(workspaceId.Select(c =>
            char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_'));
        return Path.Combine(_root, $"device-hub-{safe}.json");
    }

    private static string NormalizeWorkspaceId(string? value)
    {
        var workspaceId = (value ?? string.Empty)
            .Trim()
            .ToLowerInvariant();
        if (workspaceId.Length is < 1 or > 80)
            throw new DeviceHubValidationException(
                "WorkspaceId không hợp lệ.");
        return workspaceId;
    }

    private static string NormalizeName(string? value)
    {
        var name = string.Join(
            " ",
            (value ?? string.Empty)
                .Split(
                    [' ', '\t', '\r', '\n'],
                    StringSplitOptions.RemoveEmptyEntries));
        if (name.Length is < 2 or > MaximumDeviceNameCharacters)
            throw new DeviceHubValidationException(
                $"Tên thiết bị phải có từ 2 đến {MaximumDeviceNameCharacters} ký tự.");
        return name;
    }

    private static string NormalizeDeviceType(string? value)
    {
        var type = (value ?? string.Empty).Trim().ToLowerInvariant();
        if (!DeviceHubDeviceTypes.All.Contains(type))
            throw new DeviceHubValidationException(
                "DeviceType phải là pc, laptop, android hoặc other.");
        return type;
    }

    private static string ResolveRoot(IConfiguration configuration)
    {
        var root = configuration["DeviceHub:Root"];
        if (string.IsNullOrWhiteSpace(root))
        {
            root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PersonalAI",
                "DeviceHub");
        }

        root = Path.GetFullPath(Environment.ExpandEnvironmentVariables(root));
        Directory.CreateDirectory(root);
        return root;
    }
}
