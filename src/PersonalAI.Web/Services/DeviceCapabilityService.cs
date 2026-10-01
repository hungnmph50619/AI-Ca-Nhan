using System.Text.Json;
using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public interface IDeviceCapabilityService
{
    DeviceCapabilityStatus GetStatus(string workspaceId);
    IReadOnlyList<DeviceCapabilityRegistration> GetAll(
        string workspaceId,
        Guid deviceId);
    DeviceCapabilityRegistration Register(
        string workspaceId,
        Guid deviceId,
        RegisterDeviceCapabilityRequest request);
    DeviceCapabilityRegistration SetPermission(
        string workspaceId,
        Guid deviceId,
        string capability,
        SetDeviceCapabilityPermissionRequest request);
    DeviceCapabilityAccessResult CheckAccess(
        string workspaceId,
        Guid deviceId,
        string capability);
}

public sealed class DeviceCapabilityService(
    IDeviceHubService hub,
    IDeviceIdentityService identities,
    IUnifiedPermissionService permissions,
    IConfiguration configuration,
    IAuditRecorder audit) : IDeviceCapabilityService
{
    private readonly object _gate = new();
    private readonly string _root = ResolveRoot(configuration);

    private static readonly JsonSerializerOptions Options =
        new(JsonSerializerDefaults.Web)
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

    public DeviceCapabilityStatus GetStatus(string workspaceId)
    {
        var normalized = NormalizeWorkspaceId(workspaceId);
        var all = Load(normalized);

        return new(
            PersonalAiRelease.Version,
            normalized,
            all.Count(x => x.Registered),
            all.Count(x => x.Registered && x.PermissionGranted),
            RegistrationRequired: true,
            ExplicitPermissionRequired: true,
            UnregisteredInvocationBlocked: true,
            CompanionTrustRequiredForPermissionGrant: true,
            AutomaticPermissionGrantEnabled: false,
            DeviceCapabilityNames.All
                .Order(StringComparer.Ordinal)
                .ToArray());
    }

    public IReadOnlyList<DeviceCapabilityRegistration> GetAll(
        string workspaceId,
        Guid deviceId)
    {
        var normalized = NormalizeWorkspaceId(workspaceId);
        _ = RequireDevice(normalized, deviceId);

        lock (_gate)
        {
            return Load(normalized)
                .Where(x =>
                    x.DeviceId == deviceId &&
                    x.Registered)
                .OrderBy(x => x.Capability, StringComparer.Ordinal)
                .ToArray();
        }
    }

    public DeviceCapabilityRegistration Register(
        string workspaceId,
        Guid deviceId,
        RegisterDeviceCapabilityRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!request.ConfirmRegister)
            throw new DeviceCapabilityValidationException(
                "Cần ConfirmRegister=true để đăng ký capability cho thiết bị.");

        var normalized = NormalizeWorkspaceId(workspaceId);
        var capability = NormalizeCapability(request.Capability);
        var device = RequireDevice(normalized, deviceId);

        DeviceCapabilityRegistration registration;
        lock (_gate)
        {
            var all = Load(normalized);
            var index = all.FindIndex(x =>
                x.DeviceId == deviceId &&
                x.Capability == capability);

            var now = DateTimeOffset.UtcNow;
            if (index >= 0)
            {
                var current = all[index];
                registration = current with
                {
                    Registered = true,
                    PermissionGranted = current.Registered
                        ? current.PermissionGranted
                        : false,
                    RegisteredAt = current.Registered
                        ? current.RegisteredAt
                        : now,
                    PermissionUpdatedAt = current.Registered
                        ? current.PermissionUpdatedAt
                        : null,
                    UpdatedAt = now
                };
                all[index] = registration;
            }
            else
            {
                registration = new(
                    Guid.NewGuid(),
                    normalized,
                    deviceId,
                    capability,
                    Registered: true,
                    PermissionGranted: false,
                    now,
                    PermissionUpdatedAt: null,
                    now);
                all.Add(registration);
            }

            Save(normalized, all);
            SyncIdentityCapabilities(device, all);
        }

        audit.Record(
            AuditAgents.User,
            "device-capability.register",
            $"device:{deviceId:D}",
            $"capability:{capability};permission-granted:{registration.PermissionGranted}",
            AuditResults.Prepared,
            workspaceId: normalized);

        return registration;
    }

    public DeviceCapabilityRegistration SetPermission(
        string workspaceId,
        Guid deviceId,
        string capability,
        SetDeviceCapabilityPermissionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!request.ConfirmPermissionChange)
            throw new DeviceCapabilityValidationException(
                "Cần ConfirmPermissionChange=true để thay đổi permission capability.");

        var normalized = NormalizeWorkspaceId(workspaceId);
        var normalizedCapability = NormalizeCapability(capability);
        var device = RequireDevice(normalized, deviceId);

        if (request.PermissionGranted &&
            device.Source == DeviceHubSources.Companion)
        {
            var identity = identities.Get(normalized, deviceId)
                ?? throw new DeviceCapabilityValidationException(
                    "Companion device chưa có identity.");

            if (identity.TrustLevel != DeviceTrustLevels.Trusted)
                throw new DeviceCapabilityValidationException(
                    "Companion device phải secure-paired và trusted trước khi được cấp capability permission.");
        }

        DeviceCapabilityRegistration updated;
        lock (_gate)
        {
            var all = Load(normalized);
            var index = all.FindIndex(x =>
                x.DeviceId == deviceId &&
                x.Capability == normalizedCapability &&
                x.Registered);

            if (index < 0)
                throw new DeviceCapabilityValidationException(
                    "Capability chưa được đăng ký cho thiết bị; không thể cấp permission.");

            var now = DateTimeOffset.UtcNow;
            updated = all[index] with
            {
                PermissionGranted = request.PermissionGranted,
                PermissionUpdatedAt = now,
                UpdatedAt = now
            };

            all[index] = updated;
            Save(normalized, all);
        }

        _ = permissions.Set(
            new SetUnifiedPermissionRequest(
                UnifiedPermissionSubjectTypes.Device,
                deviceId.ToString("D"),
                $"capability:{normalizedCapability}",
                "use",
                updated.PermissionGranted
                    ? UnifiedPermissionEffects.Allow
                    : UnifiedPermissionEffects.Deny,
                ExpiresAt: null,
                ConfirmChange: true));

        audit.Record(
            AuditAgents.User,
            "device-capability.permission",
            $"device:{deviceId:D}",
            $"capability:{normalizedCapability};permission-granted:{updated.PermissionGranted}",
            AuditResults.Succeeded,
            workspaceId: normalized);

        return updated;
    }

    public DeviceCapabilityAccessResult CheckAccess(
        string workspaceId,
        Guid deviceId,
        string capability)
    {
        var normalized = NormalizeWorkspaceId(workspaceId);
        var normalizedCapability = NormalizeCapability(capability);
        _ = RequireDevice(normalized, deviceId);

        lock (_gate)
        {
            var registration = Load(normalized)
                .FirstOrDefault(x =>
                    x.DeviceId == deviceId &&
                    x.Capability == normalizedCapability &&
                    x.Registered);

            if (registration is null)
            {
                return new(
                    deviceId,
                    normalizedCapability,
                    Registered: false,
                    PermissionGranted: false,
                    Allowed: false,
                    Reason: "capability-not-registered");
            }

            if (!registration.PermissionGranted)
            {
                return new(
                    deviceId,
                    normalizedCapability,
                    Registered: true,
                    PermissionGranted: false,
                    Allowed: false,
                    Reason: "permission-not-granted");
            }

            var unifiedDecision = permissions.Evaluate(
                new EvaluateUnifiedPermissionRequest(
                    UnifiedPermissionSubjectTypes.Device,
                    deviceId.ToString("D"),
                    $"capability:{normalizedCapability}",
                    "use"));

            if (unifiedDecision.MatchedRuleId is not null)
            {
                return new(
                    deviceId,
                    normalizedCapability,
                    Registered: true,
                    PermissionGranted: registration.PermissionGranted,
                    Allowed: unifiedDecision.Allowed,
                    Reason: unifiedDecision.Reason);
            }

            return new(
                deviceId,
                normalizedCapability,
                Registered: true,
                PermissionGranted: registration.PermissionGranted,
                Allowed: registration.PermissionGranted,
                Reason: registration.PermissionGranted
                    ? "legacy-permission-metadata-fallback"
                    : "permission-not-granted");
        }
    }

    private DeviceHubDevice RequireDevice(
        string workspaceId,
        Guid deviceId)
    {
        var device = hub.ReconcileCompanionDevices(workspaceId)
            .FirstOrDefault(x => x.Id == deviceId);

        if (device is null)
            throw new KeyNotFoundException(
                "Không tìm thấy thiết bị trong workspace hiện tại.");

        return device;
    }

    private void SyncIdentityCapabilities(
        DeviceHubDevice device,
        IReadOnlyList<DeviceCapabilityRegistration> registrations)
    {
        var capabilities = registrations
            .Where(x =>
                x.DeviceId == device.Id &&
                x.Registered)
            .Select(x => x.Capability)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        _ = identities.SetCapabilitiesMetadata(
            device,
            capabilities);
    }

    private List<DeviceCapabilityRegistration> Load(string workspaceId)
    {
        var path = PathForWorkspace(workspaceId);
        if (!File.Exists(path)) return [];

        try
        {
            return JsonSerializer.Deserialize<List<DeviceCapabilityRegistration>>(
                File.ReadAllText(path),
                Options) ?? [];
        }
        catch (JsonException)
        {
            throw new DeviceCapabilityValidationException(
                "Device capability state bị hỏng; từ chối suy đoán permission.");
        }
    }

    private void Save(
        string workspaceId,
        List<DeviceCapabilityRegistration> registrations)
    {
        Directory.CreateDirectory(_root);
        var path = PathForWorkspace(workspaceId);
        var temp = path + ".tmp";
        File.WriteAllText(
            temp,
            JsonSerializer.Serialize(registrations, Options));
        File.Move(temp, path, true);
    }

    private string PathForWorkspace(string workspaceId)
    {
        var safe = string.Concat(workspaceId.Select(c =>
            char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_'));
        return Path.Combine(
            _root,
            $"device-capabilities-{safe}.json");
    }

    private static string NormalizeWorkspaceId(string? value)
    {
        var normalized = (value ?? string.Empty)
            .Trim()
            .ToLowerInvariant();

        if (normalized.Length is < 1 or > 80)
            throw new DeviceCapabilityValidationException(
                "WorkspaceId không hợp lệ.");

        return normalized;
    }

    private static string NormalizeCapability(string? value)
    {
        var capability = (value ?? string.Empty)
            .Trim()
            .ToLowerInvariant();

        if (!DeviceCapabilityNames.All.Contains(capability))
            throw new DeviceCapabilityValidationException(
                "Capability phải là git, build, gpu, gps, camera, notification hoặc voice.");

        return capability;
    }

    private static string ResolveRoot(IConfiguration configuration)
    {
        var root = configuration["DeviceCapability:Root"];
        if (string.IsNullOrWhiteSpace(root))
        {
            root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PersonalAI",
                "DeviceCapability");
        }

        root = Path.GetFullPath(
            Environment.ExpandEnvironmentVariables(root));
        Directory.CreateDirectory(root);
        return root;
    }
}
