namespace PersonalAI.Web.Models;

public static class LifeContextSourceKinds
{
    public const string Device = "device";
    public const string Calendar = "calendar";
    public const string Location = "location";
    public const string Activity = "activity";
    public const string Manual = "manual";
    public const string Other = "other";

    public static readonly IReadOnlySet<string> All =
        new HashSet<string>(
            [Device, Calendar, Location, Activity, Manual, Other],
            StringComparer.OrdinalIgnoreCase);
}

public sealed record LifeContextStatusResponse(
    string Version,
    bool Supported,
    bool AutomaticCollectionEnabled,
    bool ContentEncrypted,
    int MaximumSourcesPerWorkspace,
    int MaximumEntriesPerWorkspace,
    int MaximumContentCharacters,
    int MinimumRetentionDays,
    int MaximumRetentionDays,
    IReadOnlyList<string> SupportedSourceKinds,
    IReadOnlyList<string> Limitations);

public sealed record LifeContextSource(
    Guid Id,
    string WorkspaceId,
    string Name,
    string Kind,
    bool Enabled,
    bool ConsentGranted,
    int RetentionDays,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? LastEntryAt,
    int EntryCount);

public sealed record LifeContextEntry(
    Guid Id,
    Guid SourceId,
    string WorkspaceId,
    string SourceName,
    string SourceKind,
    string Content,
    DateTimeOffset CapturedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset CreatedAt);

public sealed record CreateLifeContextSourceRequest(
    string Name,
    string Kind,
    int RetentionDays,
    bool ConsentGranted = false,
    bool Confirmed = false);

public sealed record SetLifeContextSourceEnabledRequest(
    bool Enabled,
    bool Confirmed = false);

public sealed record SetLifeContextConsentRequest(
    bool Granted,
    bool PurgeExisting = true,
    bool Confirmed = false);

public sealed record CreateLifeContextEntryRequest(
    string Content,
    DateTimeOffset? CapturedAt = null,
    bool Confirmed = false);

public sealed record DeleteLifeContextSourceRequest(
    bool Confirmed = false);

public sealed record LifeContextSourceListResponse(
    string WorkspaceId,
    int MaximumSources,
    IReadOnlyList<LifeContextSource> Sources);

public sealed record LifeContextEntryListResponse(
    string WorkspaceId,
    int MaximumEntries,
    IReadOnlyList<LifeContextEntry> Entries);

public sealed record LifeContextSelection(
    LifeContextEntry Entry,
    double Score,
    string Reason);
