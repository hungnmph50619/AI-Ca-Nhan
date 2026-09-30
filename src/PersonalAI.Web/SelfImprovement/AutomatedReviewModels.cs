using PersonalAI.Web.Models;

namespace PersonalAI.Web.SelfImprovement;

public sealed record RunAutomatedReviewRequest(
    AutomatedExperimentPlan Experiment,
    SelfCodingResult SelfCoding,
    string DotnetTargetPath,
    string Configuration = "Debug",
    bool RunTests = true,
    bool ConfirmCodeExecution = false,
    bool ConfirmExternalReviewer = false);

public sealed record AutomatedReviewCheck(
    string Name,
    string Status,
    string Detail);

public sealed record AutomatedReviewResult(
    string Version,
    string WorkspaceId,
    string ExperimentId,
    string Branch,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    IReadOnlyList<AutomatedReviewCheck> Checks,
    DevelopmentProcessResult Build,
    DevelopmentProcessResult? Tests,
    ReviewerReport? Reviewer,
    string? ReviewerProvider,
    string? ReviewerModel,
    string DecisionStatus,
    bool ReadyForHumanDecision,
    bool Approved,
    bool CommitCreated,
    bool Pushed,
    bool Merged,
    bool Deployed);

public static class AutomatedReviewDecisionStatuses
{
    public const string ReadyForHumanReview = "ready-for-human-review";
    public const string ChangesRequired = "changes-required";
}

public sealed class AutomatedReviewValidationException(string message)
    : Exception(message);
