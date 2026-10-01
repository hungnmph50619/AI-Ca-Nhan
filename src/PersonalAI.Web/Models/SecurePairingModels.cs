namespace PersonalAI.Web.Models;

public sealed record SecurePairingTicket(
    string Code,
    string WorkspaceId,
    string Challenge,
    string HubPublicKey,
    string KeyAlgorithm,
    string QrPayload,
    DateTimeOffset ExpiresAt);

public sealed record SecurePairingClaimRequest(
    string Code,
    string DeviceName,
    string DevicePublicKey,
    string ProofSignature);

public sealed record SecurePairingClaimResponse(
    string Token,
    CompanionDevice Device,
    DeviceIdentity Identity,
    string HubPublicKey,
    string KeyAlgorithm,
    DateTimeOffset TrustedAt);

public sealed record SecurePairingStatus(
    string Version,
    bool Enabled,
    int PairingLifetimeMinutes,
    bool ChallengeSignatureRequired,
    bool PairingCodeSingleUse,
    bool ConsumedCodesPersisted,
    bool ReplayProtectionSurvivesRestart,
    bool TrustedOnlyAfterProof,
    string KeyAlgorithm);

public sealed class SecurePairingValidationException(string message)
    : Exception(message);
