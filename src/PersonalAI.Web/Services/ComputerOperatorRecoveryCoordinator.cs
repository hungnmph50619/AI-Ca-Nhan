namespace PersonalAI.Web.Services;

public static class ComputerOperatorRecoveryCoordinatorDecisions
{
    public const string Retry = "retry";
    public const string ChangeStrategy = "change-strategy";
    public const string Reobserve = "reobserve";
    public const string ResumeFragment = "resume-fragment";
    public const string Wait = "wait";
    public const string Recover = "recover";
    public const string Abort = "abort";
}

public sealed record ComputerOperatorRecoveryCoordinatorInput(
    ComputerOperatorLoopAssessment? LoopAssessment = null,
    ComputerOperatorRecoveryPlan? RecoveryPlan = null,
    ComputerOperatorRuntimeStateAssessment? RuntimeState = null,
    int RepeatedFailures = 0,
    bool CurrentStrategySuperseded = false,
    bool FastPathRejected = false,
    bool CompatibleFragmentAvailable = false,
    bool ExactFragmentAvailable = false);

public sealed record ComputerOperatorRecoveryCoordinatorDecision(
    string Decision,
    bool AllowSameStrategyRetry,
    bool RequiresFreshObservation,
    bool PreferKnownFragment,
    string Reason);

public interface IComputerOperatorRecoveryCoordinator
{
    ComputerOperatorRecoveryCoordinatorDecision Decide(
        ComputerOperatorRecoveryCoordinatorInput input);
}

