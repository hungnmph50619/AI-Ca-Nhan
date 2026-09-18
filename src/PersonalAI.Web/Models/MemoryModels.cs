namespace PersonalAI.Web.Models;

public sealed record PersonalMemory(
    Guid Id,
    string Kind,
    string Content,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    bool IsEnabled = true,
    string Retention = "long-term",
    DateTimeOffset? ExpiresAt = null,
    bool IsExpired = false,
    bool IsStale = false,
    string WorkspaceId = PersonalWorkspaceIds.Personal);

public sealed record CreatePersonalMemoryRequest(
    string? Kind,
    string? Content,
    bool IsEnabled = true,
    string? Retention = null,
    DateTimeOffset? ExpiresAt = null,
    bool ConfirmCreateSimilar = false);

public sealed record UpdatePersonalMemoryRequest(
    string? Kind,
    string? Content,
    bool ConfirmOverwrite = false,
    string? Retention = null,
    DateTimeOffset? ExpiresAt = null);

public sealed record SetPersonalMemoryEnabledRequest(bool IsEnabled);

public sealed record ImportPersonalMemoryItem(
    string? Kind,
    string? Content,
    bool IsEnabled = true,
    DateTimeOffset? CreatedAt = null,
    DateTimeOffset? UpdatedAt = null,
    string? Retention = null,
    DateTimeOffset? ExpiresAt = null);

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
    int LongTerm,
    int Temporary,
    int Expired,
    int Stale,
    DateTimeOffset? OldestCreatedAt,
    DateTimeOffset? LatestUpdatedAt);

public sealed record PersonalMemoryExport(
    int Version,
    DateTimeOffset ExportedAt,
    IReadOnlyList<PersonalMemory> Memories);
