using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public sealed class UniversalClosedLoopLiveStepRunner(
    IUniversalDirectToolPath directToolPath,
    IToolOrchestrationService orchestration,
    IUniversalVerificationPipeline verificationPipeline)
    : IUniversalClosedLoopStepRunner
{
    public async Task<UniversalClosedLoopStepResult> RunAsync(
        UniversalClosedLoopStepRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var prepared =
            await directToolPath.PrepareAsync(
                new UniversalExecutionPrepareRequest(
                    request.Goal),
                cancellationToken);

        if (prepared.Mode.Equals(
                UniversalExecutionModes.NeedsFurtherRouting,
                StringComparison.OrdinalIgnoreCase))
        {
            return new(
                "route",
                "needs-further-routing",
                Executed: false,
                GoalAchieved: false,
                RequiresConfirmation: false,
                NeedsVerification: false,
                ReplanRequired: true,
                CanContinue: false,
                prepared.Reason);
        }

        if (prepared.Mode.Equals(
                UniversalExecutionModes.ExecutionAgentFallback,
                StringComparison.OrdinalIgnoreCase))
        {
            return new(
                $"agent:{prepared.FallbackChannel ?? "unknown"}",
                "explicit-confirmation-required",
                Executed: false,
                GoalAchieved: false,
                RequiresConfirmation: true,
                NeedsVerification: false,
                ReplanRequired: false,
                CanContinue: false,
                "Execution agent fallback có side effect và phải chờ xác nhận rõ ràng.");
        }

        if (!prepared.Mode.Equals(
                UniversalExecutionModes.DirectToolProposal,
                StringComparison.OrdinalIgnoreCase) ||
            prepared.ToolProposal is null)
        {
            return new(
                "direct-tool",
                "invalid-prepare-result",
                Executed: false,
                GoalAchieved: false,
                RequiresConfirmation: false,
                NeedsVerification: false,
                ReplanRequired: false,
                CanContinue: false,
                "Direct Tool Path trả trạng thái không hợp lệ.");
        }

        var proposal = prepared.ToolProposal;

        if (proposal.RequiresConfirmation)
        {
            return new(
                $"tool:{proposal.ToolName}",
                "explicit-confirmation-required",
                Executed: false,
                GoalAchieved: false,
                RequiresConfirmation: true,
                NeedsVerification: false,
                ReplanRequired: false,
                CanContinue: false,
                $"Tool {proposal.ToolName} cần xác nhận; live closed-loop không tự vượt gate.");
        }

        var execution =
            await orchestration.ExecuteAsync(
                proposal.ProposalId,
                confirmed: false,
                cancellationToken);

        if (execution is null)
        {
            return new(
                $"tool:{proposal.ToolName}",
                "proposal-unavailable",
                Executed: false,
                GoalAchieved: false,
                RequiresConfirmation: false,
                NeedsVerification: false,
                ReplanRequired: true,
                CanContinue: false,
                "Proposal không còn khả dụng để thực thi.");
        }

        var verification =
            verificationPipeline.Verify(
                new UniversalVerificationPipelineRequest(
                    request.Goal,
                    prepared.Route,
                    execution.Execution));

        return MapVerification(
            proposal.ToolName,
            execution.Execution,
            verification);
    }

    private static UniversalClosedLoopStepResult MapVerification(
        string toolName,
        ToolExecutionResponse execution,
        UniversalVerificationPipelineResult verification)
    {
        var actionKey =
            $"tool:{toolName}";

        var outcomeKey =
            $"{execution.Status}:{verification.Verification.Status}:{verification.Decision.Action}";

        if (verification.Decision.Action.Equals(
                UniversalFallbackActions.Complete,
                StringComparison.OrdinalIgnoreCase))
        {
            return new(
                actionKey,
                outcomeKey,
                Executed: true,
                GoalAchieved: true,
                RequiresConfirmation: false,
                NeedsVerification: false,
                ReplanRequired: false,
                CanContinue: false,
                verification.Decision.Reason);
        }

        if (verification.Decision.Action.Equals(
                UniversalFallbackActions.VerifyOutcome,
                StringComparison.OrdinalIgnoreCase))
        {
            return new(
                actionKey,
                outcomeKey,
                Executed: true,
                GoalAchieved: false,
                RequiresConfirmation: false,
                NeedsVerification: true,
                ReplanRequired: false,
                CanContinue: false,
                verification.Decision.Reason);
        }

        if (verification.Decision.Action.Equals(
                UniversalFallbackActions.AgentFallback,
                StringComparison.OrdinalIgnoreCase))
        {
            return new(
                actionKey,
                outcomeKey,
                Executed: true,
                GoalAchieved: false,
                RequiresConfirmation: true,
                NeedsVerification: false,
                ReplanRequired: false,
                CanContinue: false,
                verification.Decision.Reason);
        }

        if (verification.Decision.Action is
            UniversalFallbackActions.ReplanArguments or
            UniversalFallbackActions.ReplanGoal or
            UniversalFallbackActions.Reroute)
        {
            return new(
                actionKey,
                outcomeKey,
                Executed: true,
                GoalAchieved: false,
                RequiresConfirmation: false,
                NeedsVerification: false,
                ReplanRequired: true,
                CanContinue: false,
                verification.Decision.Reason);
        }

        return new(
            actionKey,
            outcomeKey,
            Executed: true,
            GoalAchieved: false,
            RequiresConfirmation: false,
            NeedsVerification: false,
            ReplanRequired: false,
            CanContinue: false,
            verification.Decision.Reason);
    }
}
