using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.DataProtection;
using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public interface ILifeContextService
{
    LifeContextStatusResponse GetStatus();

    Task<LifeContextSourceListResponse> GetSourcesAsync(
        CancellationToken cancellationToken = default);

    Task<LifeContextSource> CreateSourceAsync(
        CreateLifeContextSourceRequest request,
        CancellationToken cancellationToken = default);

    Task<LifeContextSource?> SetSourceEnabledAsync(
        Guid sourceId,
        SetLifeContextSourceEnabledRequest request,
        CancellationToken cancellationToken = default);

    Task<LifeContextSource?> SetConsentAsync(
        Guid sourceId,
        SetLifeContextConsentRequest request,
        CancellationToken cancellationToken = default);

    Task<LifeContextEntry> AddEntryAsync(
        Guid sourceId,
        CreateLifeContextEntryRequest request,
        CancellationToken cancellationToken = default);

    Task<LifeContextEntryListResponse> GetEntriesAsync(
        Guid? sourceId = null,
        int limit = 100,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteEntryAsync(
        Guid entryId,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteSourceAsync(
        Guid sourceId,
        bool confirmed,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LifeContextSelection>> SelectRelevantAsync(
        string question,
        int maximumCharacters,
        int maximumEntries,
        CancellationToken cancellationToken = default);
}

public sealed class LifeContextConfirmationRequiredException(string message)
    : Exception(message);

public sealed class LifeContextConsentRequiredException(string message)
    : Exception(message);

public sealed class LifeContextService : ILifeContextService
{
    public const int MaximumSourcesPerWorkspace = 20;
    public const int MaximumEntriesPerWorkspace = 1_000;
    public const int MaximumContentCharacters = 2_000;
    public const int MinimumRetentionDays = 1;
    public const int MaximumRetentionDays = 365;
    public const int MaximumQueryLimit = 200;

    private const int MaximumSourceNameCharacters = 80;

    private static readonly Regex TokenRegex =
        new(@"[p{L}p{N}]+", RegexOptions.Compiled);

    private static readonly HashSet<string> StopWords =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "ai", "bạn", "biết", "các", "có", "của", "cho", "đó", "được",
            "gì", "giúp", "hãy", "không", "là", "mình", "một", "này",
            "những", "nói", "tôi", "trả", "trong", "và", "về", "theo",
            "thế", "nào", "để", "đang", "khi", "ở", "đâu", "thì", "với",
            "sẽ", "đã", "cần"
        };

    private static readonly HashSet<string> BroadLifeSignals =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "lịch", "calendar", "hôm nay", "hôm qua", "ngày mai",
            "ở đâu", "location", "địa điểm", "vị trí", "di chuyển",
            "hoạt động", "activity", "thiết bị", "device", "điện thoại",
            "routine", "thói quen", "cuộc sống", "life context"
        };

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web)
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly IDataProtector _protector;
    private readonly IWorkspaceContextAccessor _workspaceContext;
    private readonly ILogger<LifeContextService> _logger;
    private readonly string _storagePath;
    private StoredLifeContextState _state;

    public LifeContextService(
        IConfiguration configuration,
        IDataProtectionProvider dataProtectionProvider,
        IWorkspaceContextAccessor workspaceContext,
        ILogger<LifeContextService> logger)
    {
        _workspaceContext = workspaceContext;
        _logger = logger;
        _protector = dataProtectionProvider.CreateProtector(
            "PersonalAI.LifeContext.v1");

        var configuredRoot =
            configuration["LifeContext:Root"]?.Trim();
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
                "LifeContext");
        }

        Directory.CreateDirectory(directory);
        _storagePath = Path.Combine(
            directory,
            "life-context.json");
        _state = Load();
    }

    public LifeContextStatusResponse GetStatus() =>
        new(
            PersonalAiRelease.Version,
            Supported: true,
            AutomaticCollectionEnabled: false,
            ContentEncrypted: true,
            MaximumSourcesPerWorkspace,
            MaximumEntriesPerWorkspace,
            MaximumContentCharacters,
            MinimumRetentionDays,
            MaximumRetentionDays,
            LifeContextSourceKinds.All
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            [
                "v1.6.0 chỉ lưu snapshot được người dùng/import flow chủ động gửi; không tự đọc GPS, sensor, calendar hoặc activity ở nền.",
                "Mỗi source phải được cấp consent riêng và có retention riêng.",
                "Source bị revoke consent hoặc disable không được đưa vào Context Manager.",
                "Entry content được mã hóa local bằng ASP.NET Core Data Protection.",
                "Không có background collector, Android sensor permission hoặc cloud sync trong v1.6.0."
            ]);

    public async Task<LifeContextSourceListResponse> GetSourcesAsync(
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            CleanupExpiredLocked();

            var workspaceId =
                _workspaceContext.CurrentWorkspaceId;
            var sources = _state.Sources
                .Where(item =>
                    SameWorkspace(
                        item.WorkspaceId,
                        workspaceId))
                .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .Select(ToPublicSource)
                .ToArray();

            return new LifeContextSourceListResponse(
                workspaceId,
                MaximumSourcesPerWorkspace,
                sources);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<LifeContextSource> CreateSourceAsync(
        CreateLifeContextSourceRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!request.Confirmed)
        {
            throw new LifeContextConfirmationRequiredException(
                "Cần xác nhận trước khi tạo nguồn Life Context.");
        }

        if (!request.ConsentGranted)
        {
            throw new LifeContextConsentRequiredException(
                "Nguồn Life Context chỉ được tạo khi người dùng cấp consent rõ ràng.");
        }

        var name = NormalizeSourceName(request.Name);
        var kind = NormalizeSourceKind(request.Kind);
        var retentionDays =
            NormalizeRetentionDays(request.RetentionDays);
        var workspaceId =
            _workspaceContext.CurrentWorkspaceId;
        var now = DateTimeOffset.UtcNow;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            CleanupExpiredLocked();

            var currentCount = _state.Sources.Count(item =>
                SameWorkspace(
                    item.WorkspaceId,
                    workspaceId));
            if (currentCount >= MaximumSourcesPerWorkspace)
            {
                throw new ArgumentException(
                    $"Workspace đã đạt giới hạn {MaximumSourcesPerWorkspace} Life Context source.");
            }

            if (_state.Sources.Any(item =>
                SameWorkspace(
                    item.WorkspaceId,
                    workspaceId)
                && item.Name.Equals(
                    name,
                    StringComparison.OrdinalIgnoreCase)))
            {
                throw new ArgumentException(
                    "Tên Life Context source đã tồn tại trong workspace hiện tại.");
            }

            var stored = new StoredLifeContextSource
            {
                Id = Guid.NewGuid(),
                WorkspaceId = workspaceId,
                Name = name,
                Kind = kind,
                Enabled = true,
                ConsentGranted = true,
                RetentionDays = retentionDays,
                CreatedAt = now,
                UpdatedAt = now
            };

            _state.Sources.Add(stored);
            await PersistLockedAsync(cancellationToken);
            return ToPublicSource(stored);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<LifeContextSource?> SetSourceEnabledAsync(
        Guid sourceId,
        SetLifeContextSourceEnabledRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!request.Confirmed)
        {
            throw new LifeContextConfirmationRequiredException(
                "Cần xác nhận trước khi đổi trạng thái Life Context source.");
        }

        var workspaceId =
            _workspaceContext.CurrentWorkspaceId;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var source = FindSourceLocked(
                sourceId,
                workspaceId);
            if (source is null)
            {
                return null;
            }

            if (request.Enabled
                && !source.ConsentGranted)
            {
                throw new LifeContextConsentRequiredException(
                    "Không thể bật source khi consent đang bị thu hồi.");
            }

            source.Enabled = request.Enabled;
            source.UpdatedAt = DateTimeOffset.UtcNow;
            await PersistLockedAsync(cancellationToken);
            return ToPublicSource(source);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<LifeContextSource?> SetConsentAsync(
        Guid sourceId,
        SetLifeContextConsentRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!request.Confirmed)
        {
            throw new LifeContextConfirmationRequiredException(
                "Cần xác nhận trước khi thay đổi consent Life Context.");
        }

        var workspaceId =
            _workspaceContext.CurrentWorkspaceId;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var source = FindSourceLocked(
                sourceId,
                workspaceId);
            if (source is null)
            {
                return null;
            }

            source.ConsentGranted = request.Granted;
            source.Enabled = request.Granted
                ? source.Enabled
                : false;
            source.UpdatedAt = DateTimeOffset.UtcNow;

            if (!request.Granted
                && request.PurgeExisting)
            {
                _state.Entries.RemoveAll(item =>
                    item.SourceId == source.Id
                    && SameWorkspace(
                        item.WorkspaceId,
                        workspaceId));
            }

            await PersistLockedAsync(cancellationToken);
            return ToPublicSource(source);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<LifeContextEntry> AddEntryAsync(
        Guid sourceId,
        CreateLifeContextEntryRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!request.Confirmed)
        {
            throw new LifeContextConfirmationRequiredException(
                "Cần xác nhận trước khi lưu Life Context entry.");
        }

        var content = NormalizeContent(request.Content);
        var capturedAt =
            request.CapturedAt ?? DateTimeOffset.UtcNow;
        var now = DateTimeOffset.UtcNow;

        if (capturedAt > now.AddMinutes(10))
        {
            throw new ArgumentException(
                "Thời điểm Life Context không được nằm quá xa trong tương lai.");
        }

        var workspaceId =
            _workspaceContext.CurrentWorkspaceId;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            CleanupExpiredLocked();

            var source = FindSourceLocked(
                sourceId,
                workspaceId)
                ?? throw new KeyNotFoundException(
                    "Không tìm thấy Life Context source trong workspace hiện tại.");

            if (!source.ConsentGranted)
            {
                throw new LifeContextConsentRequiredException(
                    "Source chưa có consent.");
            }

            if (!source.Enabled)
            {
                throw new ArgumentException(
                    "Source đang bị tắt.");
            }

            var expiresAt =
                capturedAt.AddDays(source.RetentionDays);
            if (expiresAt <= now)
            {
                throw new ArgumentException(
                    "Snapshot đã nằm ngoài retention window của source.");
            }

            var workspaceCount = _state.Entries.Count(item =>
                SameWorkspace(
                    item.WorkspaceId,
                    workspaceId));
            if (workspaceCount >= MaximumEntriesPerWorkspace)
            {
                throw new ArgumentException(
                    $"Workspace đã đạt giới hạn {MaximumEntriesPerWorkspace} Life Context entry.");
            }

            var stored = new StoredLifeContextEntry
            {
                Id = Guid.NewGuid(),
                SourceId = source.Id,
                WorkspaceId = workspaceId,
                EncryptedContent = _protector.Protect(content),
                CapturedAt = capturedAt,
                ExpiresAt = expiresAt,
                CreatedAt = now
            };

            _state.Entries.Add(stored);
            source.LastEntryAt = capturedAt;
            source.UpdatedAt = now;

            await PersistLockedAsync(cancellationToken);
            return ToPublicEntry(
                stored,
                source)
                ?? throw new InvalidOperationException(
                    "Không thể đọc lại Life Context entry vừa lưu.");
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<LifeContextEntryListResponse> GetEntriesAsync(
        Guid? sourceId = null,
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        var safeLimit = Math.Clamp(
            limit,
            1,
            MaximumQueryLimit);
        var workspaceId =
            _workspaceContext.CurrentWorkspaceId;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            CleanupExpiredLocked();

            var sourceMap = _state.Sources
                .Where(item =>
                    SameWorkspace(
                        item.WorkspaceId,
                        workspaceId))
                .ToDictionary(item => item.Id);

            if (sourceId.HasValue
                && !sourceMap.ContainsKey(sourceId.Value))
            {
                return new LifeContextEntryListResponse(
                    workspaceId,
                    MaximumEntriesPerWorkspace,
                    []);
            }

            var entries = _state.Entries
                .Where(item =>
                    SameWorkspace(
                        item.WorkspaceId,
                        workspaceId)
                    && (!sourceId.HasValue
                        || item.SourceId == sourceId.Value)
                    && sourceMap.TryGetValue(
                        item.SourceId,
                        out var source)
                    && source.ConsentGranted)
                .OrderByDescending(item => item.CapturedAt)
                .Take(safeLimit)
                .Select(item =>
                    ToPublicEntry(
                        item,
                        sourceMap[item.SourceId]))
                .Where(item => item is not null)
                .Cast<LifeContextEntry>()
                .ToArray();

            return new LifeContextEntryListResponse(
                workspaceId,
                MaximumEntriesPerWorkspace,
                entries);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> DeleteEntryAsync(
        Guid entryId,
        CancellationToken cancellationToken = default)
    {
        var workspaceId =
            _workspaceContext.CurrentWorkspaceId;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var removed = _state.Entries.RemoveAll(item =>
                item.Id == entryId
                && SameWorkspace(
                    item.WorkspaceId,
                    workspaceId)) > 0;

            if (!removed)
            {
                return false;
            }

            await PersistLockedAsync(cancellationToken);
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> DeleteSourceAsync(
        Guid sourceId,
        bool confirmed,
        CancellationToken cancellationToken = default)
    {
        if (!confirmed)
        {
            throw new LifeContextConfirmationRequiredException(
                "Cần xác nhận trước khi xóa Life Context source và dữ liệu liên quan.");
        }

        var workspaceId =
            _workspaceContext.CurrentWorkspaceId;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var removed = _state.Sources.RemoveAll(item =>
                item.Id == sourceId
                && SameWorkspace(
                    item.WorkspaceId,
                    workspaceId)) > 0;

            if (!removed)
            {
                return false;
            }

            _state.Entries.RemoveAll(item =>
                item.SourceId == sourceId
                && SameWorkspace(
                    item.WorkspaceId,
                    workspaceId));

            await PersistLockedAsync(cancellationToken);
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<LifeContextSelection>> SelectRelevantAsync(
        string question,
        int maximumCharacters,
        int maximumEntries,
        CancellationToken cancellationToken = default)
    {
        var normalizedQuestion =
            (question ?? string.Empty).Trim();
        if (normalizedQuestion.Length < 2
            || maximumCharacters <= 0
            || maximumEntries <= 0)
        {
            return [];
        }

        var workspaceId =
            _workspaceContext.CurrentWorkspaceId;
        var now = DateTimeOffset.UtcNow;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            CleanupExpiredLocked();

            var activeSources = _state.Sources
                .Where(item =>
                    SameWorkspace(
                        item.WorkspaceId,
                        workspaceId)
                    && item.Enabled
                    && item.ConsentGranted)
                .ToDictionary(item => item.Id);

            if (activeSources.Count == 0)
            {
                return [];
            }

            var questionTokens =
                SignificantTokens(normalizedQuestion);
            var broad = IsBroadLifeQuestion(
                normalizedQuestion);

            var candidates = _state.Entries
                .Where(item =>
                    SameWorkspace(
                        item.WorkspaceId,
                        workspaceId)
                    && item.ExpiresAt > now
                    && activeSources.ContainsKey(item.SourceId))
                .Select(item =>
                {
                    var source =
                        activeSources[item.SourceId];
                    var entry =
                        ToPublicEntry(item, source);
                    if (entry is null)
                    {
                        return null;
                    }

                    var score = ScoreEntry(
                        entry,
                        questionTokens,
                        broad,
                        now);
                    return new LifeContextSelection(
                        entry,
                        score,
                        BuildReason(
                            entry,
                            score,
                            broad));
                })
                .Where(item =>
                    item is not null
                    && item.Score > 0)
                .Cast<LifeContextSelection>()
                .OrderByDescending(item => item.Score)
                .ThenByDescending(item =>
                    item.Entry.CapturedAt)
                .ToArray();

            var selected =
                new List<LifeContextSelection>();
            var used = 0;

            foreach (var candidate in candidates)
            {
                if (selected.Count >= maximumEntries)
                {
                    break;
                }

                var length =
                    candidate.Entry.Content.Length;
                if (length <= 0
                    || used + length > maximumCharacters)
                {
                    continue;
                }

                selected.Add(candidate);
                used += length;
            }

            return selected;
        }
        finally
        {
            _gate.Release();
        }
    }

    private StoredLifeContextState Load()
    {
        if (!File.Exists(_storagePath))
        {
            return new StoredLifeContextState();
        }

        try
        {
            var loaded =
                JsonSerializer.Deserialize<StoredLifeContextState>(
                    File.ReadAllText(_storagePath),
                    JsonOptions)
                ?? new StoredLifeContextState();
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
                "Không thể đọc Life Context store. Bắt đầu với store trống.");
            return new StoredLifeContextState();
        }
    }

    private async Task PersistLockedAsync(
        CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(
            _state,
            JsonOptions);
        var temporaryPath =
            _storagePath + ".tmp";
        await File.WriteAllTextAsync(
            temporaryPath,
            json,
            cancellationToken);
        File.Move(
            temporaryPath,
            _storagePath,
            true);
    }

    private void CleanupExpiredLocked()
    {
        var now = DateTimeOffset.UtcNow;
        _state.Entries.RemoveAll(item =>
            item.ExpiresAt <= now);
    }

    private StoredLifeContextSource? FindSourceLocked(
        Guid sourceId,
        string workspaceId) =>
        _state.Sources.FirstOrDefault(item =>
            item.Id == sourceId
            && SameWorkspace(
                item.WorkspaceId,
                workspaceId));

    private LifeContextSource ToPublicSource(
        StoredLifeContextSource item)
    {
        var entryCount = _state.Entries.Count(entry =>
            entry.SourceId == item.Id
            && SameWorkspace(
                entry.WorkspaceId,
                item.WorkspaceId)
            && entry.ExpiresAt > DateTimeOffset.UtcNow);

        return new LifeContextSource(
            item.Id,
            NormalizeWorkspaceId(item.WorkspaceId),
            item.Name,
            NormalizeSourceKind(item.Kind),
            item.Enabled,
            item.ConsentGranted,
            NormalizeRetentionDays(item.RetentionDays),
            item.CreatedAt,
            item.UpdatedAt,
            item.LastEntryAt,
            entryCount);
    }

    private LifeContextEntry? ToPublicEntry(
        StoredLifeContextEntry item,
        StoredLifeContextSource source)
    {
        try
        {
            return new LifeContextEntry(
                item.Id,
                item.SourceId,
                NormalizeWorkspaceId(item.WorkspaceId),
                source.Name,
                NormalizeSourceKind(source.Kind),
                _protector.Unprotect(
                    item.EncryptedContent),
                item.CapturedAt,
                item.ExpiresAt,
                item.CreatedAt);
        }
        catch (Exception exception) when (
            exception is CryptographicException
                or ArgumentException)
        {
            _logger.LogWarning(
                exception,
                "Không thể giải mã Life Context entry {EntryId}.",
                item.Id);
            return null;
        }
    }

    private static string NormalizeSourceName(
        string? value)
    {
        var normalized = Regex.Replace(
                value ?? string.Empty,
                @"s+",
                " ")
            .Trim();

        if (normalized.Length is < 2
            or > MaximumSourceNameCharacters)
        {
            throw new ArgumentException(
                $"Tên source phải có từ 2 đến {MaximumSourceNameCharacters} ký tự.");
        }

        return normalized;
    }

    private static string NormalizeSourceKind(
        string? value)
    {
        var normalized =
            (value ?? string.Empty)
                .Trim()
                .ToLowerInvariant();

        if (!LifeContextSourceKinds.All.Contains(
            normalized))
        {
            throw new ArgumentException(
                "Loại Life Context source không hợp lệ.");
        }

        return normalized;
    }

    private static int NormalizeRetentionDays(
        int value)
    {
        if (value is < MinimumRetentionDays
            or > MaximumRetentionDays)
        {
            throw new ArgumentException(
                $"Retention phải từ {MinimumRetentionDays} đến {MaximumRetentionDays} ngày.");
        }

        return value;
    }

    private static string NormalizeContent(
        string? value)
    {
        var normalized = (value ?? string.Empty)
            .Trim();

        if (normalized.Length is < 3
            or > MaximumContentCharacters)
        {
            throw new ArgumentException(
                $"Life Context entry phải có từ 3 đến {MaximumContentCharacters} ký tự.");
        }

        return normalized;
    }

    private static string NormalizeWorkspaceId(
        string? workspaceId) =>
        string.IsNullOrWhiteSpace(workspaceId)
            ? PersonalWorkspaceIds.Personal
            : workspaceId.Trim().ToLowerInvariant();

    private static bool SameWorkspace(
        string? left,
        string? right) =>
        string.Equals(
            NormalizeWorkspaceId(left),
            NormalizeWorkspaceId(right),
            StringComparison.OrdinalIgnoreCase);

    private static string[] SignificantTokens(
        string value) =>
        TokenRegex.Matches(
                value.Normalize(
                    NormalizationForm.FormKC))
            .Cast<Match>()
            .Select(match =>
                match.Value.ToLowerInvariant())
            .Where(token =>
                token.Length >= 2
                && !StopWords.Contains(token))
            .Distinct(
                StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static bool IsBroadLifeQuestion(
        string question)
    {
        var normalized = Regex.Replace(
                question
                    .Normalize(
                        NormalizationForm.FormKC)
                    .ToLowerInvariant(),
                @"s+",
                " ")
            .Trim();

        return BroadLifeSignals.Any(signal =>
            normalized.Contains(
                signal,
                StringComparison.OrdinalIgnoreCase));
    }

    private static double ScoreEntry(
        LifeContextEntry entry,
        IReadOnlyList<string> questionTokens,
        bool broad,
        DateTimeOffset now)
    {
        var entryTokens =
            SignificantTokens(
                $"{entry.SourceName} {entry.SourceKind} {entry.Content}")
            .ToHashSet(
                StringComparer.OrdinalIgnoreCase);

        var matched =
            questionTokens.Count(
                entryTokens.Contains);

        if (!broad
            && matched == 0)
        {
            return 0;
        }

        var coverage =
            questionTokens.Count == 0
                ? 0
                : (double)matched
                    / questionTokens.Count;

        var age =
            now - entry.CapturedAt;
        var recencyBonus =
            age <= TimeSpan.FromHours(24)
                ? 18
                : age <= TimeSpan.FromDays(7)
                    ? 10
                    : age <= TimeSpan.FromDays(30)
                        ? 4
                        : 0;

        var broadBonus =
            broad ? 14 : 0;

        return broadBonus
            + recencyBonus
            + (matched * 9)
            + (coverage * 28);
    }

    private static string BuildReason(
        LifeContextEntry entry,
        double score,
        bool broad) =>
        broad
            ? $"Life Context từ source {entry.SourceName} phù hợp loại yêu cầu hiện tại; điểm {score:0.##}."
            : $"Life Context từ source {entry.SourceName} có nội dung liên quan; điểm {score:0.##}.";

    private sealed class StoredLifeContextState
    {
        public List<StoredLifeContextSource> Sources { get; set; } = [];

        public List<StoredLifeContextEntry> Entries { get; set; } = [];

        public void Normalize()
        {
            Sources ??= [];
            Entries ??= [];

            foreach (var source in Sources)
            {
                source.WorkspaceId =
                    NormalizeWorkspaceId(
                        source.WorkspaceId);
            }

            foreach (var entry in Entries)
            {
                entry.WorkspaceId =
                    NormalizeWorkspaceId(
                        entry.WorkspaceId);
            }
        }
    }

    private sealed class StoredLifeContextSource
    {
        public Guid Id { get; set; }

        public string WorkspaceId { get; set; } =
            PersonalWorkspaceIds.Personal;

        public string Name { get; set; } =
            string.Empty;

        public string Kind { get; set; } =
            LifeContextSourceKinds.Manual;

        public bool Enabled { get; set; } =
            true;

        public bool ConsentGranted { get; set; }

        public int RetentionDays { get; set; } =
            30;

        public DateTimeOffset CreatedAt { get; set; }

        public DateTimeOffset UpdatedAt { get; set; }

        public DateTimeOffset? LastEntryAt { get; set; }
    }

    private sealed class StoredLifeContextEntry
    {
        public Guid Id { get; set; }

        public Guid SourceId { get; set; }

        public string WorkspaceId { get; set; } =
            PersonalWorkspaceIds.Personal;

        public string EncryptedContent { get; set; } =
            string.Empty;

        public DateTimeOffset CapturedAt { get; set; }

        public DateTimeOffset ExpiresAt { get; set; }

        public DateTimeOffset CreatedAt { get; set; }
    }
}
