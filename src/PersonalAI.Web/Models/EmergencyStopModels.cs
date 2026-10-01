namespace PersonalAI.Web.Models;

public sealed record EngageEmergencyStopRequest(
    string Reason,
    bool ConfirmStop = false);

public sealed record ReleaseEmergencyStopRequest(
    bool ConfirmRelease = false);

public sealed record EmergencyStopStatus(
    string Version,
    bool Engaged,
    DateTimeOffset? EngagedAt,
    string? EngagedByWorkspace,
    string? Reason,
    long Generation,
    bool CancellationIssued,
    bool DesktopControlPaused,
    bool NewExecutionBlocked,
    bool AuditPreserved,
    bool ExplicitReleaseRequired,
    IReadOnlyList<string> ProtectedSurfaces);

public sealed class EmergencyStopValidationException(string message)
    : Exception(message);

public sealed class EmergencyStopEngagedException(string message)
    : Exception(message);
