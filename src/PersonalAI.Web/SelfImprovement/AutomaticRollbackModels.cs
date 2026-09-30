using PersonalAI.Web.Models;

namespace PersonalAI.Web.SelfImprovement;

public sealed record RunAutomaticRollbackRequest(
    AutoDeployResult Deployment,
    bool FailureDetected,
    string FailureReason);

public sealed record AutomaticRollbackResult(
    string Version,
    string WorkspaceId,
    string ExperimentId,
    string DeploymentId,
    string DeploymentSlot,
    string FailureReason,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    DevelopmentDeploymentRollbackResult Rollback,
    string Status,
    bool Automatic,
    bool ProductionWasActivated,
    bool GitHistoryChanged);

public static class AutomaticRollbackStatuses
{
    public const string RolledBack = "rolled-back";
    public const string RollbackFailed = "rollback-failed";
}

public sealed class AutomaticRollbackValidationException(string message)
    : Exception(message);
