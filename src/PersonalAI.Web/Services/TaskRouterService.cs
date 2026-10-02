using System.Text.Json;
using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public interface ITaskRouterService
{
    TaskRouterStatus GetStatus();
    IReadOnlyList<TaskRouteDecision> GetRoutes();
    TaskRouteDecision? GetLatest(Guid taskId);
    TaskRouteDecision Route(
        Guid taskId,
        RoutePersonalTaskRequest request);
}

public sealed class TaskRouterService(
    ITaskEngineService tasks,
    IDeviceHubService hub,
    IDeviceIdentityService identities,
    IDeviceCapabilityService capabilities,
    IWorkspaceContextAccessor workspace,
    IConfiguration configuration,
    IAuditRecorder audit) : ITaskRouterService
{
    private readonly object _gate = new();
    private readonly string _root = ResolveRoot(configuration);

    private static readonly JsonSerializerOptions Options =
        new(JsonSerializerDefaults.Web)
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

    public TaskRouterStatus GetStatus() =>
        new(
            PersonalAiRelease.Version,
            workspace.CurrentWorkspaceId,
            DeterministicRouting: true,
            OfflineDevicesEligible: false,
            CapabilityPermissionRequired: true,
            CompanionTrustRequired: true,
            LocalAdminRegistrationAcceptedAsTrustBoundary: true,
            RouteDecisionPersisted: true,
            [
                "trust-rank",
                "device-type-rank",
                "stable-device-id"
            ]);

    public IReadOnlyList<TaskRouteDecision> GetRoutes()
    {
        lock (_gate)
            return Load()
                .OrderByDescending(x => x.RoutedAt)
                .ToArray();
    }

    public TaskRouteDecision? GetLatest(Guid taskId)
    {
        lock (_gate)
            return Load()
                .Where(x => x.TaskId == taskId)
                .OrderByDescending(x => x.RoutedAt)
                .FirstOrDefault();
    }

    public TaskRouteDecision Route(
        Guid taskId,
        RoutePersonalTaskRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!request.ConfirmRoute)
            throw new TaskRouterValidationException(
                "Cần ConfirmRoute=true để persist quyết định routing.");

        var task = tasks.Get(taskId)
            ?? throw new KeyNotFoundException(
                "Không tìm thấy tác vụ.");

        if (task.WorkspaceId != workspace.CurrentWorkspaceId)
            throw new TaskRouterValidationException(
                "Tác vụ không thuộc workspace hiện tại.");

        if (task.Status is PersonalTaskStatuses.Completed
            or PersonalTaskStatuses.Cancelled
            or PersonalTaskStatuses.Failed)
        {
            throw new TaskRouterValidationException(
                "Không route tác vụ đã kết thúc.");
        }

        var requiredCapability =
            NormalizeCapability(request.RequiredCapability);
        var devices = hub.ReconcileCompanionDevices(
            workspace.CurrentWorkspaceId);

        var candidates = devices
            .Select(device => BuildCandidate(
                device,
                requiredCapability))
            .OrderBy(x => x.TrustRank)
            .ThenBy(x => x.DeviceTypeRank)
            .ThenBy(x => x.DeviceId.ToString("N"), StringComparer.Ordinal)
            .ToArray();

        var selected = candidates.FirstOrDefault(x => x.Eligible);
        var now = DateTimeOffset.UtcNow;

        var decision = selected is null
            ? new TaskRouteDecision(
                Guid.NewGuid(),
                workspace.CurrentWorkspaceId,
                task.Id,
                requiredCapability,
                DeviceId: null,
                DeviceName: null,
                DeviceType: null,
                Decision: "unroutable",
                Reason: ResolveBlockedReason(candidates),
                candidates,
                now)
            : new TaskRouteDecision(
                Guid.NewGuid(),
                workspace.CurrentWorkspaceId,
                task.Id,
                requiredCapability,
                selected.DeviceId,
                selected.DeviceName,
                selected.DeviceType,
                Decision: "routed",
                Reason: "deterministic-eligible-device-selected",
                candidates,
                now);

        lock (_gate)
        {
            var all = Load();
            all.Add(decision);
            Save(all);
        }

        audit.Record(
            AuditAgents.TaskEngine,
            "task.device.route",
            $"task:{task.Id:D}",
            selected is null
                ? $"capability:{requiredCapability};decision:unroutable;reason:{decision.Reason}"
                : $"capability:{requiredCapability};decision:routed;device:{selected.DeviceId:D};trust-rank:{selected.TrustRank};type-rank:{selected.DeviceTypeRank}",
            selected is null
                ? AuditResults.Denied
                : AuditResults.Prepared,
            workspaceId: workspace.CurrentWorkspaceId);

        return decision;
    }

    private TaskRouteCandidate BuildCandidate(
        DeviceHubDevice device,
        string requiredCapability)
    {
        var access = capabilities.CheckAccess(
            device.WorkspaceId,
            device.Id,
            requiredCapability);

        var identity = identities.Get(
            device.WorkspaceId,
            device.Id);

        var trusted = device.Source == DeviceHubSources.LocalAdmin
            || identity?.TrustLevel == DeviceTrustLevels.Trusted;

        var online = device.ConnectionStatus ==
            DeviceHubConnectionStatuses.Online;

        var eligible =
            online &&
            trusted &&
            access.Registered &&
            access.PermissionGranted &&
            access.Allowed;

        var reason = !online
            ? "device-offline"
            : !trusted
                ? "device-not-trusted"
                : !access.Registered
                    ? "capability-not-registered"
                    : !access.PermissionGranted
                        ? "permission-not-granted"
                        : access.Allowed
                            ? "eligible"
                            : access.Reason;

        return new(
            device.Id,
            device.Name,
            device.DeviceType,
            device.Source,
            identity?.TrustLevel ?? DeviceTrustLevels.Untrusted,
            device.ConnectionStatus,
            access.Registered,
            access.PermissionGranted,
            eligible,
            reason,
            TrustRank(device, identity),
            DeviceTypeRank(device.DeviceType));
    }

    private static int TrustRank(
        DeviceHubDevice device,
        DeviceIdentity? identity)
    {
        if (device.Source == DeviceHubSources.LocalAdmin)
            return 0;

        if (identity?.TrustLevel == DeviceTrustLevels.Trusted)
            return 1;

        return 9;
    }

    private static int DeviceTypeRank(string deviceType) =>
        deviceType switch
        {
            DeviceHubDeviceTypes.Pc => 0,
            DeviceHubDeviceTypes.Laptop => 1,
            DeviceHubDeviceTypes.Android => 2,
            _ => 3
        };

    private static string ResolveBlockedReason(
        IReadOnlyList<TaskRouteCandidate> candidates)
    {
        if (candidates.Count == 0)
            return "no-devices";

        if (candidates.All(x =>
                x.ConnectionStatus !=
                DeviceHubConnectionStatuses.Online))
            return "no-online-devices";

        if (candidates.All(x => !x.CapabilityRegistered))
            return "capability-not-registered";

        if (candidates.All(x => !x.PermissionGranted))
            return "permission-not-granted";

        if (candidates.All(x =>
                x.Source == DeviceHubSources.Companion &&
                x.TrustLevel != DeviceTrustLevels.Trusted))
            return "no-trusted-device";

        return "no-eligible-device";
    }

    private List<TaskRouteDecision> Load()
    {
        var path = PathForWorkspace();
        if (!File.Exists(path)) return [];

        try
        {
            return JsonSerializer.Deserialize<List<TaskRouteDecision>>(
                File.ReadAllText(path),
                Options) ?? [];
        }
        catch (JsonException)
        {
            throw new TaskRouterValidationException(
                "Task Router state bị hỏng; từ chối suy đoán routing.");
        }
    }

    private void Save(List<TaskRouteDecision> routes)
    {
        Directory.CreateDirectory(_root);
        var path = PathForWorkspace();
        var temp = path + ".tmp";
        File.WriteAllText(
            temp,
            JsonSerializer.Serialize(routes, Options));
        File.Move(temp, path, true);
    }

    private string PathForWorkspace()
    {
        var safe = string.Concat(
            workspace.CurrentWorkspaceId.Select(c =>
                char.IsLetterOrDigit(c) || c is '-' or '_'
                    ? c
                    : '_'));

        return Path.Combine(
            _root,
            $"task-routes-{safe}.json");
    }

    private static string NormalizeCapability(string? value)
    {
        var capability = (value ?? string.Empty)
            .Trim()
            .ToLowerInvariant();

        if (!DeviceCapabilityNames.All.Contains(capability))
            throw new TaskRouterValidationException(
                "RequiredCapability phải là git, build, gpu, gps, camera, notification hoặc voice.");

        return capability;
    }

    private static string ResolveRoot(IConfiguration configuration)
    {
        var root = configuration["TaskRouter:Root"];
        if (string.IsNullOrWhiteSpace(root))
        {
            root = Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData),
                "PersonalAI",
                "TaskRouter");
        }

        root = Path.GetFullPath(
            Environment.ExpandEnvironmentVariables(root));
        Directory.CreateDirectory(root);
        return root;
    }
}
