namespace PersonalAI.Web.Evaluation.Benchmarks;

public sealed record RunAgentBenchmarkRequest(
    bool ConfirmExternalExecution,
    IReadOnlyList<string>? CaseIds = null,
    int? MaximumCases = null);

public sealed record AgentBenchmarkCaseResult(
    string CaseId,
    string Title,
    string AgentId,
    bool Passed,
    double Score,
    double LatencyMilliseconds,
    string? Provider,
    string? Model,
    IReadOnlyList<string> Failures);

public sealed record AgentBenchmarkReport(
    string Version,
    string WorkspaceId,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    int RequestedCases,
    int ExecutedCases,
    int PassedCases,
    int FailedCases,
    double PassRate,
    double AverageLatencyMilliseconds,
    IReadOnlyList<AgentBenchmarkCaseResult> Cases);

public sealed class AgentBenchmarkValidationException(string message)
    : Exception(message);
