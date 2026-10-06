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
