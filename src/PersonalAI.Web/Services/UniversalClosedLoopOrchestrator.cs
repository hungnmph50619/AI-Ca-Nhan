namespace PersonalAI.Web.Services;

public static class UniversalClosedLoopStatuses
{
    public const string Completed = "completed";
    public const string AwaitingConfirmation = "awaiting-confirmation";
    public const string NeedsVerification = "needs-verification";
    public const string ReplanRequired = "replan-required";
    public const string LoopDetected = "loop-detected";
    public const string MaxIterationsReached = "max-iterations-reached";
    public const string Failed = "failed";
}

public sealed record UniversalClosedLoopRequest(
    string Goal,
    int MaxIterations = 4);

public sealed record UniversalClosedLoopStepRequest(
    string Goal,
    int Iteration,
    string? PreviousSummary = null);

public sealed record UniversalClosedLoopStepResult(
    string ActionKey,
    string OutcomeKey,
    bool Executed,
    bool GoalAchieved,
    bool RequiresConfirmation,
    bool NeedsVerification,
    bool ReplanRequired,
    bool CanContinue,
    string Summary);

public sealed record UniversalClosedLoopIteration(
    int Iteration,
    string ActionKey,
    string OutcomeKey,
    bool Executed,
    bool GoalAchieved,
    string Summary);

public sealed record UniversalClosedLoopResult(
    string Status,
    string Goal,
    int Iterations,
    IReadOnlyList<UniversalClosedLoopIteration> History,
    string Reason);

public interface IUniversalClosedLoopStepRunner
{
    Task<UniversalClosedLoopStepResult> RunAsync(
        UniversalClosedLoopStepRequest request,
        CancellationToken cancellationToken = default);
}

public interface IUniversalClosedLoopOrchestrator
{
    Task<UniversalClosedLoopResult> RunAsync(
        UniversalClosedLoopRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class UniversalClosedLoopOrchestrator(
    IUniversalClosedLoopStepRunner stepRunner)
    : IUniversalClosedLoopOrchestrator
{
    private const int MinimumIterations = 1;
    private const int MaximumIterations = 8;
    private const int RepeatedFingerprintLimit = 3;

    public async Task<UniversalClosedLoopResult> RunAsync(
        UniversalClosedLoopRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var goal = (request.Goal ?? string.Empty).Trim();

        if (goal.Length < 2)
        {
            throw new AgentValidationException(
                "Closed-loop orchestrator cần goal rõ ràng.");
        }

        var maxIterations = Math.Clamp(
            request.MaxIterations,
            MinimumIterations,
            MaximumIterations);

        var history =
            new List<UniversalClosedLoopIteration>();

        var fingerprintCounts =
            new Dictionary<string, int>(
                StringComparer.Ordinal);

        string? previousSummary = null;

        for (var iteration = 1;
             iteration <= maxIterations;
             iteration++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var step =
                await stepRunner.RunAsync(
                    new UniversalClosedLoopStepRequest(
                        goal,
                        iteration,
                        previousSummary),
                    cancellationToken);

            ValidateStep(step);

            history.Add(
                new UniversalClosedLoopIteration(
                    iteration,
                    step.ActionKey,
                    step.OutcomeKey,
                    step.Executed,
                    step.GoalAchieved,
                    step.Summary));

            var fingerprint =
                BuildFingerprint(
                    step.ActionKey,
                    step.OutcomeKey);

            fingerprintCounts.TryGetValue(
                fingerprint,
                out var repeated);

            repeated++;
            fingerprintCounts[fingerprint] =
                repeated;

            if (repeated >=
                RepeatedFingerprintLimit)
            {
                return Finish(
                    UniversalClosedLoopStatuses.LoopDetected,
                    goal,
                    history,
                    $"Phát hiện cùng action/outcome lặp {repeated} lần; dừng để tránh vòng lặp.");
            }

            if (step.RequiresConfirmation)
            {
                return Finish(
                    UniversalClosedLoopStatuses.AwaitingConfirmation,
                    goal,
                    history,
                    "Bước tiếp theo cần xác nhận rõ ràng; closed-loop không tự vượt confirmation.");
            }

            if (step.NeedsVerification)
            {
                return Finish(
                    UniversalClosedLoopStatuses.NeedsVerification,
                    goal,
                    history,
                    "Execution đã có nhưng chưa đủ verification; không được tự coi là hoàn thành.");
            }

            if (step.GoalAchieved)
            {
                return Finish(
                    UniversalClosedLoopStatuses.Completed,
                    goal,
                    history,
                    "Goal đã được xác minh đạt.");
            }

            if (step.ReplanRequired)
            {
                if (!step.CanContinue)
                {
                    return Finish(
                        UniversalClosedLoopStatuses.ReplanRequired,
                        goal,
                        history,
                        "Verifier yêu cầu replan nhưng step runner chưa có phương án tiếp tục an toàn.");
                }

                previousSummary =
                    step.Summary;

                continue;
            }

            if (!step.CanContinue)
            {
                return Finish(
                    UniversalClosedLoopStatuses.Failed,
                    goal,
                    history,
                    "Bước hiện tại không đạt goal và không có continuation an toàn.");
            }

            previousSummary =
                step.Summary;
        }

        return Finish(
            UniversalClosedLoopStatuses.MaxIterationsReached,
            goal,
            history,
            $"Đã đạt giới hạn {maxIterations} vòng mà goal chưa được xác minh hoàn tất.");
    }

    private static void ValidateStep(
        UniversalClosedLoopStepResult step)
    {
        ArgumentNullException.ThrowIfNull(step);

        if (string.IsNullOrWhiteSpace(
                step.ActionKey) ||
            string.IsNullOrWhiteSpace(
                step.OutcomeKey))
        {
            throw new AgentValidationException(
                "Closed-loop step phải có actionKey và outcomeKey.");
        }

        if (step.GoalAchieved &&
            (step.NeedsVerification ||
             step.RequiresConfirmation ||
             step.ReplanRequired))
        {
            throw new AgentValidationException(
                "Closed-loop step không được vừa GoalAchieved vừa yêu cầu verify/confirm/replan.");
        }
    }

    private static UniversalClosedLoopResult Finish(
        string status,
        string goal,
        IReadOnlyList<UniversalClosedLoopIteration> history,
        string reason) =>
        new(
            status,
            goal,
            history.Count,
            history,
            reason);

    private static string BuildFingerprint(
        string actionKey,
        string outcomeKey) =>
        $"{actionKey.Trim().ToLowerInvariant()}|{outcomeKey.Trim().ToLowerInvariant()}";
}
