namespace PersonalAI.Web.Models;

public sealed record RoutePersonalTaskRequest(
    string RequiredCapability,
    bool ConfirmRoute = false);

public sealed record TaskRouteCandidate(
    Guid DeviceId,
    string DeviceName,
    string DeviceType,
    string Source,
    string TrustLevel,
    string ConnectionStatus,
    bool CapabilityRegistered,
    bool PermissionGranted,
    bool Eligible,
    string Reason,
    int TrustRank,
    int DeviceTypeRank);

public sealed record TaskRouteDecision(
    Guid Id,
    string WorkspaceId,
    Guid TaskId,
    string RequiredCapability,
    Guid? DeviceId,
    string? DeviceName,
    string? DeviceType,
    string Decision,
    string Reason,
    IReadOnlyList<TaskRouteCandidate> Candidates,
    DateTimeOffset RoutedAt);

public sealed record TaskRouterStatus(
    string Version,
    string WorkspaceId,
    bool DeterministicRouting,
    bool OfflineDevicesEligible,
    bool CapabilityPermissionRequired,
    bool CompanionTrustRequired,
    bool LocalAdminRegistrationAcceptedAsTrustBoundary,
    bool RouteDecisionPersisted,
    IReadOnlyList<string> StableTieBreakOrder);

public sealed class TaskRouterValidationException(string message)
    : Exception(message);
