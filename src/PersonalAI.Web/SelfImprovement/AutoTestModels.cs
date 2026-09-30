using PersonalAI.Web.Evaluation.Benchmarks;
using PersonalAI.Web.Evaluation.Core;

namespace PersonalAI.Web.SelfImprovement;

public sealed record RunAutoTestRequest(
    AutomatedExperimentPlan Experiment,
    SelfCodingResult SelfCoding,
    AutomatedReviewResult Review,
    int? MaximumCases = null,
    bool IncludeAgentBenchmark = false,
    bool ConfirmExternalExecution = false);

public sealed record AutoTestCaseResult(
    string CaseId,
    string Title,
    string Category,
    string Status,
    double? Score,
    IReadOnlyList<string> Errors,
    string? Detail);

public sealed record AutoTestResult(
    string Version,
    string WorkspaceId,
    string ExperimentId,
    string Branch,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    int DatasetCasesRead,
    int ExecutedCases,
    int PassedCases,
    int FailedCases,
    int SkippedCases,
    IReadOnlyList<AutoTestCaseResult> Cases,
    EvaluationSummary? EvaluationSummary,
    AgentBenchmarkReport? AgentBenchmark,
    string Status,
    bool ReadyForPromotionReview,
    bool CommitCreated,
    bool Pushed,
    bool Merged,
    bool Deployed);

public static class AutoTestStatuses
{
    public const string Passed = "passed";
    public const string Failed = "failed";
    public const string Skipped = "skipped";
    public const string ReadyForPromotionReview = "ready-for-promotion-review";
    public const string ChangesRequired = "changes-required";
}

public sealed class AutoTestValidationException(string message)
    : Exception(message);
