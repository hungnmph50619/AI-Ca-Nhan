namespace PersonalAI.Web.Models;

public static class DeviceHubDeviceTypes
{
    public const string Pc = "pc";
    public const string Laptop = "laptop";
    public const string Android = "android";
    public const string Other = "other";

    public static readonly IReadOnlySet<string> All =
        new HashSet<string>(
            [Pc, Laptop, Android, Other],
            StringComparer.Ordinal);
}

public static class DeviceHubConnectionStatuses
{
    public const string Online = "online";
    public const string Offline = "offline";
    public const string Unknown = "unknown";
}

public static class DeviceHubSources
{
    public const string LocalAdmin = "local-admin";
    public const string Companion = "companion";
}

public sealed record RegisterDeviceHubDeviceRequest(
    string Name,
    string DeviceType,
    bool ConfirmRegister = false);

public sealed record RecordDeviceHubHeartbeatRequest(
    bool ConfirmHeartbeat = false);

public sealed record DeviceHubDevice(
    Guid Id,
    string WorkspaceId,
    string Name,
    string DeviceType,
    string Source,
    Guid? CompanionDeviceId,
    string ConnectionStatus,
    bool PermissionsGranted,
    bool RemoteExecutionEnabled,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastSeenAt,
    DateTimeOffset UpdatedAt);

public sealed record DeviceHubWorkspaceStatus(
    string WorkspaceId,
    int TotalDevices,
    int OnlineDevices,
    int OfflineDevices,
    int UnknownDevices,
    IReadOnlyList<DeviceHubDevice> Devices);

public sealed record DeviceHubStatus(
    string Version,
    string WorkspaceId,
    int TotalDevices,
    int OnlineDevices,
    int OfflineDevices,
    int UnknownDevices,
    bool DeviceStatePersisted,
    bool CompanionReconciliationEnabled,
    bool AutomaticPermissionGrantEnabled,
    bool RemoteExecutionEnabled,
    int OnlineWindowMinutes,
    IReadOnlyList<string> SupportedDeviceTypes);

public sealed class DeviceHubValidationException(string message)
    : Exception(message);
