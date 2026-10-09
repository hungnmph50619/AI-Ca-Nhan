namespace PersonalAI.Web.Services;

public static class ComputerOperatorFastPathStatuses
{
    public const string Eligible = "eligible";
    public const string NotEnoughEvidence = "not-enough-evidence";
    public const string StrategyMismatch = "strategy-mismatch";
    public const string ActionNotEligible = "action-not-eligible";
    public const string NoMemory = "no-memory";
}

public sealed record ComputerOperatorFastPathAssessment(
    string Status,
    bool Eligible,
    string StrategyKey,
    string ActionKind,
    int SuccessCount,
    double Confidence,
    DateTimeOffset? LastVerifiedAt,
    string Reason);

public interface IComputerOperatorStrategyRanker
{
    ComputerOperatorFastPathAssessment AssessFastPath(
        string currentStateFingerprint,
        string proposedStrategyKey,
        string proposedActionKind,
        ComputerOperatorProcedureContextDescriptor? currentContext = null);
}

/// <summary>
/// Fast Path admission gate.
/// Không dựng action từ memory. Chỉ xác nhận một action deterministic/local đã được
/// planner hiện tại tạo ra và vẫn đi qua safety + execute + VERIFY như bình thường.
/// </summary>
public sealed class ComputerOperatorStrategyRanker(
    IComputerOperatorProcedureGraphStore graph,
    IComputerOperatorProcedureContextStore contexts,
    IComputerOperatorProcedureContextService compatibility,
    IComputerOperatorProcedureEdgeLifecycleService lifecycle)
    : IComputerOperatorStrategyRanker
{
    public const int MinimumFastPathSuccesses = 3;
    public const double MinimumFastPathConfidence = 0.95;
    public static readonly TimeSpan MaximumEvidenceAge =
        TimeSpan.FromDays(60);

    private static readonly HashSet<string> EligibleActions =
        new(
            StringComparer.OrdinalIgnoreCase)
        {
            "click-left",
            "double-click-left",
            "scroll",
            "focus-window",
            "press-key",
            "press-hotkey",
            "structured-focus",
            "structured-invoke",
            "structured-select",
            "structured-toggle",
            "structured-expand",
            "structured-collapse",
            "structured-legacy-default"
        };

    public ComputerOperatorFastPathAssessment AssessFastPath(
        string currentStateFingerprint,
        string proposedStrategyKey,
        string proposedActionKind,
        ComputerOperatorProcedureContextDescriptor? currentContext = null)
    {
        var strategy =
            Normalize(
                proposedStrategyKey);

        var action =
            Normalize(
                proposedActionKind);

        if (!EligibleActions.Contains(action))
        {
            return new(
                ComputerOperatorFastPathStatuses.ActionNotEligible,
                false,
                strategy,
                action,
                0,
                0,
                null,
                "Action hiện tại không nằm trong allowlist Fast Path; giữ planner/verification đầy đủ.");
        }

        if (currentContext is not null)
        {
            var rememberedContext =
                contexts.Get(
                    currentStateFingerprint);

            if (rememberedContext is not null)
            {
                var contextAssessment =
                    compatibility.Assess(
                        currentContext,
                        rememberedContext);

                if (!contextAssessment.ExactVersion)
                {
                    return new(
                        ComputerOperatorFastPathStatuses.NotEnoughEvidence,
                        false,
                        strategy,
                        action,
                        0,
                        0,
                        null,
                        $"Context version không khớp exact version đã học ({contextAssessment.Kind}); không bật Fast Path. Fragment cùng family chỉ dùng làm tham khảo.");
                }
            }
        }

        var outgoing =
            graph.GetOutgoing(
                currentStateFingerprint,
                limit: 50);

        if (outgoing.Count == 0)
        {
            return new(
                ComputerOperatorFastPathStatuses.NoMemory,
                false,
                strategy,
                action,
                0,
                0,
                null,
                "State hiện tại chưa có verified edge trong Procedure Graph.");
        }

        var match =
            outgoing.FirstOrDefault(edge =>
                edge.StrategyKey.Equals(
                    strategy,
                    StringComparison.OrdinalIgnoreCase) &&
                edge.ActionKind.Equals(
                    action,
                    StringComparison.OrdinalIgnoreCase));

        if (match is null)
        {
            return new(
                ComputerOperatorFastPathStatuses.StrategyMismatch,
                false,
                strategy,
                action,
                0,
                0,
                null,
                "Local proposal hiện tại không trùng strategy/action đã được verify ở state này.");
        }

        var lifecycleAssessment =
            lifecycle.Evaluate(
                match);

        if (lifecycleAssessment.Status.Equals(
                ComputerOperatorProcedureEdgeStatuses.Superseded,
                StringComparison.OrdinalIgnoreCase))
        {
            return new(
                ComputerOperatorFastPathStatuses.NotEnoughEvidence,
                false,
                strategy,
                action,
                match.SuccessCount,
                0,
                match.LastVerifiedAt,
                $"Strategy đã bị supersede bởi {lifecycleAssessment.SupersededByStrategyKey ?? "strategy mới"}; không dùng Fast Path.");
        }

        var age =
            DateTimeOffset.UtcNow -
            match.LastVerifiedAt;

        if (match.SuccessCount <
                MinimumFastPathSuccesses ||
            lifecycleAssessment.EffectiveConfidence <
                MinimumFastPathConfidence ||
            age >
                MaximumEvidenceAge)
        {
            return new(
                ComputerOperatorFastPathStatuses.NotEnoughEvidence,
                false,
                strategy,
                action,
                match.SuccessCount,
                lifecycleAssessment.EffectiveConfidence,
                match.LastVerifiedAt,
                $"Verified edge chưa đủ mạnh cho Fast Path: successes={match.SuccessCount}/{MinimumFastPathSuccesses}; effectiveConfidence={lifecycleAssessment.EffectiveConfidence:0.000}/{MinimumFastPathConfidence:0.000}; decay={lifecycleAssessment.DecayMultiplier:0.00}; ageDays={Math.Max(0, age.TotalDays):0.0}/{MaximumEvidenceAge.TotalDays:0}.");
        }

        return new(
            ComputerOperatorFastPathStatuses.Eligible,
            true,
            strategy,
            action,
            match.SuccessCount,
            match.AverageConfidence,
            match.LastVerifiedAt,
            "Local deterministic proposal khớp exact state + verified strategy đủ mạnh; cho phép bỏ Gemini nhưng vẫn bắt buộc safety gate và VERIFY sau action.");
    }

    private static string Normalize(
        string? value) =>
        (value ?? string.Empty)
            .Trim()
            .ToLowerInvariant();
}
