namespace PersonalAI.Web.Models;

public sealed record PersonalMemory(
    Guid Id,
    string Kind,
    string Content,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    bool IsEnabled = true);

public sealed record CreatePersonalMemoryRequest(
    string? Kind,
    string? Content,
    bool IsEnabled = true);

public sealed record UpdatePersonalMemoryRequest(
    string? Kind,
    string? Content,
    bool ConfirmOverwrite = false,
    bool? IsEnabled = null);

public sealed record SetPersonalMemoryEnabledRequest(bool IsEnabled);

public sealed record ImportPersonalMemoryItem(
    string? Kind,
    string? Content,
    bool IsEnabled = true,
    DateTimeOffset? CreatedAt = null,
    DateTimeOffset? UpdatedAt = null);

public sealed record ImportPersonalMemoriesRequest(
    IReadOnlyList<ImportPersonalMemoryItem>? Memories,
    bool SkipDuplicates = true);

public sealed record PersonalMemoryImportResult(
    int Imported,
    int Skipped,
    int Total);

public sealed record PersonalMemoryStats(
    int Total,
    int Enabled,
    int Disabled,
    int Facts,
    int Preferences,
    int Rules,
    DateTimeOffset? OldestCreatedAt,
    DateTimeOffset? LatestUpdatedAt);

public sealed record PersonalMemoryExport(
    int Version,
    DateTimeOffset ExportedAt,
    IReadOnlyList<PersonalMemory> Memories);
