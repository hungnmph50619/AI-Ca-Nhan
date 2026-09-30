using PersonalAI.Web.Models;

namespace PersonalAI.Web.SelfImprovement;

public sealed record RunAutoDeployRequest(
    AutomatedExperimentPlan Experiment,
    SelfCodingResult SelfCoding,
    AutomatedReviewResult Review,
    AutoTestResult AutoTest,
    string DotnetTargetPath,
    string Configuration = "Release",
    bool ConfirmHumanApproval = false);

public sealed record AutoDeployResult(
    string Version,
    string WorkspaceId,
    string ExperimentId,
    string Branch,
    string DeploymentId,
    string DeploymentSlot,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    DevelopmentPublishResult Publish,
    string Status,
    bool HumanApproved,
    bool CandidatePublished,
    bool ProductionActivated,
    bool CommitCreated,
    bool Pushed,
    bool Merged);

public static class AutoDeployStatuses
{
    public const string CandidatePublished = "candidate-published";
    public const string PublishFailed = "publish-failed";
}

public sealed class AutoDeployValidationException(string message)
    : Exception(message);
