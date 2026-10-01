using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public interface ISecurePairingService
{
    SecurePairingStatus GetStatus();
    SecurePairingTicket Start(string workspaceId);
    SecurePairingClaimResponse Claim(SecurePairingClaimRequest request);
}

public sealed class SecurePairingService(
    ICompanionService companion,
    IDeviceHubService hub,
    IDeviceIdentityService identities,
    IDataProtectionProvider dataProtectionProvider,
    IConfiguration configuration,
    IAuditRecorder audit) : ISecurePairingService
{
    public const int DefaultLifetimeSeconds = 300;
    public const int MaximumLifetimeSeconds = 600;
    private const string PairingAlphabet = "23456789ABCDEFGHJKLMNPQRSTUVWXYZ";
    private const int CodeLength = 12;
    private const string Algorithm = "ECDSA-P256";

    private readonly object _gate = new();
    private readonly string _root = ResolveRoot(configuration);
    private readonly IDataProtector _protector =
        dataProtectionProvider.CreateProtector("PersonalAI.SecurePairing.v1");
    private readonly int _lifetimeSeconds = Math.Clamp(
        configuration.GetValue("SecurePairing:LifetimeSeconds", DefaultLifetimeSeconds),
        1,
        MaximumLifetimeSeconds);

    private static readonly JsonSerializerOptions Options =
        new(JsonSerializerDefaults.Web)
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

    public SecurePairingStatus GetStatus() =>
        new(
            PersonalAiRelease.Version,
            companion.Enabled,
            _lifetimeSeconds,
            ChallengeSignatureRequired: true,
            PairingCodeSingleUse: true,
            ConsumedCodesPersisted: true,
            ReplayProtectionSurvivesRestart: true,
            TrustedOnlyAfterProof: true,
            Algorithm);

    public SecurePairingTicket Start(string workspaceId)
    {
        if (!companion.Enabled)
            throw new CompanionDisabledException(
                "Android Companion đang bị tắt.");

        var normalizedWorkspace = NormalizeWorkspaceId(workspaceId);
        var now = DateTimeOffset.UtcNow;
        var code = CreateCode();
        var challengeBytes = RandomNumberGenerator.GetBytes(32);
        var challenge = Base64Url(challengeBytes);

        using var hubKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var publicBytes = hubKey.ExportSubjectPublicKeyInfo();
        var privateBytes = hubKey.ExportPkcs8PrivateKey();

        try
        {
            var hubPublicKey = Convert.ToBase64String(publicBytes);
            var record = new StoredPairing(
                Guid.NewGuid(),
                normalizedWorkspace,
                HashCode(code),
                challenge,
                hubPublicKey,
                _protector.Protect(Convert.ToBase64String(privateBytes)),
                now,
                now.AddSeconds(_lifetimeSeconds),
                ConsumedAt: null,
                ClaimedDeviceId: null);

            lock (_gate)
            {
                var state = Load();
                state.RemoveAll(x =>
                    x.WorkspaceId == normalizedWorkspace &&
                    x.ConsumedAt is null &&
                    x.ExpiresAt <= now);
                state.Add(record);
                Trim(state);
                Save(state);
            }

            var qrObject = JsonSerializer.Serialize(new
            {
                v = 1,
                code,
                challenge,
                hubPublicKey,
                algorithm = Algorithm,
                expiresAt = record.ExpiresAt
            });
            var qrPayload =
                "personalai://pair/" +
                Base64Url(Encoding.UTF8.GetBytes(qrObject));

            audit.Record(
                AuditAgents.User,
                "secure-pairing.start",
                $"pairing:{record.Id:D}",
                $"workspace:{normalizedWorkspace};expires:{record.ExpiresAt:O};code:hashed;hub-private-key:encrypted",
                AuditResults.Prepared,
                workspaceId: normalizedWorkspace);

            return new(
                code,
                normalizedWorkspace,
                challenge,
                hubPublicKey,
                Algorithm,
                qrPayload,
                record.ExpiresAt);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(privateBytes);
            CryptographicOperations.ZeroMemory(challengeBytes);
        }
    }

    public SecurePairingClaimResponse Claim(
        SecurePairingClaimRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var code = NormalizeCode(request.Code);
        var codeHash = HashCode(code);
        var now = DateTimeOffset.UtcNow;

        StoredPairing pairing;
        lock (_gate)
        {
            var state = Load();
            pairing = state.FirstOrDefault(x =>
                FixedEqualsHex(x.CodeSha256, codeHash))
                ?? throw new SecurePairingValidationException(
                    "Mã ghép nối không hợp lệ.");

            if (pairing.ConsumedAt is not null)
                throw new SecurePairingValidationException(
                    "Mã ghép nối đã được sử dụng; replay bị từ chối.");

            if (pairing.ExpiresAt <= now)
                throw new SecurePairingValidationException(
                    "Mã ghép nối đã hết hạn.");

            ValidateHubKey(pairing);
            VerifyDeviceProof(pairing, code, request);

            var index = state.FindIndex(x => x.Id == pairing.Id);
            pairing = pairing with { ConsumedAt = now };
            state[index] = pairing;
            Save(state);
        }

        var created = companion.CreateSecurePairedDevice(
            pairing.WorkspaceId,
            request.DeviceName);

        var hubDevice = hub.RecordCompanionHeartbeat(created.Device);
        var identity = identities.EstablishTrust(
            hubDevice,
            request.DevicePublicKey);

        lock (_gate)
        {
            var state = Load();
            var index = state.FindIndex(x => x.Id == pairing.Id);
            if (index >= 0)
            {
                state[index] = state[index] with
                {
                    ClaimedDeviceId = created.Device.Id
                };
                Save(state);
            }
        }

        audit.Record(
            AuditAgents.Companion,
            "secure-pairing.claim",
            $"device:{created.Device.Id:D}",
            $"pairing:{pairing.Id:D};trust:trusted;proof:ecdsa-verified;replay-protected:true;permissions-granted:false",
            AuditResults.Succeeded,
            workspaceId: pairing.WorkspaceId);

        return new(
            created.Token,
            created.Device,
            identity,
            pairing.HubPublicKey,
            Algorithm,
            identity.TrustedAt ?? now);
    }

    private void VerifyDeviceProof(
        StoredPairing pairing,
        string code,
        SecurePairingClaimRequest request)
    {
        byte[] publicBytes;
        byte[] signature;
        try
        {
            publicBytes = Convert.FromBase64String(
                (request.DevicePublicKey ?? string.Empty).Trim());
            signature = Convert.FromBase64String(
                (request.ProofSignature ?? string.Empty).Trim());
        }
        catch (FormatException)
        {
            throw new SecurePairingValidationException(
                "Device public key hoặc proof signature không hợp lệ.");
        }

        try
        {
            using var key = ECDsa.Create();
            key.ImportSubjectPublicKeyInfo(publicBytes, out var read);
            if (read != publicBytes.Length || key.KeySize != 256)
                throw new SecurePairingValidationException(
                    "Device public key phải là ECDSA P-256.");

            var proof = ProofBytes(
                code,
                pairing.Challenge,
                pairing.HubPublicKey);

            if (!key.VerifyData(
                    proof,
                    signature,
                    HashAlgorithmName.SHA256))
            {
                throw new SecurePairingValidationException(
                    "Chữ ký challenge không hợp lệ.");
            }
        }
        catch (CryptographicException)
        {
            throw new SecurePairingValidationException(
                "Device public key hoặc chữ ký không hợp lệ.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(publicBytes);
            CryptographicOperations.ZeroMemory(signature);
        }
    }

    private void ValidateHubKey(StoredPairing pairing)
    {
        try
        {
            var encoded = _protector.Unprotect(
                pairing.EncryptedHubPrivateKey);
            var privateBytes = Convert.FromBase64String(encoded);
            try
            {
                using var key = ECDsa.Create();
                key.ImportPkcs8PrivateKey(privateBytes, out _);
                var publicKey = Convert.ToBase64String(
                    key.ExportSubjectPublicKeyInfo());

                if (!string.Equals(
                        publicKey,
                        pairing.HubPublicKey,
                        StringComparison.Ordinal))
                {
                    throw new SecurePairingValidationException(
                        "Hub pairing key không khớp persisted ticket.");
                }
            }
            finally
            {
                CryptographicOperations.ZeroMemory(privateBytes);
            }
        }
        catch (CryptographicException)
        {
            throw new SecurePairingValidationException(
                "Hub pairing private key không giải mã được.");
        }
    }

    private static byte[] ProofBytes(
        string code,
        string challenge,
        string hubPublicKey) =>
        Encoding.UTF8.GetBytes(
            $"PersonalAI-SecurePairing-v1\n{code}\n{challenge}\n{hubPublicKey}");

    private List<StoredPairing> Load()
    {
        var path = Path.Combine(_root, "secure-pairings.json");
        if (!File.Exists(path)) return [];

        try
        {
            return JsonSerializer.Deserialize<List<StoredPairing>>(
                File.ReadAllText(path),
                Options) ?? [];
        }
        catch (JsonException)
        {
            throw new SecurePairingValidationException(
                "Secure pairing state bị hỏng; từ chối pairing.");
        }
    }

    private void Save(List<StoredPairing> state)
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "secure-pairings.json");
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(state, Options));
        File.Move(temp, path, true);
    }

    private static void Trim(List<StoredPairing> state)
    {
        if (state.Count <= 1000) return;
        var keep = state
            .OrderByDescending(x => x.CreatedAt)
            .Take(1000)
            .OrderBy(x => x.CreatedAt)
            .ToList();
        state.Clear();
        state.AddRange(keep);
    }

    private static string CreateCode()
    {
        Span<char> chars = stackalloc char[CodeLength];
        for (var i = 0; i < chars.Length; i++)
            chars[i] = PairingAlphabet[
                RandomNumberGenerator.GetInt32(PairingAlphabet.Length)];
        return new string(chars);
    }

    private static string NormalizeCode(string? value)
    {
        var code = (value ?? string.Empty)
            .Trim()
            .Replace("-", string.Empty)
            .Replace(" ", string.Empty)
            .ToUpperInvariant();

        if (code.Length != CodeLength ||
            code.Any(c => PairingAlphabet.IndexOf(c) < 0))
        {
            throw new SecurePairingValidationException(
                "Mã ghép nối không hợp lệ.");
        }

        return code;
    }

    private static string HashCode(string code) =>
        Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(code)))
        .ToLowerInvariant();

    private static bool FixedEqualsHex(string left, string right)
    {
        try
        {
            var a = Convert.FromHexString(left);
            var b = Convert.FromHexString(right);
            return a.Length == b.Length &&
                   CryptographicOperations.FixedTimeEquals(a, b);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private static string NormalizeWorkspaceId(string? value)
    {
        var workspaceId = (value ?? string.Empty)
            .Trim()
            .ToLowerInvariant();
        if (workspaceId.Length is < 1 or > 80)
            throw new SecurePairingValidationException(
                "WorkspaceId không hợp lệ.");
        return workspaceId;
    }

    private static string ResolveRoot(IConfiguration configuration)
    {
        var root = configuration["SecurePairing:Root"];
        if (string.IsNullOrWhiteSpace(root))
        {
            root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PersonalAI",
                "SecurePairing");
        }

        root = Path.GetFullPath(
            Environment.ExpandEnvironmentVariables(root));
        Directory.CreateDirectory(root);
        return root;
    }

    private sealed record StoredPairing(
        Guid Id,
        string WorkspaceId,
        string CodeSha256,
        string Challenge,
        string HubPublicKey,
        string EncryptedHubPrivateKey,
        DateTimeOffset CreatedAt,
        DateTimeOffset ExpiresAt,
        DateTimeOffset? ConsumedAt,
        Guid? ClaimedDeviceId);
}
