using PersonalAI.Web.Models;
using PersonalAI.Web.SelfImprovement;

namespace PersonalAI.Web.Services;

public interface IAutomaticRollbackService
{
    Task<AutomaticRollbackResult> RunAsync(
        RunAutomaticRollbackRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class AutomaticRollbackService(
    IDevelopmentAgentService development,
    IWorkspaceContextAccessor workspace,
    IAuditRecorder audit) : IAutomaticRollbackService
{
    public async Task<AutomaticRollbackResult> RunAsync(
        RunAutomaticRollbackRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Deployment);

        var deployment = request.Deployment;
        ValidateDeployment(deployment);

        if (!request.FailureDetected)
        {
            throw new AutomaticRollbackValidationException(
                "Automatic Rollback chỉ chạy khi failureDetected=true.");
        }

        var reason = (request.FailureReason ?? string.Empty).Trim();
        if (reason.Length is < 3 or > 500)
        {
            throw new AutomaticRollbackValidationException(
                "FailureReason phải có từ 3 đến 500 ký tự.");
        }

        var startedAt = DateTimeOffset.UtcNow;

        audit.Record(
            AuditAgents.System,
            "self-improvement.auto-rollback.detected",
            $"deployment:{deployment.DeploymentId}",
            reason,
            AuditResults.Prepared);

        var rollback = await development.DiscardDeploymentCandidateAsync(
            deployment.DeploymentId,
            cancellationToken);

        var status = rollback.RolledBack
            ? AutomaticRollbackStatuses.RolledBack
            : AutomaticRollbackStatuses.RollbackFailed;

        audit.Record(
            AuditAgents.System,
            "self-improvement.auto-rollback.execute",
            $"deployment:{deployment.DeploymentId}",
            reason,
            rollback.RolledBack
                ? AuditResults.Succeeded
                : AuditResults.Failed);

        return new AutomaticRollbackResult(
            PersonalAiRelease.Version,
            workspace.CurrentWorkspaceId,
            deployment.ExperimentId,
            deployment.DeploymentId,
            rollback.DeploymentSlot,
            reason,
            startedAt,
            DateTimeOffset.UtcNow,
            rollback,
            status,
            Automatic: true,
            ProductionWasActivated: false,
            GitHistoryChanged: false);
    }

    private void ValidateDeployment(AutoDeployResult deployment)
    {
        if (!string.Equals(
            deployment.WorkspaceId,
            workspace.CurrentWorkspaceId,
            StringComparison.OrdinalIgnoreCase))
        {
            throw new AutomaticRollbackValidationException(
                "Deployment không thuộc workspace hiện tại.");
        }

        if (!deployment.CandidatePublished ||
            deployment.Status != AutoDeployStatuses.CandidatePublished)
        {
            throw new AutomaticRollbackValidationException(
                "Chỉ rollback deployment candidate đã publish thành công.");
        }

        if (deployment.ProductionActivated ||
            deployment.CommitCreated ||
            deployment.Pushed ||
            deployment.Merged)
        {
            throw new AutomaticRollbackValidationException(
                "v2.4.7 chỉ rollback candidate local chưa activation/commit/push/merge.");
        }

        var expectedSlot =
            $"{workspace.CurrentWorkspaceId}/{deployment.DeploymentId}";
        if (!string.Equals(
            deployment.DeploymentSlot,
            expectedSlot,
            StringComparison.Ordinal))
        {
            throw new AutomaticRollbackValidationException(
                "Deployment slot không khớp deployment ID/workspace.");
        }

        if (string.IsNullOrWhiteSpace(deployment.ExperimentId) ||
            string.IsNullOrWhiteSpace(deployment.DeploymentId))
        {
            throw new AutomaticRollbackValidationException(
                "Deployment thiếu experiment/deployment ID.");
        }
    }
}
