namespace PersonalAI.Web.Models;

public static class DevelopmentTestPhaseNames
{
    public const string Restore = "restore";
    public const string Build = "build";
    public const string Unit = "unit";
    public const string Integration = "integration";
    public const string Regression = "regression";
}

public sealed record RunDevelopmentTestsRequest(
    Guid DevelopmentRunId,
    string DotnetTargetPath = "PersonalAI.sln",
    string? UnitTestTargetPath = null,
    string? IntegrationTestTargetPath = null,
    int MaximumRegressionCases = 50,
    bool ConfirmExecution = false);

public sealed record DevelopmentTestPhaseResult(
    string Phase,
    string Status,
    int? ExitCode,
    bool TimedOut,
    int DurationMs,
    string? FailureLog,
    bool FailureLogTruncated,
    int ExecutedCases = 0,
    int PassedCases = 0,
    int FailedCases = 0,
    int SkippedCases = 0);

public sealed record DevelopmentTestReport(
    Guid Id,
    string WorkspaceId,
    Guid DevelopmentRunId,
    string RepositoryPath,
    string Branch,
    string WorktreePath,
    string DotnetTargetPath,
    IReadOnlyList<DevelopmentTestPhaseResult> Phases,
    bool Succeeded,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt);

public sealed record DevelopmentTestStatus(
    string Version,
    string WorkspaceId,
    int Reports,
    int PassedReports,
    int FailedReports,
    bool ReportsPersisted,
    bool FailureLogsPersisted,
    bool FailureLogsRedacted,
    bool WorktreeOnly,
    IReadOnlyList<string> Phases);

public sealed class DevelopmentTestValidationException(string message)
    : Exception(message);
