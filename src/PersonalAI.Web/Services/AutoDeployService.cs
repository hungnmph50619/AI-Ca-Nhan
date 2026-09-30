using PersonalAI.Web.Models;
using PersonalAI.Web.SelfImprovement;

namespace PersonalAI.Web.Services;

public interface IAutoDeployService
{
    Task<AutoDeployResult> RunAsync(
        RunAutoDeployRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class AutoDeployService(
    IDevelopmentAgentService development,
    IWorkspaceContextAccessor workspace,
    IAuditRecorder audit) : IAutoDeployService
{
    public async Task<AutoDeployResult> RunAsync(
        RunAutoDeployRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Experiment);
        ArgumentNullException.ThrowIfNull(request.SelfCoding);
        ArgumentNullException.ThrowIfNull(request.Review);
        ArgumentNullException.ThrowIfNull(request.AutoTest);

        ValidateSequence(request);

        if (!request.ConfirmHumanApproval)
        {
            throw new AutoDeployValidationException(
                "Auto Deploy cần human approval rõ ràng trước khi tạo deployment candidate.");
        }

        var currentBranch = await development.GetCurrentBranchAsync(
            request.Experiment.RepositoryPath,
            cancellationToken);
        if (!currentBranch.Succeeded ||
            !string.Equals(
                currentBranch.Branch,
                request.Experiment.ExperimentBranch,
                StringComparison.Ordinal))
        {
            throw new AutoDeployValidationException(
                "Auto Deploy chỉ chạy khi repository đang ở đúng experiment branch.");
        }

        var startedAt = DateTimeOffset.UtcNow;
        var deploymentId =
            $"candidate-{SafeId(request.Experiment.ExperimentId)}-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";

        audit.Record(
            AuditAgents.User,
            "self-improvement.auto-deploy.prepare",
            $"deployment:{deploymentId}",
            "explicit-human-approval",
            AuditResults.Prepared);

        var publish = await development.DotnetPublishCandidateAsync(
            request.DotnetTargetPath,
            request.Configuration,
            deploymentId,
            cancellationToken);

        var candidatePublished = publish.Succeeded;
        var status = candidatePublished
            ? AutoDeployStatuses.CandidatePublished
            : AutoDeployStatuses.PublishFailed;

        audit.Record(
            AuditAgents.System,
            "self-improvement.auto-deploy.publish-candidate",
            $"deployment:{deploymentId}",
            "isolated-local-deployment-candidate",
            candidatePublished
                ? AuditResults.Succeeded
                : AuditResults.Failed);

        return new AutoDeployResult(
            PersonalAiRelease.Version,
            workspace.CurrentWorkspaceId,
            request.Experiment.ExperimentId,
            request.Experiment.ExperimentBranch,
            deploymentId,
            publish.DeploymentSlot,
            startedAt,
            DateTimeOffset.UtcNow,
            publish,
            status,
            HumanApproved: true,
            CandidatePublished: candidatePublished,
            ProductionActivated: false,
            CommitCreated: false,
            Pushed: false,
            Merged: false);
    }

    private void ValidateSequence(RunAutoDeployRequest request)
    {
        foreach (var workspaceId in new[]
        {
            request.Experiment.WorkspaceId,
            request.SelfCoding.WorkspaceId,
            request.Review.WorkspaceId,
            request.AutoTest.WorkspaceId
        })
        {
            if (!string.Equals(
                workspaceId,
                workspace.CurrentWorkspaceId,
                StringComparison.OrdinalIgnoreCase))
            {
                throw new AutoDeployValidationException(
                    "Experiment/Self Coding/Review/Auto Test không thuộc workspace hiện tại.");
            }
        }

        if (!string.Equals(
                request.Experiment.ExperimentId,
                request.SelfCoding.ExperimentId,
                StringComparison.Ordinal) ||
            !string.Equals(
                request.Experiment.ExperimentId,
                request.Review.ExperimentId,
                StringComparison.Ordinal) ||
            !string.Equals(
                request.Experiment.ExperimentId,
                request.AutoTest.ExperimentId,
                StringComparison.Ordinal) ||
            !string.Equals(
                request.Experiment.ExperimentBranch,
                request.SelfCoding.Branch,
                StringComparison.Ordinal) ||
            !string.Equals(
                request.Experiment.ExperimentBranch,
                request.Review.Branch,
                StringComparison.Ordinal) ||
            !string.Equals(
                request.Experiment.ExperimentBranch,
                request.AutoTest.Branch,
                StringComparison.Ordinal))
        {
            throw new AutoDeployValidationException(
                "Các stage Self Improvement không khớp experiment ID/branch.");
        }

        if (!request.Experiment.ExperimentBranch.StartsWith(
            "experiment/",
            StringComparison.Ordinal))
        {
            throw new AutoDeployValidationException(
                "Auto Deploy chỉ nhận branch experiment/*.");
        }

        if (!request.Review.ReadyForHumanDecision ||
            !request.AutoTest.ReadyForPromotionReview ||
            request.AutoTest.Status != AutoTestStatuses.ReadyForPromotionReview)
        {
            throw new AutoDeployValidationException(
                "Automated Review và Auto Test phải đạt trạng thái sẵn sàng trước Auto Deploy.");
        }

        if (request.AutoTest.FailedCases != 0 ||
            request.AutoTest.ExecutedCases < 1)
        {
            throw new AutoDeployValidationException(
                "Auto Deploy yêu cầu ít nhất một test đã chạy và không có test fail.");
        }

        if (request.SelfCoding.CommitCreated ||
            request.SelfCoding.Pushed ||
            request.SelfCoding.Merged ||
            request.Review.CommitCreated ||
            request.Review.Pushed ||
            request.Review.Merged ||
            request.Review.Deployed ||
            request.AutoTest.CommitCreated ||
            request.AutoTest.Pushed ||
            request.AutoTest.Merged ||
            request.AutoTest.Deployed)
        {
            throw new AutoDeployValidationException(
                "v2.4.6 chỉ publish candidate trước commit/push/merge/production activation.");
        }

        if (string.IsNullOrWhiteSpace(request.DotnetTargetPath))
        {
            throw new AutoDeployValidationException(
                "Cần chỉ định project/solution để publish deployment candidate.");
        }
    }

    private static string SafeId(string value)
    {
        var chars = (value ?? string.Empty)
            .ToLowerInvariant()
            .Where(character =>
                char.IsLetterOrDigit(character) || character == '-')
            .Take(48)
            .ToArray();

        var result = new string(chars).Trim('-');
        return result.Length >= 3 ? result : "experiment";
    }
}
