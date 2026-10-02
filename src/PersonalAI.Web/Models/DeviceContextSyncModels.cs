namespace PersonalAI.Web.Models;

public static class DeviceContextKinds
{
    public const string Memory = "memory";
    public const string Task = "task";
    public const string LifeContext = "life-context";

    public static readonly IReadOnlySet<string> All =
        new HashSet<string>(
            [Memory, Task, LifeContext],
            StringComparer.Ordinal);
}

public sealed record CreateDeviceContextSyncRequest(
    string Query,
    IReadOnlyList<string>? AllowedKinds = null,
    int? MaximumCharacters = null,
    bool ConfirmSync = false);

public sealed record DeviceContextSyncItem(
    string Kind,
    string SourceId,
    string Label,
    string Content,
    int CharacterCount);

public sealed record DeviceContextSyncPackage(
    Guid SyncId,
    Guid CorrelationId,
    string WorkspaceId,
    Guid DeviceId,
    string Query,
    IReadOnlyList<string> AllowedKinds,
    int MaximumCharacters,
    int UsedCharacters,
    bool FullMemorySync,
    DateTimeOffset CreatedAt,
    IReadOnlyList<DeviceContextSyncItem> Items);

public sealed record DeviceContextSyncStatus(
    string Version,
    string WorkspaceId,
    bool ExplicitConfirmationRequired,
    bool FullMemorySyncEnabled,
    bool QueryScopedSelection,
    bool DeviceRouteBindingRequired,
    bool OnlineDeviceRequired,
    bool CapabilityPermissionRevalidated,
    int DefaultMaximumCharacters,
    int HardMaximumCharacters,
    IReadOnlyList<string> SupportedKinds);

public sealed class DeviceContextSyncValidationException(string message)
    : Exception(message);