/// <summary>
/// Coordinator duy nhất để hợp nhất tín hiệu loop/recovery/runtime/memory.
/// Không tự execute action; chỉ chọn hướng xử lý ưu tiên.
/// </summary>
public sealed class ComputerOperatorRecoveryCoordinator
    : IComputerOperatorRecoveryCoordinator
{
    public ComputerOperatorRecoveryCoordinatorDecision Decide(
        ComputerOperatorRecoveryCoordinatorInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (input.RuntimeState is
            {
                RecommendedDecision: ComputerOperatorRuntimeDecisions.Abort
            })
        {
            return Decision(
                ComputerOperatorRecoveryCoordinatorDecisions.Abort,
                allowSameStrategyRetry: false,
                requiresFreshObservation: false,
                preferKnownFragment: false,
                "Runtime State Intelligence yêu cầu abort.");
        }

        if (input.RuntimeState is
            {
                RecommendedDecision: ComputerOperatorRuntimeDecisions.Wait
            } runtime &&
            runtime.State is
                ComputerOperatorRuntimeStates.Progressing or
                ComputerOperatorRuntimeStates.WaitingExpected or
                ComputerOperatorRuntimeStates.WaitingExternal or
                ComputerOperatorRuntimeStates.Starting)
        {
            return Decision(
                ComputerOperatorRecoveryCoordinatorDecisions.Wait,
                allowSameStrategyRetry: false,
                requiresFreshObservation: true,
                preferKnownFragment: false,
                $"Runtime vẫn đang tiến triển/chờ hợp lệ ({runtime.State}); không recovery sớm.");
        }

        if (input.CurrentStrategySuperseded)
        {
            return input.ExactFragmentAvailable ||
                   input.CompatibleFragmentAvailable
                ? Decision(
                    ComputerOperatorRecoveryCoordinatorDecisions.ResumeFragment,
                    allowSameStrategyRetry: false,
                    requiresFreshObservation: true,
                    preferKnownFragment: true,
                    "Strategy hiện tại đã superseded; ưu tiên fragment đã biết thay vì retry strategy cũ.")
                : Decision(
                    ComputerOperatorRecoveryCoordinatorDecisions.ChangeStrategy,
                    allowSameStrategyRetry: false,
                    requiresFreshObservation: true,
                    preferKnownFragment: false,
                    "Strategy hiện tại đã superseded; bắt buộc đổi strategy.");
        }

        if (input.LoopAssessment is
            {
                Detected: true,
                RequiresStrategyChange: true
            } loop)
        {
            return input.ExactFragmentAvailable ||
                   input.CompatibleFragmentAvailable
                ? Decision(
                    ComputerOperatorRecoveryCoordinatorDecisions.ResumeFragment,
                    allowSameStrategyRetry: false,
                    requiresFreshObservation: true,
                    preferKnownFragment: true,
                    $"Loop Guard phát hiện {loop.Kind}; ưu tiên thoát vòng bằng fragment đã verify.")
                : Decision(
                    ComputerOperatorRecoveryCoordinatorDecisions.ChangeStrategy,
                    allowSameStrategyRetry: false,
                    requiresFreshObservation: true,
                    preferKnownFragment: false,
                    $"Loop Guard phát hiện {loop.Kind}; cấm lặp strategy hiện tại.");
        }

        if (input.RepeatedFailures >= 3)
        {
            return input.CompatibleFragmentAvailable
                ? Decision(
                    ComputerOperatorRecoveryCoordinatorDecisions.ResumeFragment,
                    allowSameStrategyRetry: false,
                    requiresFreshObservation: true,
                    preferKnownFragment: true,
                    "Failure lặp >=3; strategy hiện tại bị loại, ưu tiên fragment tương thích đã biết.")
                : Decision(
                    ComputerOperatorRecoveryCoordinatorDecisions.ChangeStrategy,
                    allowSameStrategyRetry: false,
                    requiresFreshObservation: true,
                    preferKnownFragment: false,
                    "Failure lặp >=3; không retry cùng strategy.");
        }

        if (input.RecoveryPlan is not null)
        {
            var plan =
                input.RecoveryPlan;

            return plan.PrimaryAction switch
            {
                ComputerOperatorRecoveryAction.WaitForStableUi =>
                    Decision(
                        ComputerOperatorRecoveryCoordinatorDecisions.Wait,
                        plan.AllowSameStrategyRetry,
                        requiresFreshObservation: true,
                        preferKnownFragment: false,
                        plan.Reason),

                ComputerOperatorRecoveryAction.Reobserve =>
                    Decision(
                        ComputerOperatorRecoveryCoordinatorDecisions.Reobserve,
                        plan.AllowSameStrategyRetry,
                        requiresFreshObservation: true,
                        preferKnownFragment: false,
                        plan.Reason),

                ComputerOperatorRecoveryAction.Block =>
                    Decision(
                        ComputerOperatorRecoveryCoordinatorDecisions.Abort,
                        allowSameStrategyRetry: false,
                        requiresFreshObservation: false,
                        preferKnownFragment: false,
                        plan.Reason),

                _ =>
                    input.ExactFragmentAvailable &&
                    !plan.AllowSameStrategyRetry
                        ? Decision(
                            ComputerOperatorRecoveryCoordinatorDecisions.ResumeFragment,
                            allowSameStrategyRetry: false,
                            requiresFreshObservation: true,
                            preferKnownFragment: true,
                            "Recovery cần đổi hướng và có exact fragment đã verify; ưu tiên fragment sau khi re-observe.")
                        : Decision(
                            ComputerOperatorRecoveryCoordinatorDecisions.Recover,
                            plan.AllowSameStrategyRetry,
                            requiresFreshObservation: true,
                            preferKnownFragment: false,
                            plan.Reason)
            };
        }

        if (input.FastPathRejected)
        {
            return Decision(
                ComputerOperatorRecoveryCoordinatorDecisions.Reobserve,
                allowSameStrategyRetry: false,
                requiresFreshObservation: true,
                preferKnownFragment: input.CompatibleFragmentAvailable,
                "Fast Path bị từ chối; quay lại observation/planner hiện tại thay vì ép memory.");
        }

        return Decision(
            ComputerOperatorRecoveryCoordinatorDecisions.Reobserve,
            allowSameStrategyRetry: false,
            requiresFreshObservation: true,
            preferKnownFragment: input.CompatibleFragmentAvailable,
            "Chưa có tín hiệu đủ mạnh cho retry; mặc định re-observe an toàn.");
    }

    private static ComputerOperatorRecoveryCoordinatorDecision Decision(
        string decision,
        bool allowSameStrategyRetry,
        bool requiresFreshObservation,
        bool preferKnownFragment,
        string reason) =>
        new(
            decision,
            allowSameStrategyRetry,
            requiresFreshObservation,
            preferKnownFragment,
            reason);
}
