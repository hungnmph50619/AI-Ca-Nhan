namespace PersonalAI.Web.Models;

public static class DevelopmentBenchmarkGateNames
{
    public const string Rag = "rag";
    public const string Tool = "tool";
    public const string Task = "task";
    public const string Agent = "agent";
    public const string Performance = "performance";
    public const string Resource = "resource";

    public static readonly IReadOnlyList<string> Required =
    [
        Rag,
        Tool,
        Task,
        Agent,
        Performance,
        Resource
    ];
}

public sealed record RunDevelopmentBenchmarkRequest(
    Guid DevelopmentRunId,
    int MaximumCases = 50,
    bool IncludeAgentBenchmark = false,
    bool ConfirmExternalExecution = false,
    double MinimumRagScore = 0.8,
    double MinimumToolScore = 0.8,
    double MinimumTaskScore = 0.8,
    double MinimumAgentPassRate = 0.8,
    double MaximumAverageAgentLatencyMs = 5000,
    int MaximumDurationMs = 30000,
    long MaximumWorkingSetBytes = 1073741824,
    bool ConfirmExecution = false);

public sealed record DevelopmentBenchmarkGateResult(
    string Gate,
    bool Passed,
    double? Observed,
    double? Threshold,
    string Detail);

public sealed record DevelopmentBenchmarkReport(
    Guid Id,
    string WorkspaceId,
    Guid DevelopmentRunId,
    IReadOnlyList<DevelopmentBenchmarkGateResult> Gates,
    int EvaluatedCases,
    int FailedCases,
    bool Passed,
    long DurationMs,
    long WorkingSetBytes,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt);

public sealed record DevelopmentBenchmarkStatus(
    string Version,
    string WorkspaceId,
    int Reports,
    int PassedReports,
    int FailedReports,
    bool ReportsPersisted,
    bool FailedBenchmarkBlocksContinuation,
    IReadOnlyList<string> RequiredGates);

public sealed class DevelopmentBenchmarkValidationException(string message)
    : Exception(message);
