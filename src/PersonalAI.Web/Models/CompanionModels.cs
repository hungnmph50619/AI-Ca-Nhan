namespace PersonalAI.Web.Models;

public static class CompanionCapabilities
{
    public const string Pairing = "pairing";
    public const string Chat = "chat";
    public const string TasksRead = "tasks-read";
    public const string CoreStatus = "core-status";
}

public sealed record CompanionStatusResponse(
    string Version,
    bool Enabled,
    bool SecureTransportRequired,
    int MaximumDevicesPerWorkspace,
    int PairingLifetimeMinutes,
    IReadOnlyList<string> Capabilities,
    IReadOnlyList<string> Limitations);

public sealed record CompanionPairingStartRequest(
    bool Confirmed = false);

public sealed record CompanionPairingTicket(
    string Code,
    string WorkspaceId,
    DateTimeOffset ExpiresAt);

public sealed record CompanionPairingClaimRequest(
    string Code,
    string DeviceName);

public sealed record CompanionDevice(
    Guid Id,
    string WorkspaceId,
    string Name,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastSeenAt);

public sealed record CompanionPairingClaimResponse(
    string Token,
    CompanionDevice Device);

public sealed record CompanionDeviceListResponse(
    string WorkspaceId,
    int MaximumDevices,
    IReadOnlyList<CompanionDevice> Devices);

public sealed record CompanionMeResponse(
    CompanionDevice Device,
    string Version,
    IReadOnlyList<string> Capabilities);

public sealed record CompanionCoreStatusResponse(
    string Version,
    string ApiContractVersion,
    string Channel,
    string WorkspaceId,
    string WorkspaceName,
    bool AiConfigured,
    string Provider,
    string Model);
