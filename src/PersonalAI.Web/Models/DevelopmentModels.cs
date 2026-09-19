namespace PersonalAI.Web.Models;

public static class DevelopmentCapabilities
{
    public const string WorkspaceInspect = "workspace-inspect";
    public const string TextSearch = "text-search";
    public const string GitStatus = "git-status";
    public const string GitDiff = "git-diff";
    public const string DotnetRestore = "dotnet-restore";
    public const string DotnetBuild = "dotnet-build";
    public const string DotnetTest = "dotnet-test";
}

public sealed record DevelopmentStatusResponse(
    string Version,
    string WorkspaceId,
    bool GitAvailable,
    bool DotnetAvailable,
    bool ArbitraryShellEnabled,
    bool ArbitraryProcessEnabled,
    bool GitWriteActionsEnabled,
    int MaximumScannedFiles,
    int MaximumSearchHits,
    int MaximumProcessOutputCharacters,
    IReadOnlyList<string> AvailableCapabilities,
    IReadOnlyList<string> Limitations);

public sealed record DevelopmentProjectEntry(
    string Path,
    string Kind,
    long SizeBytes);

public sealed record DevelopmentWorkspaceInspection(
    string WorkspaceId,
    int ScannedFiles,
    bool Truncated,
    IReadOnlyList<DevelopmentProjectEntry> Projects,
    IReadOnlyList<string> DetectedLanguages,
    IReadOnlyList<string> RootFiles);

public sealed record DevelopmentSearchHit(
    string Path,
    int LineNumber,
    string Preview);

public sealed record DevelopmentSearchResult(
    string WorkspaceId,
    string Query,
    int ScannedFiles,
    int MatchCount,
    bool Truncated,
    IReadOnlyList<DevelopmentSearchHit> Hits);

public sealed record DevelopmentProcessResult(
    string Tool,
    string TargetPath,
    int ExitCode,
    bool Succeeded,
    bool TimedOut,
    int DurationMs,
    string Output,
    bool OutputTruncated);

public sealed record DevelopmentGitResult(
    string Command,
    int ExitCode,
    bool Succeeded,
    int DurationMs,
    string Output,
    bool OutputTruncated);
