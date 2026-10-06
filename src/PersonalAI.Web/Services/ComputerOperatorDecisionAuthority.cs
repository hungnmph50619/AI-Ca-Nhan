namespace PersonalAI.Web.Services;

public static class ComputerOperatorDecisionAuthorityDirectives
{
    public const string Execute = "execute";
    public const string Replan = "replan";
}

public sealed record ComputerOperatorDecisionAuthorityInput(
    string PlannerAction,
    string StrategySignature,
    bool RecentlyConsumedReplay,
    ComputerOperatorLoopAssessment LoopAssessment,
    ComputerOperatorRecoveryCoordinatorDecision? RecoveryRecommendation);

public sealed record ComputerOperatorDecisionAuthorityDecision(
    string Directive,
    bool AllowExecution,
    bool RequiresFreshObservation,
    bool PreferKnownFragment,
    string Reason);

public interface IComputerOperatorDecisionAuthority
{
    ComputerOperatorDecisionAuthorityDecision Evaluate(
        ComputerOperatorDecisionAuthorityInput input);
}

/// <summary>
/// Quyền quyết định duy nhất ở pha Plan.
/// Planner chọn action; loop/recovery/memory chỉ cung cấp tín hiệu.
/// Authority này mới được phép quyết định action tiếp tục EXECUTE hay phải REPLAN.
/// </summary>
public sealed class ComputerOperatorDecisionAuthority
    : IComputerOperatorDecisionAuthority
{
    public ComputerOperatorDecisionAuthorityDecision Evaluate(
        ComputerOperatorDecisionAuthorityInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (input.RecentlyConsumedReplay)
        {
            return new(
                ComputerOperatorDecisionAuthorityDirectives.Replan,
                AllowExecution: false,
                RequiresFreshObservation: true,
                PreferKnownFragment: false,
                "Action/target vừa tạo transition mạnh đã được consume; phải quan sát scene mới và chọn bước khác.");
        }

        if (input.LoopAssessment is
            {
                Detected: true,
                RequiresStrategyChange: true
            })
        {
            var recovery =
                input.RecoveryRecommendation;

            return new(
                ComputerOperatorDecisionAuthorityDirectives.Replan,
                AllowExecution: false,
                RequiresFreshObservation:
                    recovery?.RequiresFreshObservation ?? true,
                PreferKnownFragment:
                    recovery?.PreferKnownFragment ?? false,
                recovery?.Reason ??
                    $"Loop Guard phát hiện {input.LoopAssessment.Kind}; planner phải đổi chiến lược.");
        }

        return new(
            ComputerOperatorDecisionAuthorityDirectives.Execute,
            AllowExecution: true,
            RequiresFreshObservation: false,
            PreferKnownFragment: false,
            "Planner action được phép đi tiếp sang Grounding/Safety/Executor.");
    }
}
