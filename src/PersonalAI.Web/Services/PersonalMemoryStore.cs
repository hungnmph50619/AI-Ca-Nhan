using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public interface IPersonalMemoryStore
{
    Task<IReadOnlyList<PersonalMemory>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<PersonalMemory> AddAsync(
        CreatePersonalMemoryRequest request,
        CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(Guid memoryId, CancellationToken cancellationToken = default);
}

public sealed class PersonalMemoryStore : IPersonalMemoryStore
{
    private static readonly HashSet<string> SupportedKinds =
        new(StringComparer.OrdinalIgnoreCase) { "fact", "preference", "rule" };

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly IDataProtector _protector;
    private readonly ILogger<PersonalMemoryStore> _logger;
    private readonly string _memoryPath;
    private List<StoredPersonalMemory> _memories;

    public PersonalMemoryStore(
        IDataProtectionProvider dataProtectionProvider,
        ILogger<PersonalMemoryStore> logger)
    {
        _protector = dataProtectionProvider.CreateProtector("PersonalAI.PersonalMemory.v1");
        _logger = logger;

        var localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localData))
        {
            localData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".personalai");
        }

        var memoryDirectory = Path.Combine(localData, "PersonalAI", "Memory");
        Directory.CreateDirectory(memoryDirectory);
        _memoryPath = Path.Combine(memoryDirectory, "memories.json");
        _memories = LoadMemories();
    }

    public async Task<IReadOnlyList<PersonalMemory>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return _memories
                .Select(ToPublicMemory)
                .Where(memory => memory is not null)
                .Cast<PersonalMemory>()
                .OrderByDescending(memory => memory.UpdatedAt)
                .ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<PersonalMemory> AddAsync(
        CreatePersonalMemoryRequest request,
        CancellationToken cancellationToken = default)
    {
        var content = request.Content?.Trim() ?? string.Empty;
        if (content.Length is < 3 or > 1_000)
        {
            throw new ArgumentException("Nội dung trí nhớ phải có từ 3 đến 1.000 ký tự.");
        }

        var kind = NormalizeKind(request.Kind);
        var now = DateTimeOffset.UtcNow;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var duplicate = _memories
                .Select(ToPublicMemory)
                .Where(memory => memory is not null)
                .Cast<PersonalMemory>()
                .Any(memory => memory.Content.Equals(content, StringComparison.OrdinalIgnoreCase));
            if (duplicate)
            {
                throw new ArgumentException("Nội dung này đã có trong trí nhớ.");
            }

            var stored = new StoredPersonalMemory
            {
                Id = Guid.NewGuid(),
                Kind = kind,
                EncryptedContent = _protector.Protect(content),
                CreatedAt = now,
                UpdatedAt = now
            };
            _memories.Add(stored);
            await PersistAsync(cancellationToken);
            return ToPublicMemory(stored)
                ?? throw new InvalidOperationException("Không thể đọc lại trí nhớ vừa lưu.");
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> DeleteAsync(
        Guid memoryId,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var removed = _memories.RemoveAll(memory => memory.Id == memoryId) > 0;
            if (!removed)
            {
                return false;
            }

            await PersistAsync(cancellationToken);
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    private List<StoredPersonalMemory> LoadMemories()
    {
        if (!File.Exists(_memoryPath))
        {
            return [];
        }

        try
        {
            var json = File.ReadAllText(_memoryPath);
            return JsonSerializer.Deserialize<List<StoredPersonalMemory>>(json) ?? [];
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException)
        {
            _logger.LogWarning(exception, "Could not read personal memories. Starting with an empty store.");
            return [];
        }
    }

    private PersonalMemory? ToPublicMemory(StoredPersonalMemory stored)
    {
        try
        {
            var content = _protector.Unprotect(stored.EncryptedContent);
            return new PersonalMemory(
                stored.Id,
                NormalizeKind(stored.Kind),
                content,
                stored.CreatedAt,
                stored.UpdatedAt);
        }
        catch (CryptographicException exception)
        {
            _logger.LogWarning(exception, "Could not decrypt personal memory {MemoryId}.", stored.Id);
            return null;
        }
    }

    private async Task PersistAsync(CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(_memories, new JsonSerializerOptions
        {
            WriteIndented = true
        });
        var temporaryPath = $"{_memoryPath}.tmp";
        await File.WriteAllTextAsync(temporaryPath, json, cancellationToken);
        File.Move(temporaryPath, _memoryPath, true);
    }

    private static string NormalizeKind(string? kind)
    {
        var normalized = kind?.Trim().ToLowerInvariant() ?? "fact";
        if (!SupportedKinds.Contains(normalized))
        {
            throw new ArgumentException("Loại trí nhớ không hợp lệ.");
        }

        return normalized;
    }
}

internal sealed class StoredPersonalMemory
{
    public Guid Id { get; set; }
    public string Kind { get; set; } = "fact";
    public string EncryptedContent { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
