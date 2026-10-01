namespace PersonalAI.Web.Models;

public static class DevelopmentCapabilities
{
    public const string WorkspaceInspect = "workspace-inspect";
    public const string TextSearch = "text-search";
    public const string GitStatus = "git-status";
    public const string GitDiff = "git-diff";
    public const string GitExperimentBranch = "git-experiment-branch";
    public const string GitFetch = "git-fetch";
    public const string GitBranch = "git-branch";
    public const string GitCheckout = "git-checkout";
    public const string GitLog = "git-log";
    public const string GitPullFastForwardOnly = "git-pull-ff-only";
    public const string GitCommit = "git-commit";
    public const string GitPush = "git-push";
    public const string GitWorktreeIsolation = "git-worktree-isolation";
    public const string DevelopmentLeases = "development-leases";
    public const string GitCredentialReferences = "git-credential-references";
    public const string GitHubWebhookEvents = "github-webhook-events";
    public const string CiMonitor = "ci-monitor";
    public const string DevelopmentRunOrchestration = "development-run-orchestration";
    public const string ImprovementBacklog = "improvement-backlog";
    public const string RootCauseDiagnosis = "root-cause-diagnosis";
    public const string SelfCodingWorktree = "self-coding-worktree";
    public const string DevelopmentAutoTest = "development-auto-test";
    public const string IndependentCodeReview = "independent-code-review";
    public const string DevelopmentSecurityGate = "development-security-gate";
    public const string AutomaticBenchmarkGate = "automatic-benchmark-gate";
    public const string GitHubPullRequestCi = "github-pull-request-ci";
    public const string MergePolicy = "merge-policy";
    public const string SafeLocalSync = "safe-local-sync";
    public const string PerRunWorktreeLifecycle = "per-run-worktree-lifecycle";
    public const string RestartSafeRecovery = "restart-safe-recovery";
    public const string ControlledDependencyMaintenance = "controlled-dependency-maintenance";
    public const string DotnetRestore = "dotnet-restore";
    public const string DotnetBuild = "dotnet-build";
    public const string DotnetTest = "dotnet-test";
    public const string DotnetPublishCandidate = "dotnet-publish-candidate";
    public const string DeploymentCandidateDiscard = "deployment-candidate-discard";
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
    IReadOnlyList<string> Limitations,
    bool ExperimentBranchActionsEnabled = false);

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


public sealed record DevelopmentBranchResult(
    string RepositoryPath,
    string Branch,
    bool Succeeded,
    int DurationMs,
    string Output);


public sealed record DevelopmentPublishResult(
    string DeploymentSlot,
    string TargetPath,
    int ExitCode,
    bool Succeeded,
    bool TimedOut,
    int DurationMs,
    string Output,
    bool OutputTruncated,
    int FileCount,
    long TotalBytes);


public sealed record DevelopmentDeploymentRollbackResult(
    string DeploymentSlot,
    bool Existed,
    bool RolledBack,
    DateTimeOffset CompletedAt);
