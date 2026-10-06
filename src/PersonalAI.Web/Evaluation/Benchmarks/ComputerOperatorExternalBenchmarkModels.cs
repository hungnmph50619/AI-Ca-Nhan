namespace PersonalAI.Web.Evaluation.Benchmarks;

public sealed record PrepareComputerOperatorExternalBenchmarkRequest(
    IReadOnlyList<string>? CaseIds = null,
    int? MaximumCases = null);

public sealed record ComputerOperatorExternalBenchmarkCasePlan(
    string CaseId,
    string Title,
    string Goal,
    string ExpectedEffect,
    int MaximumSteps,
    bool RequiresInteractiveDesktop);

public sealed record ComputerOperatorExternalBenchmarkPlan(
    string Version,
    string WorkspaceId,
    DateTimeOffset PreparedAtUtc,
    string Category,
    int RequestedCases,
    int PlannedCases,
    bool ReadyForExecution,
    IReadOnlyList<ComputerOperatorExternalBenchmarkCasePlan> Cases);

public sealed class ComputerOperatorExternalBenchmarkValidationException(
    string message)
    : Exception(message);


public sealed record RunComputerOperatorExternalBenchmarkCaseRequest(
    string CaseId,
    bool Confirmed);

public sealed record ComputerOperatorExternalBenchmarkExecutionResult(
    string Version,
    string WorkspaceId,
    string CaseId,
    string Title,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc,
    bool TaskCompleted,
    bool WithinStepBudget,
    bool ReadyForIndependentEvaluation,
    int MaximumSteps,
    int RecordedSteps,
    int ObservationCount,
    int ActionCount,
    string Summary,
    string Provider,
    string Model,
    double DurationMilliseconds);
