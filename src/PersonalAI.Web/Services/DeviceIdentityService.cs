using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public interface IDeviceIdentityService
{
    DeviceIdentityStatus GetStatus(string workspaceId);
    IReadOnlyList<DeviceIdentity> GetAll(string workspaceId);
    DeviceIdentity? Get(string workspaceId, Guid deviceId);
    DeviceIdentity EnsureDevice(DeviceHubDevice device);
    DeviceIdentity EstablishTrust(
        DeviceHubDevice device,
        string pairedDevicePublicKey);
}

public sealed class DeviceIdentityService(
    IDeviceHubService hub,
    IDataProtectionProvider dataProtectionProvider,
    IConfiguration configuration,
    IAuditRecorder audit) : IDeviceIdentityService
{
    private const string KeyAlgorithm = "ECDSA-P256";
    private readonly object _gate = new();
    private readonly string _root = ResolveRoot(configuration);
    private readonly IDataProtector _protector =
        dataProtectionProvider.CreateProtector("PersonalAI.DeviceIdentity.v1");

    private static readonly JsonSerializerOptions Options =
        new(JsonSerializerDefaults.Web)
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

    public DeviceIdentityStatus GetStatus(string workspaceId)
    {
        var all = GetAll(workspaceId);
        return new(
            PersonalAiRelease.Version,
            NormalizeWorkspaceId(workspaceId),
            all.Count,
            StableDeviceId: true,
            PublicKeyAvailable: true,
            PrivateKeyEncrypted: true,
            PrivateKeyExportEnabled: false,
            AutomaticTrustElevationEnabled: false,
            AutomaticCapabilityGrantEnabled: false,
            DefaultTrustLevel: DeviceTrustLevels.Untrusted,
            KeyAlgorithm);
    }

    public IReadOnlyList<DeviceIdentity> GetAll(string workspaceId)
    {
        var normalized = NormalizeWorkspaceId(workspaceId);
        var devices = hub.ReconcileCompanionDevices(normalized);

        foreach (var device in devices)
            EnsureDevice(device);

        lock (_gate)
        {
            var stored = Load(normalized)
                .ToDictionary(x => x.DeviceId);

            return devices
                .Where(x => stored.ContainsKey(x.Id))
                .Select(x => ToPublic(stored[x.Id], x))
                .OrderByDescending(x => x.LastSeenAt)
                .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
    }

    public DeviceIdentity? Get(string workspaceId, Guid deviceId)
    {
        var normalized = NormalizeWorkspaceId(workspaceId);
        var device = hub.GetDevices(normalized)
            .FirstOrDefault(x => x.Id == deviceId);

        if (device is null)
            return null;

        return EnsureDevice(device);
    }

    public DeviceIdentity EnsureDevice(DeviceHubDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        var workspaceId = NormalizeWorkspaceId(device.WorkspaceId);

        lock (_gate)
        {
            var all = Load(workspaceId);
            var index = all.FindIndex(x => x.DeviceId == device.Id);

            if (index >= 0)
            {
                var current = all[index];
                ValidateEncryptedPrivateKey(current);

                var updated = current with
                {
                    UpdatedAt = DateTimeOffset.UtcNow
                };
                all[index] = updated;
                Save(workspaceId, all);
                return ToPublic(updated, device);
            }

            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var publicBytes = key.ExportSubjectPublicKeyInfo();
            var privateBytes = key.ExportPkcs8PrivateKey();

            try
            {
                var now = DateTimeOffset.UtcNow;
                var stored = new StoredDeviceIdentity(
                    device.Id,
                    workspaceId,
                    Convert.ToBase64String(publicBytes),
                    Sha256(publicBytes),
                    _protector.Protect(
                        Convert.ToBase64String(privateBytes)),
                    PairedDevicePublicKey: null,
                    PairedDeviceFingerprintSha256: null,
                    Capabilities: [],
                    DeviceTrustLevels.Untrusted,
                    TrustedAt: null,
                    now,
                    now);

                all.Add(stored);
                Save(workspaceId, all);

                audit.Record(
                    AuditAgents.System,
                    "device-identity.create",
                    $"device:{device.Id:D}",
                    $"algorithm:{KeyAlgorithm};trust:{DeviceTrustLevels.Untrusted};capabilities:0;private-key:encrypted",
                    AuditResults.Succeeded,
                    workspaceId: workspaceId);

                return ToPublic(stored, device);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(privateBytes);
            }
        }
    }

    public DeviceIdentity EstablishTrust(
        DeviceHubDevice device,
        string pairedDevicePublicKey)
    {
        ArgumentNullException.ThrowIfNull(device);
        var workspaceId = NormalizeWorkspaceId(device.WorkspaceId);
        var peerBytes = ValidatePublicKey(pairedDevicePublicKey);
        try
        {
            lock (_gate)
            {
                var all = Load(workspaceId);
                var index = all.FindIndex(x => x.DeviceId == device.Id);
                if (index < 0)
                {
                    _ = EnsureDevice(device);
                    all = Load(workspaceId);
                    index = all.FindIndex(x => x.DeviceId == device.Id);
                }

                if (index < 0)
                    throw new DeviceIdentityValidationException(
                        "Không tạo được identity trước khi trust.");

                var current = all[index];
                ValidateEncryptedPrivateKey(current);

                var fingerprint = Sha256(peerBytes);
                if (current.TrustLevel == DeviceTrustLevels.Trusted)
                {
                    if (!string.Equals(
                            current.PairedDeviceFingerprintSha256,
                            fingerprint,
                            StringComparison.Ordinal))
                    {
                        throw new DeviceIdentityValidationException(
                            "Thiết bị đã trusted với public key khác; từ chối thay key tự động.");
                    }

                    return ToPublic(current, device);
                }

                var now = DateTimeOffset.UtcNow;
                var updated = current with
                {
                    PairedDevicePublicKey = pairedDevicePublicKey.Trim(),
                    PairedDeviceFingerprintSha256 = fingerprint,
                    TrustLevel = DeviceTrustLevels.Trusted,
                    TrustedAt = now,
                    UpdatedAt = now
                };

                all[index] = updated;
                Save(workspaceId, all);

                audit.Record(
                    AuditAgents.System,
                    "device-identity.trust",
                    $"device:{device.Id:D}",
                    $"trust:{DeviceTrustLevels.Trusted};peer-fingerprint:{fingerprint};permissions-granted:false;remote-execution:false",
                    AuditResults.Succeeded,
                    workspaceId: workspaceId);

                return ToPublic(updated, device);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(peerBytes);
        }
    }

    private static byte[] ValidatePublicKey(string value)
    {
        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String((value ?? string.Empty).Trim());
            using var key = ECDsa.Create();
            key.ImportSubjectPublicKeyInfo(bytes, out var read);
            if (read != bytes.Length)
                throw new CryptographicException("Public key có dữ liệu dư.");
            return bytes;
        }
        catch (Exception exception) when (
            exception is FormatException or CryptographicException)
        {
            throw new DeviceIdentityValidationException(
                "Device public key không phải ECDSA P-256 SubjectPublicKeyInfo hợp lệ.");
        }
    }

    private void ValidateEncryptedPrivateKey(StoredDeviceIdentity stored)
    {
        try
        {
            var encoded = _protector.Unprotect(stored.EncryptedPrivateKey);
            var bytes = Convert.FromBase64String(encoded);
            try
            {
                using var key = ECDsa.Create();
                key.ImportPkcs8PrivateKey(bytes, out _);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(bytes);
            }
        }
        catch (Exception exception) when (
            exception is CryptographicException or FormatException)
        {
            throw new DeviceIdentityValidationException(
                "Encrypted device private key không thể xác minh; từ chối thay identity tự động.");
        }
    }

    private static DeviceIdentity ToPublic(
        StoredDeviceIdentity identity,
        DeviceHubDevice device) =>
        new(
            identity.DeviceId,
            identity.WorkspaceId,
            device.Name,
            device.DeviceType,
            KeyAlgorithm,
            identity.PublicKey,
            identity.PublicKeyFingerprintSha256,
            identity.PairedDevicePublicKey,
            identity.PairedDeviceFingerprintSha256,
            identity.Capabilities.ToArray(),
            identity.TrustLevel,
            device.LastSeenAt,
            device.ConnectionStatus,
            PermissionsGranted: false,
            RemoteExecutionEnabled: false,
            identity.TrustedAt,
            identity.CreatedAt,
            identity.UpdatedAt);

    private List<StoredDeviceIdentity> Load(string workspaceId)
    {
        var path = PathForWorkspace(workspaceId);
        if (!File.Exists(path)) return [];

        try
        {
            return JsonSerializer.Deserialize<List<StoredDeviceIdentity>>(
                File.ReadAllText(path),
                Options) ?? [];
        }
        catch (JsonException)
        {
            throw new DeviceIdentityValidationException(
                "Device identity state bị hỏng; từ chối tạo identity thay thế.");
        }
    }

    private void Save(
        string workspaceId,
        List<StoredDeviceIdentity> identities)
    {
        Directory.CreateDirectory(_root);
        var path = PathForWorkspace(workspaceId);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(identities, Options));
        File.Move(temp, path, true);
    }

    private string PathForWorkspace(string workspaceId)
    {
        var safe = string.Concat(workspaceId.Select(c =>
            char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_'));
        return Path.Combine(_root, $"device-identities-{safe}.json");
    }

    private static string NormalizeWorkspaceId(string? value)
    {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized.Length is < 1 or > 80)
            throw new DeviceIdentityValidationException(
                "WorkspaceId không hợp lệ.");
        return normalized;
    }

    private static string Sha256(byte[] value) =>
        Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();

    private static string ResolveRoot(IConfiguration configuration)
    {
        var root = configuration["DeviceIdentity:Root"];
        if (string.IsNullOrWhiteSpace(root))
        {
            root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PersonalAI",
                "DeviceIdentity");
        }

        root = Path.GetFullPath(Environment.ExpandEnvironmentVariables(root));
        Directory.CreateDirectory(root);
        return root;
    }

    private sealed record StoredDeviceIdentity(
        Guid DeviceId,
        string WorkspaceId,
        string PublicKey,
        string PublicKeyFingerprintSha256,
        string EncryptedPrivateKey,
        string? PairedDevicePublicKey,
        string? PairedDeviceFingerprintSha256,
        IReadOnlyList<string> Capabilities,
        string TrustLevel,
        DateTimeOffset? TrustedAt,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt);
}
