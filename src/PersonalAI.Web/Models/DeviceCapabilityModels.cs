namespace PersonalAI.Web.Models;

public static class DeviceCapabilityNames
{
    public const string Git = "git";
    public const string Build = "build";
    public const string Gpu = "gpu";
    public const string Gps = "gps";
    public const string Camera = "camera";
    public const string Notification = "notification";
    public const string Voice = "voice";

    public static readonly IReadOnlySet<string> All =
        new HashSet<string>(
            [Git, Build, Gpu, Gps, Camera, Notification, Voice],
            StringComparer.Ordinal);
}

public sealed record RegisterDeviceCapabilityRequest(
    string Capability,
    bool ConfirmRegister = false);

public sealed record SetDeviceCapabilityPermissionRequest(
    bool PermissionGranted,
    bool ConfirmPermissionChange = false);

public sealed record DeviceCapabilityRegistration(
    Guid Id,
    string WorkspaceId,
    Guid DeviceId,
    string Capability,
    bool Registered,
    bool PermissionGranted,
    DateTimeOffset RegisteredAt,
    DateTimeOffset? PermissionUpdatedAt,
    DateTimeOffset UpdatedAt);

public sealed record DeviceCapabilityAccessResult(
    Guid DeviceId,
    string Capability,
    bool Registered,
    bool PermissionGranted,
    bool Allowed,
    string Reason);

public sealed record DeviceCapabilityStatus(
    string Version,
    string WorkspaceId,
    int Registrations,
    int GrantedPermissions,
    bool RegistrationRequired,
    bool ExplicitPermissionRequired,
    bool UnregisteredInvocationBlocked,
    bool CompanionTrustRequiredForPermissionGrant,
    bool AutomaticPermissionGrantEnabled,
    IReadOnlyList<string> SupportedCapabilities);

public sealed class DeviceCapabilityValidationException(string message)
    : Exception(message);
