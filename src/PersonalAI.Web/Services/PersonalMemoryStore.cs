using System.Security.Cryptography;
using System.Text.Json;
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
    private static readonly HashSet<string> SupportedKinds = new(StringComparer.OrdinalIgnoreCase) { "fact", "preference", "rule" };
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly IDataProtector _protector;
    private readonly ILogger<PersonalMemoryStore> _logger;
    private readonly string _memoryPath;
    private List<StoredPersonalMemory> _memories;

    public PersonalMemoryStore(IDataProtectionProvider dataProtectionProvider, ILogger<PersonalMemoryStore> logger)
    {
        _protector = dataProtectionProvider.CreateProtector("PersonalAI.PersonalMemory.v1");
        _logger = logger;
        var localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localData)) localData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".personalai");
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
        var now = DateTimeOffset.UtcNow;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (FindDuplicate(content) is not null) throw new ArgumentException("Nội dung này đã có trong trí nhớ.");
            var stored = new StoredPersonalMemory { Id = Guid.NewGuid(), Kind = kind, EncryptedContent = _protector.Protect(content), CreatedAt = now, UpdatedAt = now, IsEnabled = request.IsEnabled };
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
            var stored = _memories.FirstOrDefault(memory => memory.Id == memoryId);
            if (stored is null) return null;
            var duplicate = FindDuplicate(content, memoryId);
            if (duplicate is not null && !request.ConfirmOverwrite) throw new MemoryOverwriteConfirmationException(duplicate.Id, "Nội dung tương tự đã tồn tại. Hãy xác nhận trước khi cập nhật.");
            if (duplicate is not null && request.ConfirmOverwrite) _memories.RemoveAll(memory => memory.Id == duplicate.Id);
            stored.Kind = kind;
            stored.EncryptedContent = _protector.Protect(content);
            stored.UpdatedAt = DateTimeOffset.UtcNow;
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
            var stored = _memories.FirstOrDefault(memory => memory.Id == memoryId);
            if (stored is null) return null;
            stored.IsEnabled = isEnabled;
            stored.UpdatedAt = DateTimeOffset.UtcNow;
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
            var removed = _memories.RemoveAll(memory => memory.Id == memoryId) > 0;
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
            var count = _memories.Count;
            if (count == 0) return 0;
            _memories.Clear();
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
                staged.Add(new StoredPersonalMemory
                {
                    Id = Guid.NewGuid(),
                    Kind = kind,
                    EncryptedContent = _protector.Protect(content),
                    CreatedAt = createdAt,
                    UpdatedAt = updatedAt,
                    IsEnabled = item.IsEnabled
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

    private IEnumerable<PersonalMemory> PublicMemories() => _memories.Select(ToPublicMemory).Where(memory => memory is not null).Cast<PersonalMemory>();
    private PersonalMemory? FindDuplicate(string content, Guid? exceptId = null) => PublicMemories().FirstOrDefault(memory => memory.Id != exceptId && NormalizeContent(memory.Content) == NormalizeContent(content));
    private static string NormalizeContent(string value) => string.Join(' ', value.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();
    private static string ValidateContent(string? value) { var content = value?.Trim() ?? string.Empty; if (content.Length is < 3 or > 1_000) throw new ArgumentException("Nội dung trí nhớ phải có từ 3 đến 1.000 ký tự."); return content; }

    private List<StoredPersonalMemory> LoadMemories()
    {
        if (!File.Exists(_memoryPath)) return [];
        try { return JsonSerializer.Deserialize<List<StoredPersonalMemory>>(File.ReadAllText(_memoryPath)) ?? []; }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException) { _logger.LogWarning(exception, "Could not read personal memories. Starting with an empty store."); return []; }
    }

    private PersonalMemory? ToPublicMemory(StoredPersonalMemory stored)
    {
        try { return new PersonalMemory(stored.Id, NormalizeKind(stored.Kind), _protector.Unprotect(stored.EncryptedContent), stored.CreatedAt, stored.UpdatedAt, stored.IsEnabled); }
        catch (CryptographicException exception) { _logger.LogWarning(exception, "Could not decrypt personal memory {MemoryId}.", stored.Id); return null; }
    }

    private async Task PersistAsync(CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(_memories, new JsonSerializerOptions { WriteIndented = true });
        var temporaryPath = $"{_memoryPath}.tmp";
        await File.WriteAllTextAsync(temporaryPath, json, cancellationToken);
        File.Move(temporaryPath, _memoryPath, true);
    }

    private static string NormalizeKind(string? kind)
    {
        var normalized = kind?.Trim().ToLowerInvariant() ?? "fact";
        if (!SupportedKinds.Contains(normalized)) throw new ArgumentException("Loại trí nhớ không hợp lệ.");
        return normalized;
    }
}

public sealed class MemoryOverwriteConfirmationException(Guid duplicateMemoryId, string message) : Exception(message)
{
    public Guid DuplicateMemoryId { get; } = duplicateMemoryId;
}

internal sealed class StoredPersonalMemory
{
    public Guid Id { get; set; }
    public string Kind { get; set; } = "fact";
    public string EncryptedContent { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public bool IsEnabled { get; set; } = true;
}
