namespace PersonalAI.Web.Models;

public static class ImprovementSources
{
    public const string Error = "error";
    public const string Feedback = "feedback";
    public const string Ci = "ci";
    public const string Benchmark = "benchmark";
    public const string Security = "security";
    public const string Dependency = "dependency";

    public static readonly IReadOnlyList<string> Supported =
    [
        Error,
        Feedback,
        Ci,
        Benchmark,
        Security,
        Dependency
    ];
}

public static class ImprovementPriorities
{
    public const string Low = "low";
    public const string Medium = "medium";
    public const string High = "high";
    public const string Critical = "critical";
}

public static class ImprovementBacklogStates
{
    public const string Open = "open";
    public const string Planned = "planned";
    public const string InProgress = "in-progress";
    public const string Resolved = "resolved";
    public const string Dismissed = "dismissed";
}

public sealed record ImprovementEvidence(
    Guid Id,
    string SourceType,
    string SourceId,
    string Reference,
    string Summary,
    DateTimeOffset ObservedAt);

public sealed record CreateImprovementBacklogRequest(
    string SourceType,
    string SourceId,
    string Title,
    string Description,
    string Priority,
    string EvidenceReference,
    string EvidenceSummary);

public sealed record UpdateImprovementBacklogStateRequest(
    string State,
    string Reason);

public sealed record ImprovementBacklogItem(
    Guid Id,
    string WorkspaceId,
    string Fingerprint,
    string SourceType,
    string Title,
    string Description,
    string Priority,
    string State,
    int Occurrences,
    IReadOnlyList<ImprovementEvidence> Evidence,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset LastSeenAt,
    DateTimeOffset UpdatedAt);

public sealed record ImprovementBacklogStatus(
    string Version,
    string WorkspaceId,
    int Items,
    int OpenItems,
    int CriticalItems,
    int EvidenceRecords,
    bool DeduplicationEnabled,
    bool EvidenceTraceable,
    bool CiImportEnabled,
    IReadOnlyList<string> SupportedSources);

public sealed record ImportCiBacklogResult(
    int FailedCiRuns,
    int Created,
    int Deduplicated,
    IReadOnlyList<Guid> BacklogItemIds);

public sealed class ImprovementBacklogValidationException(string message)
    : Exception(message);
