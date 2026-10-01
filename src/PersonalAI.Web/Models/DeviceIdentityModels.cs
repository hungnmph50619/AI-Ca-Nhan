namespace PersonalAI.Web.Models;

public static class DeviceTrustLevels
{
    public const string Untrusted = "untrusted";
    public const string Trusted = "trusted";
}

public sealed record DeviceIdentity(
    Guid DeviceId,
    string WorkspaceId,
    string Name,
    string DeviceType,
    string PublicKeyAlgorithm,
    string PublicKey,
    string PublicKeyFingerprintSha256,
    string? PairedDevicePublicKey,
    string? PairedDeviceFingerprintSha256,
    IReadOnlyList<string> Capabilities,
    string TrustLevel,
    DateTimeOffset LastSeenAt,
    string ConnectionStatus,
    bool PermissionsGranted,
    bool RemoteExecutionEnabled,
    DateTimeOffset? TrustedAt,
    DateTimeOffset IdentityCreatedAt,
    DateTimeOffset IdentityUpdatedAt);

public sealed record DeviceIdentityStatus(
    string Version,
    string WorkspaceId,
    int Identities,
    bool StableDeviceId,
    bool PublicKeyAvailable,
    bool PrivateKeyEncrypted,
    bool PrivateKeyExportEnabled,
    bool AutomaticTrustElevationEnabled,
    bool AutomaticCapabilityGrantEnabled,
    string DefaultTrustLevel,
    string KeyAlgorithm);

public sealed class DeviceIdentityValidationException(string message)
    : Exception(message);
