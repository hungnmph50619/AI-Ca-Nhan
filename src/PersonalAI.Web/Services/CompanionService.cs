using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public interface ICompanionService
{
    bool Enabled { get; }

    bool AllowInsecureHttp { get; }

    CompanionStatusResponse GetStatus();

    CompanionPairingTicket StartPairing(
        string workspaceId);

    CompanionPairingClaimResponse ClaimPairing(
        CompanionPairingClaimRequest request);

    CompanionDevice? Authenticate(
        string token);

    CompanionDeviceListResponse GetDevices(
        string workspaceId);

    bool RevokeDevice(
        string workspaceId,
        Guid deviceId);
}

public sealed class CompanionDisabledException(string message)
    : Exception(message);

public sealed class CompanionPairingException(string message)
    : Exception(message);

public sealed class CompanionService : ICompanionService
{
    public const int MaximumDevicesPerWorkspace = 10;
    public const int PairingLifetimeMinutes = 5;
    public const int PairingCodeLength = 12;
    public const int MaximumDeviceNameCharacters = 80;

    private const string PairingAlphabet =
        "23456789ABCDEFGHJKLMNPQRSTUVWXYZ";

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web)
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

    private readonly object _gate = new();
    private readonly string _storagePath;
    private readonly ILogger<CompanionService> _logger;
    private readonly Dictionary<string, PendingPairing> _pairings =
        new(StringComparer.OrdinalIgnoreCase);
    private StoredCompanionState _state;

    public CompanionService(
        IConfiguration configuration,
        ILogger<CompanionService> logger)
    {
        _logger = logger;
        Enabled = configuration.GetValue(
            "Companion:Enabled",
            true);
        AllowInsecureHttp = configuration.GetValue(
            "Companion:AllowInsecureHttp",
            false);

        var configuredRoot =
            configuration["Companion:Root"]?.Trim();
        string directory;
        if (!string.IsNullOrWhiteSpace(configuredRoot))
        {
            directory = Path.GetFullPath(
                Environment.ExpandEnvironmentVariables(
                    configuredRoot));
        }
        else
        {
            var localData = Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(localData))
            {
                localData = Path.Combine(
                    Environment.GetFolderPath(
                        Environment.SpecialFolder.UserProfile),
                    ".personalai");
            }

            directory = Path.Combine(
                localData,
                "PersonalAI",
                "Companion");
        }

        Directory.CreateDirectory(directory);
        _storagePath = Path.Combine(
            directory,
            "devices.json");
        _state = Load();
    }

    public bool Enabled { get; }

    public bool AllowInsecureHttp { get; }

    public CompanionStatusResponse GetStatus() =>
        new(
            PersonalAiRelease.Version,
            Enabled,
            SecureTransportRequired: !AllowInsecureHttp,
            MaximumDevicesPerWorkspace,
            PairingLifetimeMinutes,
            [
                CompanionCapabilities.Pairing,
                CompanionCapabilities.Chat,
                CompanionCapabilities.TasksRead,
                CompanionCapabilities.CoreStatus
            ],
            [
                "Pairing code chỉ tồn tại tạm thời và chỉ được tạo từ admin request cục bộ.",
                "Device token chỉ trả một lần; backend chỉ lưu SHA-256 hash.",
                "Companion chat không được đề xuất hoặc thực thi tool.",
                "Task API trên Android là read-only trong v1.5.0.",
                "Remote Memory/Documents/Files/Tools/Undo/Connector/Development mutation chưa được mở.",
                AllowInsecureHttp
                    ? "HTTP không mã hóa đang được cho phép bằng cấu hình explicit; chỉ dùng trên mạng tin cậy."
                    : "Client pairing và client API yêu cầu HTTPS."
            ]);

    public CompanionPairingTicket StartPairing(
        string workspaceId)
    {
        EnsureEnabled();
        var normalizedWorkspace =
            NormalizeWorkspaceId(workspaceId);
        var now = DateTimeOffset.UtcNow;

        lock (_gate)
        {
            CleanupPairings(now);

            foreach (var code in _pairings
                .Where(item =>
                    item.Value.WorkspaceId.Equals(
                        normalizedWorkspace,
                        StringComparison.OrdinalIgnoreCase))
                .Select(item => item.Key)
                .ToArray())
            {
                _pairings.Remove(code);
            }

            var code = CreatePairingCode();
            var pairing = new PendingPairing(
                code,
                normalizedWorkspace,
                now.AddMinutes(PairingLifetimeMinutes));
            _pairings[code] = pairing;

            return new CompanionPairingTicket(
                pairing.Code,
                pairing.WorkspaceId,
                pairing.ExpiresAt);
        }
    }

    public CompanionPairingClaimResponse ClaimPairing(
        CompanionPairingClaimRequest request)
    {
        EnsureEnabled();
        ArgumentNullException.ThrowIfNull(request);

        var code = NormalizePairingCode(request.Code);
        var name = NormalizeDeviceName(request.DeviceName);
        var now = DateTimeOffset.UtcNow;

        lock (_gate)
        {
            CleanupPairings(now);

            if (!_pairings.TryGetValue(
                code,
                out var pairing)
                || pairing.ExpiresAt <= now)
            {
                throw new CompanionPairingException(
                    "Mã ghép nối không hợp lệ hoặc đã hết hạn.");
            }

            var count = _state.Devices.Count(item =>
                item.WorkspaceId.Equals(
                    pairing.WorkspaceId,
                    StringComparison.OrdinalIgnoreCase));
            if (count >= MaximumDevicesPerWorkspace)
            {
                throw new CompanionPairingException(
                    $"Workspace đã đạt giới hạn {MaximumDevicesPerWorkspace} thiết bị companion.");
            }

            var token = CreateDeviceToken();
            var device = new StoredCompanionDevice
            {
                Id = Guid.NewGuid(),
                WorkspaceId = pairing.WorkspaceId,
                Name = name,
                TokenSha256 = HashToken(token),
                CreatedAt = now,
                LastSeenAt = now
            };

            _state.Devices.Add(device);
            _pairings.Remove(code);
            SaveLocked();

            return new CompanionPairingClaimResponse(
                token,
                ToPublic(device));
        }
    }

    public CompanionDevice? Authenticate(
        string token)
    {
        if (!Enabled
            || string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        byte[] candidate;
        try
        {
            candidate = SHA256.HashData(
                Encoding.UTF8.GetBytes(token.Trim()));
        }
        catch
        {
            return null;
        }

        lock (_gate)
        {
            StoredCompanionDevice? matched = null;
            foreach (var item in _state.Devices)
            {
                byte[] storedHash;
                try
                {
                    storedHash = Convert.FromHexString(
                        item.TokenSha256);
                }
                catch
                {
                    continue;
                }

                if (storedHash.Length == candidate.Length
                    && CryptographicOperations.FixedTimeEquals(
                        storedHash,
                        candidate))
                {
                    matched = item;
                    break;
                }
            }

            if (matched is null)
            {
                return null;
            }

            var now = DateTimeOffset.UtcNow;
            if (now - matched.LastSeenAt
                >= TimeSpan.FromMinutes(1))
            {
                matched.LastSeenAt = now;
                SaveLocked();
            }

            return ToPublic(matched);
        }
    }

    public CompanionDeviceListResponse GetDevices(
        string workspaceId)
    {
        var normalizedWorkspace =
            NormalizeWorkspaceId(workspaceId);

        lock (_gate)
        {
            var devices = _state.Devices
                .Where(item =>
                    item.WorkspaceId.Equals(
                        normalizedWorkspace,
                        StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(item => item.LastSeenAt)
                .Select(ToPublic)
                .ToArray();

            return new CompanionDeviceListResponse(
                normalizedWorkspace,
                MaximumDevicesPerWorkspace,
                devices);
        }
    }

    public bool RevokeDevice(
        string workspaceId,
        Guid deviceId)
    {
        var normalizedWorkspace =
            NormalizeWorkspaceId(workspaceId);

        lock (_gate)
        {
            var index = _state.Devices.FindIndex(item =>
                item.Id == deviceId
                && item.WorkspaceId.Equals(
                    normalizedWorkspace,
                    StringComparison.OrdinalIgnoreCase));
            if (index < 0)
            {
                return false;
            }

            _state.Devices.RemoveAt(index);
            SaveLocked();
            return true;
        }
    }

    private void EnsureEnabled()
    {
        if (!Enabled)
        {
            throw new CompanionDisabledException(
                "Android Companion đang bị tắt trong cấu hình PersonalAI.");
        }
    }

    private StoredCompanionState Load()
    {
        if (!File.Exists(_storagePath))
        {
            return new StoredCompanionState();
        }

        try
        {
            var loaded =
                JsonSerializer.Deserialize<StoredCompanionState>(
                    File.ReadAllText(_storagePath),
                    JsonOptions)
                ?? new StoredCompanionState();
            loaded.Normalize();
            return loaded;
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or JsonException)
        {
            _logger.LogWarning(
                exception,
                "Không thể đọc Android Companion device store. Bắt đầu với danh sách trống.");
            return new StoredCompanionState();
        }
    }

    private void SaveLocked()
    {
        var json = JsonSerializer.Serialize(
            _state,
            JsonOptions);
        var temporaryPath =
            _storagePath + ".tmp";
        File.WriteAllText(
            temporaryPath,
            json,
            Encoding.UTF8);
        File.Move(
            temporaryPath,
            _storagePath,
            true);
    }

    private void CleanupPairings(
        DateTimeOffset now)
    {
        foreach (var code in _pairings
            .Where(item =>
                item.Value.ExpiresAt <= now)
            .Select(item => item.Key)
            .ToArray())
        {
            _pairings.Remove(code);
        }
    }

    private static string CreatePairingCode()
    {
        Span<char> buffer =
            stackalloc char[PairingCodeLength];

        for (var i = 0; i < buffer.Length; i++)
        {
            buffer[i] = PairingAlphabet[
                RandomNumberGenerator.GetInt32(
                    PairingAlphabet.Length)];
        }

        return new string(buffer);
    }

    private static string CreateDeviceToken()
    {
        Span<byte> bytes =
            stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static string HashToken(
        string token) =>
        Convert.ToHexString(
            SHA256.HashData(
                Encoding.UTF8.GetBytes(token)));

    private static string NormalizePairingCode(
        string? value)
    {
        var code = (value ?? string.Empty)
            .Trim()
            .Replace("-", string.Empty)
            .Replace(" ", string.Empty)
            .ToUpperInvariant();

        if (code.Length != PairingCodeLength
            || code.Any(character =>
                !PairingAlphabet.Contains(
                    character,
                    StringComparison.Ordinal)))
        {
            throw new CompanionPairingException(
                "Mã ghép nối không hợp lệ.");
        }

        return code;
    }

    private static string NormalizeDeviceName(
        string? value)
    {
        var normalized = string.Join(
            " ",
            (value ?? string.Empty)
                .Split(
                    [' ', '\t', '\r', '\n'],
                    StringSplitOptions.RemoveEmptyEntries));

        if (normalized.Length is < 2
            or > MaximumDeviceNameCharacters)
        {
            throw new CompanionPairingException(
                $"Tên thiết bị phải có từ 2 đến {MaximumDeviceNameCharacters} ký tự.");
        }

        return normalized;
    }

    private static string NormalizeWorkspaceId(
        string? value)
    {
        var normalized = (value ?? string.Empty)
            .Trim()
            .ToLowerInvariant();
        if (normalized.Length is < 1 or > 80)
        {
            throw new CompanionPairingException(
                "Workspace ghép nối không hợp lệ.");
        }

        return normalized;
    }

    private static CompanionDevice ToPublic(
        StoredCompanionDevice item) =>
        new(
            item.Id,
            item.WorkspaceId,
            item.Name,
            item.CreatedAt,
            item.LastSeenAt);

    private sealed record PendingPairing(
        string Code,
        string WorkspaceId,
        DateTimeOffset ExpiresAt);

    private sealed class StoredCompanionState
    {
        public List<StoredCompanionDevice> Devices { get; set; } = [];

        public void Normalize()
        {
            Devices ??= [];
            Devices = Devices
                .Where(item =>
                    item.Id != Guid.Empty
                    && !string.IsNullOrWhiteSpace(
                        item.WorkspaceId)
                    && !string.IsNullOrWhiteSpace(
                        item.Name)
                    && !string.IsNullOrWhiteSpace(
                        item.TokenSha256))
                .Select(item =>
                {
                    item.WorkspaceId =
                        item.WorkspaceId
                            .Trim()
                            .ToLowerInvariant();
                    item.Name = item.Name.Trim();
                    return item;
                })
                .ToList();
        }
    }

    private sealed class StoredCompanionDevice
    {
        public Guid Id { get; set; }

        public string WorkspaceId { get; set; } =
            PersonalWorkspaceIds.Personal;

        public string Name { get; set; } =
            string.Empty;

        public string TokenSha256 { get; set; } =
            string.Empty;

        public DateTimeOffset CreatedAt { get; set; }

        public DateTimeOffset LastSeenAt { get; set; }
    }
}
