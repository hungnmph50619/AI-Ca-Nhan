namespace PersonalAI.Web.Services;

public static class UniversalExecutionLifecycleActions
{
    public const string Complete = "complete";
    public const string VerifyOutcome = "verify-outcome";
    public const string ReplanGoal = "replan-goal";
    public const string Stop = "stop";
}

public sealed record UniversalExecutionLifecycleRequest(
    string Goal,
    string? PreferredChannel = null,
    bool ConfirmExecution = false,
    UniversalVerificationContinuationContext? VerificationContext = null);

public sealed record UniversalExecutionLifecycleResult(
    UniversalTaskRouteExecution Execution,
    UniversalOutcomeVerificationResult Verification,
    string Action,
    bool GoalComplete,
    string Reason,
    UniversalVerificationContinuationPlan? VerificationPlan = null,
    UniversalVerificationContinuationExecution? ContinuationExecution = null);

public interface IUniversalExecutionLifecycleCoordinator
{
    Task<UniversalExecutionLifecycleResult> ExecuteAsync(
        UniversalExecutionLifecycleRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class UniversalExecutionLifecycleCoordinator(
    IUniversalTaskRouter router,
    IUniversalOutcomeVerificationService verification,
    IUniversalVerificationContinuationPlanner? continuationPlanner = null,
    IUniversalVerificationContinuationExecutor? continuationExecutor = null)
    : IUniversalExecutionLifecycleCoordinator
{
    private readonly IUniversalVerificationContinuationPlanner continuationPlanner =
        continuationPlanner ?? new UniversalVerificationContinuationPlanner();

    private readonly IUniversalVerificationContinuationExecutor? continuationExecutor =
        continuationExecutor;
    public async Task<UniversalExecutionLifecycleResult> ExecuteAsync(
        UniversalExecutionLifecycleRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var execution = await router.ExecuteAsync(
            new UniversalTaskRouteRequest(
                request.Goal,
                request.PreferredChannel,
                request.ConfirmExecution),
            cancellationToken);

        var verified = verification.VerifyAgent(
            new UniversalAgentOutcomeVerificationRequest(
                execution.Route.Goal,
                execution.Gateway.Result));

        var action = DetermineAction(
            verified);

        var verificationPlan =
            continuationPlanner.Plan(
                execution.Route,
                verified);

        UniversalVerificationContinuationExecution? continuationExecution =
            null;

        if (action ==
                UniversalExecutionLifecycleActions.VerifyOutcome &&
            verificationPlan.Required &&
            continuationExecutor is not null)
        {
            continuationExecution =
                await continuationExecutor.ExecuteAsync(
                    verificationPlan,
                    execution.Route,
                    execution.Gateway.Result,
                    request.VerificationContext,
                    cancellationToken);

            if (continuationExecution.Evidence is not null)
            {
                verified = verification.VerifyAgent(
                    new UniversalAgentOutcomeVerificationRequest(
                        execution.Route.Goal,
                        execution.Gateway.Result,
                        continuationExecution.Evidence));

                action = DetermineAction(
                    verified);

                verificationPlan =
                    continuationPlanner.Plan(
                        execution.Route,
                        verified);
            }
        }

        return new(
            execution,
            verified,
            action,
            GoalComplete:
                action == UniversalExecutionLifecycleActions.Complete,
            action switch
            {
                UniversalExecutionLifecycleActions.Complete =>
                    "Execution đã được outcome verifier xác nhận; goal được phép complete.",

                UniversalExecutionLifecycleActions.VerifyOutcome =>
                    continuationExecution is null
                        ? $"Execution đã chạy nhưng evidence chưa đủ; continuation={verificationPlan.Strategy}."
                        : $"Continuation verification chưa đủ evidence: {continuationExecution.Reason}",

                UniversalExecutionLifecycleActions.ReplanGoal =>
                    "Verifier xác nhận goal chưa đạt; phải replan trước action tiếp theo.",

                _ =>
                    $"Execution lifecycle dừng ở trạng thái verification={verified.Status}."
            },
            verificationPlan,
            continuationExecution);
    }

    private static string DetermineAction(
        UniversalOutcomeVerificationResult verified) =>
        verified.Status switch
        {
            UniversalOutcomeStatuses.Verified
                when verified.GoalAchieved =>
                    UniversalExecutionLifecycleActions.Complete,

            UniversalOutcomeStatuses.NeedsVerification =>
                UniversalExecutionLifecycleActions.VerifyOutcome,

            UniversalOutcomeStatuses.NotAchieved =>
                UniversalExecutionLifecycleActions.ReplanGoal,

            UniversalOutcomeStatuses.ExecutionFailed =>
                UniversalExecutionLifecycleActions.Stop,

            _ =>
                UniversalExecutionLifecycleActions.Stop
        };
}
