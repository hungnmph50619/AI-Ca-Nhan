namespace PersonalAI.Web.Services;

public static class ComputerOperatorFailureKinds
{
    public const string LowConfidence = "low-confidence";
    public const string MissingExpectedEffect = "missing-expected-effect";
    public const string ActionRejected = "action-rejected";
    public const string NotApplied = "not-applied";
    public const string VerificationFailed = "verification-failed";
}

public sealed record ComputerOperatorRecoveryAttempt(
    int Sequence,
    string ActionSignature,
    string Action,
    string FailureKind,
    string State,
    string Detail,
    string ExpectedEffect,
    double Confidence,
    DateTimeOffset AtUtc);

public sealed class ComputerOperatorRecoverySession
{
    private const int MaximumAttempts = 24;
    private readonly List<ComputerOperatorRecoveryAttempt> attempts = [];
    private int sequence;

    public IReadOnlyList<ComputerOperatorRecoveryAttempt> Attempts =>
        attempts.ToArray();

    public int RecordFailure(
        string actionSignature,
        string action,
        string failureKind,
        string state,
        string detail,
        string expectedEffect,
        double confidence)
    {
        var attempt = new ComputerOperatorRecoveryAttempt(
            ++sequence,
            NormalizeInline(actionSignature, 220),
            NormalizeInline(action, 60),
            NormalizeInline(failureKind, 60),
            NormalizeInline(state, 260),
            NormalizeInline(detail, 500),
            NormalizeInline(expectedEffect, 400),
            double.IsFinite(confidence)
                ? Math.Clamp(confidence, 0, 1)
                : 0,
            DateTimeOffset.UtcNow);

        attempts.Add(attempt);
        if (attempts.Count > MaximumAttempts)
            attempts.RemoveRange(
                0,
                attempts.Count - MaximumAttempts);

        return attempts.Count(item =>
            item.ActionSignature.Equals(
                attempt.ActionSignature,
                StringComparison.OrdinalIgnoreCase));
    }

    public bool ShouldAvoidRepeatedStrategy(
        string actionSignature,
        string currentState,
        out string reason)
    {
        var signature = NormalizeInline(
            actionSignature,
            220);
        var state = NormalizeInline(
            currentState,
            260);

        var sameStrategy = attempts
            .Where(item =>
                item.ActionSignature.Equals(
                    signature,
                    StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (sameStrategy.Length == 0)
        {
            reason = string.Empty;
            return false;
        }

        var sameStateCount = sameStrategy.Count(item =>
            StatesEquivalent(
                item.State,
                state));

        if (sameStateCount > 0)
        {
            reason =
                $"Chiến lược “{signature}” đã thất bại {sameStateCount} lần trong trạng thái tương đương. Cần chọn cách khác thay vì lặp lại.";
            return true;
        }

        if (sameStrategy.Length >= 3)
        {
            reason =
                $"Chiến lược “{signature}” đã thất bại {sameStrategy.Length} lần trong task này. Cần đổi chiến lược.";
            return true;
        }

        reason = string.Empty;
        return false;
    }

    public string BuildContext()
    {
        if (attempts.Count == 0)
            return "BỘ NHỚ PHỤC HỒI: chưa có chiến lược thất bại.";

        var recent = attempts.TakeLast(12);
        var lines = new List<string>
        {
            "BỘ NHỚ PHỤC HỒI — các cách đã thất bại; dùng để chọn phương án khác:"
        };

        foreach (var item in recent)
        {
            lines.Add(
                $"- #{item.Sequence} loại={item.FailureKind}; action={item.Action}; chiến-lược={item.ActionSignature}; confidence={item.Confidence:0.00}");
            lines.Add(
                $"  trạng-thái={Limit(item.State, 180)}");
            lines.Add(
                $"  lỗi={Limit(item.Detail, 260)}");

            if (!string.IsNullOrWhiteSpace(
                    item.ExpectedEffect))
            {
                lines.Add(
                    $"  mong-đợi={Limit(item.ExpectedEffect, 220)}");
            }
        }

        return string.Join(
            "\n",
            lines);
    }

    private static bool StatesEquivalent(
        string left,
        string right)
    {
        var a = NormalizeForComparison(left);
        var b = NormalizeForComparison(right);

        if (a.Length == 0 ||
            b.Length == 0)
            return false;

        if (a.Equals(
                b,
                StringComparison.OrdinalIgnoreCase))
            return true;

        if (a.Length >= 24 &&
            b.Length >= 24 &&
            (a.Contains(
                 b,
                 StringComparison.OrdinalIgnoreCase) ||
             b.Contains(
                 a,
                 StringComparison.OrdinalIgnoreCase)))
            return true;

        return false;
    }

    private static string NormalizeForComparison(
        string value) =>
        string.Join(
            " ",
            (value ?? string.Empty)
                .Trim()
                .ToLowerInvariant()
                .Split(
                    [' ', '\t', '\r', '\n', '.', ',', ';', ':'],
                    StringSplitOptions.RemoveEmptyEntries));

    private static string NormalizeInline(
        string value,
        int maximum)
    {
        var normalized = string.Join(
            " ",
            (value ?? string.Empty)
                .Split(
                    [' ', '\t', '\r', '\n'],
                    StringSplitOptions.RemoveEmptyEntries));

        return Limit(
            normalized,
            maximum);
    }

    private static string Limit(
        string value,
        int maximum) =>
        value.Length <= maximum
            ? value
            : value[..Math.Max(
                0,
                maximum - 1)] + "…";
}
