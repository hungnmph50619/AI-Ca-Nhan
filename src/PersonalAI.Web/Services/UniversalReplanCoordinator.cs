namespace PersonalAI.Web.Services;

public static class UniversalContinuationActions
{
    public const string Complete = "complete";
    public const string RequestVerificationContext = "request-verification-context";
    public const string ReplanArguments = "replan-arguments";
    public const string ReplanSameChannel = "replan-same-channel";
    public const string Reroute = "reroute";
    public const string ProposeAgentFallback = "propose-agent-fallback";
    public const string Stop = "stop";
}

public sealed record UniversalReplanRequest(
    string Goal,
    UniversalPostExecutionVerificationResult Verification,
    int AttemptIndex = 1,
    int RepeatedOutcomeCount = 0,
    int MaximumAttempts = 3);

public sealed record UniversalReplanDecision(
    string Action,
    string? Channel,
    int CurrentAttempt,
    int NextAttempt,
    bool CanAutoContinuePlanning,
    bool RequiresExecutionConfirmation,
    string Reason);

public interface IUniversalReplanCoordinator
{
    UniversalReplanDecision Decide(
        UniversalReplanRequest request);
}

public sealed class UniversalReplanCoordinator
    : IUniversalReplanCoordinator
{
    private const int MaximumRepeatedOutcomeBeforeEscalation = 2;

    public UniversalReplanDecision Decide(
        UniversalReplanRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Verification);

        var goal = (request.Goal ?? string.Empty).Trim();

        if (goal.Length < 2)
        {
            throw new AgentValidationException(
                "Replan Coordinator cần goal rõ ràng.");
        }

        var maximumAttempts = Math.Clamp(
            request.MaximumAttempts,
            1,
            10);

        var currentAttempt = Math.Clamp(
            request.AttemptIndex,
            1,
            maximumAttempts);

        var repeatedOutcomeCount = Math.Max(
            0,
            request.RepeatedOutcomeCount);

        var fallbackDecision =
            request.Verification.Decision;

        var channel =
            request.Verification.EvidenceRoute.Channel;

        if (fallbackDecision.Action.Equals(
                UniversalFallbackActions.Complete,
                StringComparison.OrdinalIgnoreCase))
        {
            return new(
                UniversalContinuationActions.Complete,
                channel,
                currentAttempt,
                currentAttempt,
                CanAutoContinuePlanning: false,
                RequiresExecutionConfirmation: false,
                "Outcome đã được xác minh; kết thúc task.");
        }

        if (fallbackDecision.Action.Equals(
                UniversalFallbackActions.Stop,
                StringComparison.OrdinalIgnoreCase))
        {
            return new(
                UniversalContinuationActions.Stop,
                channel,
                currentAttempt,
                currentAttempt,
                CanAutoContinuePlanning: false,
                RequiresExecutionConfirmation: false,
                $"Fallback policy yêu cầu dừng: {fallbackDecision.Reason}");
        }

        if (fallbackDecision.Action.Equals(
                UniversalFallbackActions.VerifyOutcome,
                StringComparison.OrdinalIgnoreCase))
        {
            return new(
                UniversalContinuationActions.RequestVerificationContext,
                channel,
                currentAttempt,
                currentAttempt,
                CanAutoContinuePlanning: true,
                RequiresExecutionConfirmation: false,
                "Execution đã thành công nhưng chưa đủ evidence; thu thêm verification context trước khi replan execution.");
        }

        if (fallbackDecision.Action.Equals(
                UniversalFallbackActions.ReplanArguments,
                StringComparison.OrdinalIgnoreCase))
        {
            if (currentAttempt >= maximumAttempts)
            {
                return BuildReroute(
                    channel,
                    currentAttempt,
                    "Đã hết attempt budget khi lập lại tool arguments.");
            }

            return new(
                UniversalContinuationActions.ReplanArguments,
                channel,
                currentAttempt,
                currentAttempt + 1,
                CanAutoContinuePlanning: true,
                RequiresExecutionConfirmation: false,
                "Arguments chưa hợp lệ; lập lại arguments cho tool trước khi cân nhắc route khác.");
        }

        if (fallbackDecision.Action.Equals(
                UniversalFallbackActions.Reroute,
                StringComparison.OrdinalIgnoreCase))
        {
            return BuildReroute(
                channel,
                currentAttempt,
                fallbackDecision.Reason);
        }

        if (fallbackDecision.Action.Equals(
                UniversalFallbackActions.AgentFallback,
                StringComparison.OrdinalIgnoreCase))
        {
            if (!fallbackDecision.AllowAgentFallback ||
                string.IsNullOrWhiteSpace(
                    fallbackDecision.FallbackChannel))
            {
                return new(
                    UniversalContinuationActions.Stop,
                    channel,
                    currentAttempt,
                    currentAttempt,
                    CanAutoContinuePlanning: false,
                    RequiresExecutionConfirmation: false,
                    "Fallback policy không cho phép execution agent fallback.");
            }

            return new(
                UniversalContinuationActions.ProposeAgentFallback,
                fallbackDecision.FallbackChannel,
                currentAttempt,
                currentAttempt,
                CanAutoContinuePlanning: false,
                RequiresExecutionConfirmation: true,
                "Có thể đề xuất execution agent fallback sau lỗi kỹ thuật, nhưng execution vẫn cần confirmation.");
        }

        if (fallbackDecision.Action.Equals(
                UniversalFallbackActions.ReplanGoal,
                StringComparison.OrdinalIgnoreCase))
        {
            if (repeatedOutcomeCount >=
                MaximumRepeatedOutcomeBeforeEscalation)
            {
                return BuildReroute(
                    channel,
                    currentAttempt,
                    "Cùng outcome chưa đạt đã lặp lại nhiều lần; cấm retry y hệt.");
            }

            if (currentAttempt >= maximumAttempts)
            {
                return BuildReroute(
                    channel,
                    currentAttempt,
                    "Đã hết attempt budget cho channel hiện tại.");
            }

            return new(
                UniversalContinuationActions.ReplanSameChannel,
                channel,
                currentAttempt,
                currentAttempt + 1,
                CanAutoContinuePlanning: true,
                RequiresExecutionConfirmation: false,
                "Goal chưa đạt; lập phương án mới trong cùng channel nhưng không được lặp nguyên execution trước.");
        }

        return new(
            UniversalContinuationActions.Stop,
            channel,
            currentAttempt,
            currentAttempt,
            CanAutoContinuePlanning: false,
            RequiresExecutionConfirmation: false,
            $"Không nhận diện được fallback action: {fallbackDecision.Action}.");
    }

    private static UniversalReplanDecision BuildReroute(
        string? channel,
        int currentAttempt,
        string reason) =>
        new(
            UniversalContinuationActions.Reroute,
            channel,
            currentAttempt,
            currentAttempt,
            CanAutoContinuePlanning: true,
            RequiresExecutionConfirmation: false,
            $"Yêu cầu Universal Router đánh giá lại route. {reason}");
}
