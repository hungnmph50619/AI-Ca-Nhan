using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.DataProtection;
using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public interface IPersonalMemoryStore
{
    Task<IReadOnlyList<PersonalMemory>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<PersonalMemory> AddAsync(CreatePersonalMemoryRequest request, CancellationToken cancellationToken = default);
    Task<PersonalMemory?> UpdateAsync(Guid memoryId, UpdatePersonalMemoryRequest request, CancellationToken cancellationToken = default);
    Task<PersonalMemory?> SetEnabledAsync(Guid memoryId, bool isEnabled, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(Guid memoryId, CancellationToken cancellationToken = default);
    Task<int> DeleteAllAsync(CancellationToken cancellationToken = default);
    Task<PersonalMemoryStats> GetStatsAsync(CancellationToken cancellationToken = default);
    Task<PersonalMemoryImportResult> ImportAsync(ImportPersonalMemoriesRequest request, CancellationToken cancellationToken = default);
}

public sealed class PersonalMemoryStore : IPersonalMemoryStore
{
    private const int DefaultTemporaryDays = 30;
    private static readonly HashSet<string> SupportedKinds = new(StringComparer.OrdinalIgnoreCase) { "fact", "preference", "rule" };
    private static readonly HashSet<string> SupportedRetentions = new(StringComparer.OrdinalIgnoreCase) { "long-term", "temporary" };
    private static readonly HashSet<string> SimilarityStopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "tôi", "của", "là", "một", "có", "đang", "và", "với", "cho", "này", "đó", "không", "rất"
    };

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly IDataProtector _protector;
    private readonly ILogger<PersonalMemoryStore> _logger;
    private readonly IWorkspaceContextAccessor _workspaceContext;
    private readonly string _memoryPath;
    private List<StoredPersonalMemory> _memories;

    public PersonalMemoryStore(
        IDataProtectionProvider dataProtectionProvider,
        ILogger<PersonalMemoryStore> logger,
        IWorkspaceContextAccessor workspaceContext)
    {
        _protector = dataProtectionProvider.CreateProtector("PersonalAI.PersonalMemory.v1");
        _logger = logger;
        _workspaceContext = workspaceContext;
        var localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localData))
            localData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".personalai");
        var memoryDirectory = Path.Combine(localData, "PersonalAI", "Memory");
        Directory.CreateDirectory(memoryDirectory);
        _memoryPath = Path.Combine(memoryDirectory, "memories.json");
        _memories = LoadMemories();
    }

    public async Task<IReadOnlyList<PersonalMemory>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try { return PublicMemories().OrderByDescending(memory => memory.UpdatedAt).ToArray(); }
        finally { _gate.Release(); }
    }

    public async Task<PersonalMemory> AddAsync(CreatePersonalMemoryRequest request, CancellationToken cancellationToken = default)
    {
        var content = ValidateContent(request.Content);
        var kind = NormalizeKind(request.Kind);
        var retention = NormalizeRetention(request.Retention);
        var now = DateTimeOffset.UtcNow;
        var expiresAt = ResolveExpiration(retention, request.ExpiresAt, null, now, allowPast: false);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var duplicate = FindDuplicate(content);
            if (duplicate is not null)
            {
                if ((duplicate.IsExpired || duplicate.IsStale) && !request.ConfirmCreateSimilar)
                    throw new MemoryUpdateSuggestionException(duplicate.Id, duplicate.Content, "Nội dung này trùng với một trí nhớ cũ. Bạn có thể cập nhật trí nhớ đó thay vì tạo bản mới.");

                if (!duplicate.IsExpired && !duplicate.IsStale)
                    throw new ArgumentException("Nội dung này đã có trong trí nhớ.");
            }

            if (!request.ConfirmCreateSimilar)
            {
                var candidate = FindUpdateCandidate(content, kind);
                if (candidate is not null)
                    throw new MemoryUpdateSuggestionException(candidate.Id, candidate.Content, "Nội dung mới có vẻ là bản cập nhật của một trí nhớ đã lưu.");
            }

            var stored = new StoredPersonalMemory
            {
                Id = Guid.NewGuid(),
                Kind = kind,
                EncryptedContent = _protector.Protect(content),
                CreatedAt = now,
                UpdatedAt = now,
                IsEnabled = request.IsEnabled,
                Retention = retention,
                ExpiresAt = expiresAt,
                WorkspaceId = _workspaceContext.CurrentWorkspaceId
            };
            _memories.Add(stored);
            await PersistAsync(cancellationToken);
            return ToPublicMemory(stored) ?? throw new InvalidOperationException("Không thể đọc lại trí nhớ vừa lưu.");
        }
        finally { _gate.Release(); }
    }

    public async Task<PersonalMemory?> UpdateAsync(Guid memoryId, UpdatePersonalMemoryRequest request, CancellationToken cancellationToken = default)
    {
        var content = ValidateContent(request.Content);
        var kind = NormalizeKind(request.Kind);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var stored = _memories.FirstOrDefault(memory =>
                memory.Id == memoryId && IsCurrentWorkspace(memory));
            if (stored is null) return null;

            var duplicate = FindDuplicate(content, memoryId);
            if (duplicate is not null && !request.ConfirmOverwrite)
                throw new MemoryOverwriteConfirmationException(duplicate.Id, "Nội dung tương tự đã tồn tại. Hãy xác nhận trước khi cập nhật.");
            if (duplicate is not null && request.ConfirmOverwrite)
                _memories.RemoveAll(memory => memory.Id == duplicate.Id);

            var now = DateTimeOffset.UtcNow;
            var retention = request.Retention is null ? NormalizeStoredRetention(stored.Retention) : NormalizeRetention(request.Retention);
            var expiresAt = ResolveExpiration(retention, request.ExpiresAt, stored.ExpiresAt, now, allowPast: false);

            stored.Kind = kind;
            stored.EncryptedContent = _protector.Protect(content);
            stored.Retention = retention;
            stored.ExpiresAt = expiresAt;
            stored.UpdatedAt = now;
            await PersistAsync(cancellationToken);
            return ToPublicMemory(stored);
        }
        finally { _gate.Release(); }
    }

    public async Task<PersonalMemory?> SetEnabledAsync(Guid memoryId, bool isEnabled, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var stored = _memories.FirstOrDefault(memory =>
                memory.Id == memoryId && IsCurrentWorkspace(memory));
            if (stored is null) return null;
            stored.IsEnabled = isEnabled;
            await PersistAsync(cancellationToken);
            return ToPublicMemory(stored);
        }
        finally { _gate.Release(); }
    }

    public async Task<bool> DeleteAsync(Guid memoryId, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var removed = _memories.RemoveAll(memory =>
                memory.Id == memoryId && IsCurrentWorkspace(memory)) > 0;
            if (!removed) return false;
            await PersistAsync(cancellationToken);
            return true;
        }
        finally { _gate.Release(); }
    }

    public async Task<int> DeleteAllAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var count = _memories.Count(IsCurrentWorkspace);
            if (count == 0) return 0;
            _memories.RemoveAll(IsCurrentWorkspace);
            await PersistAsync(cancellationToken);
            return count;
        }
        finally { _gate.Release(); }
    }

    public async Task<PersonalMemoryStats> GetStatsAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var memories = PublicMemories().ToArray();
            return new PersonalMemoryStats(
                memories.Length,
                memories.Count(memory => memory.IsEnabled),
                memories.Count(memory => !memory.IsEnabled),
                memories.Count(memory => memory.Kind == "fact"),
                memories.Count(memory => memory.Kind == "preference"),
                memories.Count(memory => memory.Kind == "rule"),
                memories.Count(memory => memory.Retention == "long-term"),
                memories.Count(memory => memory.Retention == "temporary"),
                memories.Count(memory => memory.IsExpired),
                memories.Count(memory => memory.IsStale),
                memories.Length == 0 ? null : memories.Min(memory => memory.CreatedAt),
                memories.Length == 0 ? null : memories.Max(memory => memory.UpdatedAt));
        }
        finally { _gate.Release(); }
    }

    public async Task<PersonalMemoryImportResult> ImportAsync(ImportPersonalMemoriesRequest request, CancellationToken cancellationToken = default)
    {
        var items = request.Memories ?? [];
        if (items.Count > 5_000) throw new ArgumentException("Mỗi lần chỉ được nhập tối đa 5.000 trí nhớ.");

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var imported = 0;
            var skipped = 0;
            var staged = new List<StoredPersonalMemory>();
            var seenContent = PublicMemories()
                .Select(memory => NormalizeContent(memory.Content))
                .ToHashSet(StringComparer.Ordinal);

            foreach (var item in items)
            {
                var content = ValidateContent(item.Content);
                var kind = NormalizeKind(item.Kind);
                var normalizedContent = NormalizeContent(content);

                if (!seenContent.Add(normalizedContent))
                {
                    if (request.SkipDuplicates)
                    {
                        skipped++;
                        continue;
                    }
                    throw new ArgumentException($"Trí nhớ trùng nội dung: {content}");
                }

                var now = DateTimeOffset.UtcNow;
                var createdAt = item.CreatedAt ?? now;
                var updatedAt = item.UpdatedAt ?? createdAt;
                var retention = NormalizeRetention(item.Retention);
                var expiresAt = ResolveExpiration(retention, item.ExpiresAt, null, createdAt, allowPast: true);
                staged.Add(new StoredPersonalMemory
                {
                    Id = Guid.NewGuid(),
                    Kind = kind,
                    EncryptedContent = _protector.Protect(content),
                    CreatedAt = createdAt,
                    UpdatedAt = updatedAt,
                    IsEnabled = item.IsEnabled,
                    Retention = retention,
                    ExpiresAt = expiresAt,
                    WorkspaceId = _workspaceContext.CurrentWorkspaceId
                });
                imported++;
            }

            if (staged.Count > 0)
            {
                _memories.AddRange(staged);
                await PersistAsync(cancellationToken);
            }

            return new PersonalMemoryImportResult(imported, skipped, items.Count);
        }
        finally { _gate.Release(); }
    }

    private IEnumerable<PersonalMemory> PublicMemories()
    {
        var now = DateTimeOffset.UtcNow;
        return _memories
            .Where(IsCurrentWorkspace)
            .Select(memory => ToPublicMemory(memory, now))
            .Where(memory => memory is not null)
            .Cast<PersonalMemory>();
    }

    private bool IsCurrentWorkspace(StoredPersonalMemory memory) =>
        string.Equals(
            NormalizeWorkspaceId(memory.WorkspaceId),
            _workspaceContext.CurrentWorkspaceId,
            StringComparison.OrdinalIgnoreCase);

    private static string NormalizeWorkspaceId(string? workspaceId) =>
        string.IsNullOrWhiteSpace(workspaceId)
            ? PersonalWorkspaceIds.Personal
            : workspaceId.Trim().ToLowerInvariant();

    private PersonalMemory? FindDuplicate(string content, Guid? exceptId = null) =>
        PublicMemories().FirstOrDefault(memory => memory.Id != exceptId && NormalizeContent(memory.Content) == NormalizeContent(content));

    private PersonalMemory? FindUpdateCandidate(string content, string kind)
    {
        var incomingTokens = SimilarityTokens(content);
        if (incomingTokens.Count < 2) return null;

        return PublicMemories()
            .Where(memory => memory.Kind == kind && !memory.IsExpired)
            .Select(memory => new { Memory = memory, Score = SimilarityScore(incomingTokens, SimilarityTokens(memory.Content)) })
            .Where(item => item.Score >= 0.65)
            .OrderByDescending(item => item.Score)
            .ThenByDescending(item => item.Memory.UpdatedAt)
            .Select(item => item.Memory)
            .FirstOrDefault();
    }

    private static double SimilarityScore(HashSet<string> left, HashSet<string> right)
    {
        if (left.Count == 0 || right.Count == 0) return 0;
        var shared = left.Count(right.Contains);
        if (shared < 2) return 0;
        return (double)shared / Math.Min(left.Count, right.Count);
    }

    private static HashSet<string> SimilarityTokens(string value) =>
        Regex.Matches(value.ToLowerInvariant(), @"[\p{L}\p{N}]+")
            .Cast<Match>()
            .Select(match => match.Value)
            .Where(token => token.Length >= 2 && !SimilarityStopWords.Contains(token))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static string NormalizeContent(string value) =>
        string.Join(' ', value.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();

    private static string ValidateContent(string? value)
    {
        var content = value?.Trim() ?? string.Empty;
        if (content.Length is < 3 or > 1_000) throw new ArgumentException("Nội dung trí nhớ phải có từ 3 đến 1.000 ký tự.");
        return content;
    }

    private List<StoredPersonalMemory> LoadMemories()
    {
        if (!File.Exists(_memoryPath)) return [];
        try { return JsonSerializer.Deserialize<List<StoredPersonalMemory>>(File.ReadAllText(_memoryPath)) ?? []; }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            _logger.LogWarning(exception, "Could not read personal memories. Starting with an empty store.");
            return [];
        }
    }

    private PersonalMemory? ToPublicMemory(StoredPersonalMemory stored, DateTimeOffset? now = null)
    {
        try
        {
            var retention = NormalizeStoredRetention(stored.Retention);
            var current = now ?? DateTimeOffset.UtcNow;
            var expired = retention == "temporary" && stored.ExpiresAt.HasValue && stored.ExpiresAt.Value <= current;
            var stale = !expired && IsStale(stored.Kind, stored.UpdatedAt, current);
            return new PersonalMemory(
                stored.Id,
                NormalizeKind(stored.Kind),
                _protector.Unprotect(stored.EncryptedContent),
                stored.CreatedAt,
                stored.UpdatedAt,
                stored.IsEnabled,
                retention,
                stored.ExpiresAt,
                expired,
                stale,
                NormalizeWorkspaceId(stored.WorkspaceId));
        }
        catch (Exception exception) when (exception is CryptographicException or ArgumentException)
        {
            _logger.LogWarning(exception, "Could not read personal memory {MemoryId}.", stored.Id);
            return null;
        }
    }

    private async Task PersistAsync(CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(_memories, new JsonSerializerOptions { WriteIndented = true });
        var temporaryPath = $"{_memoryPath}.tmp";
        await File.WriteAllTextAsync(temporaryPath, json, cancellationToken);
        File.Move(temporaryPath, _memoryPath, true);
    }

    private static bool IsStale(string kind, DateTimeOffset updatedAt, DateTimeOffset now)
    {
        if (kind.Equals("rule", StringComparison.OrdinalIgnoreCase)) return false;
        var threshold = kind.Equals("preference", StringComparison.OrdinalIgnoreCase)
            ? TimeSpan.FromDays(365)
            : TimeSpan.FromDays(180);
        return now - updatedAt >= threshold;
    }

    private static string NormalizeKind(string? kind)
    {
        var normalized = kind?.Trim().ToLowerInvariant() ?? "fact";
        if (!SupportedKinds.Contains(normalized)) throw new ArgumentException("Loại trí nhớ không hợp lệ.");
        return normalized;
    }

    private static string NormalizeRetention(string? retention)
    {
        var normalized = string.IsNullOrWhiteSpace(retention) ? "long-term" : retention.Trim().ToLowerInvariant();
        if (!SupportedRetentions.Contains(normalized)) throw new ArgumentException("Vòng đời trí nhớ không hợp lệ.");
        return normalized;
    }

    private static string NormalizeStoredRetention(string? retention) =>
        SupportedRetentions.Contains(retention ?? string.Empty) ? retention!.ToLowerInvariant() : "long-term";

    private static DateTimeOffset? ResolveExpiration(
        string retention,
        DateTimeOffset? requested,
        DateTimeOffset? existing,
        DateTimeOffset referenceTime,
        bool allowPast)
    {
        if (retention == "long-term")
        {
            if (requested.HasValue) throw new ArgumentException("Trí nhớ dài hạn không dùng ngày hết hạn.");
            return null;
        }

        var expiresAt = requested ?? existing ?? referenceTime.AddDays(DefaultTemporaryDays);
        if (!allowPast && expiresAt <= DateTimeOffset.UtcNow)
            throw new ArgumentException("Ngày hết hạn của trí nhớ tạm thời phải nằm trong tương lai.");
        return expiresAt;
    }
}

public sealed class MemoryOverwriteConfirmationException(Guid duplicateMemoryId, string message) : Exception(message)
{
    public Guid DuplicateMemoryId { get; } = duplicateMemoryId;
}

public sealed class MemoryUpdateSuggestionException(Guid candidateMemoryId, string candidateContent, string message) : Exception(message)
{
    public Guid CandidateMemoryId { get; } = candidateMemoryId;
    public string CandidateContent { get; } = candidateContent;
}

internal sealed class StoredPersonalMemory
{
    public Guid Id { get; set; }
    public string Kind { get; set; } = "fact";
    public string EncryptedContent { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public bool IsEnabled { get; set; } = true;
    public string Retention { get; set; } = "long-term";
    public DateTimeOffset? ExpiresAt { get; set; }
    public string WorkspaceId { get; set; } = PersonalWorkspaceIds.Personal;
}
