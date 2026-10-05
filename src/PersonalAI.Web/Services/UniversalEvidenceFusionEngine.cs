namespace PersonalAI.Web.Services;

public static class UniversalEvidenceReliability
{
    public const string Deterministic = "deterministic";
    public const string Structured = "structured";
    public const string StrongLocal = "strong-local";
    public const string WeakLocal = "weak-local";
    public const string Semantic = "semantic";
}

public static class UniversalEvidenceFusionStatuses
{
    public const string Verified = "verified";
    public const string NotAchieved = "not-achieved";
    public const string NeedsVerification = "needs-verification";
}

public sealed record UniversalEvidenceSignal(
    string Source,
    string SourceGroup,
    bool Passed,
    double Confidence,
    string Reliability,
    string Summary);

public sealed record UniversalEvidenceFusionResult(
    string Status,
    bool GoalAchieved,
    bool IndependentlyVerified,
    double Confidence,
    string Source,
    string Reason,
    IReadOnlyList<UniversalEvidenceSignal> AcceptedSignals);

public interface IUniversalEvidenceFusionEngine
{
    UniversalEvidenceFusionResult Fuse(
        IReadOnlyList<UniversalEvidenceSignal> signals);
}

public sealed class UniversalEvidenceFusionEngine
    : IUniversalEvidenceFusionEngine
{
    private const double DeterministicThreshold = 0.90;
    private const double StructuredThreshold = 0.85;
    private const double StrongLocalThreshold = 0.85;

    public UniversalEvidenceFusionResult Fuse(
        IReadOnlyList<UniversalEvidenceSignal> signals)
    {
        ArgumentNullException.ThrowIfNull(signals);

        var normalized =
            signals
                .Where(item =>
                    item is not null &&
                    !string.IsNullOrWhiteSpace(item.Source) &&
                    !string.IsNullOrWhiteSpace(item.SourceGroup))
                .Select(item => item with
                {
                    Confidence = Math.Clamp(
                        item.Confidence,
                        0,
                        1),
                    Source = item.Source.Trim(),
                    SourceGroup = item.SourceGroup.Trim(),
                    Reliability = NormalizeReliability(
                        item.Reliability),
                    Summary = (item.Summary ?? string.Empty).Trim()
                })
                .ToArray();

        if (normalized.Length == 0)
        {
            return NeedsVerification(
                "Chưa có bằng chứng hợp lệ để hợp nhất.",
                normalized);
        }

        // Không double-count các tín hiệu tương quan từ cùng một nguồn.
        // Trong mỗi source group chỉ lấy tín hiệu mạnh nhất.
        var independent =
            normalized
                .GroupBy(
                    item => item.SourceGroup,
                    StringComparer.OrdinalIgnoreCase)
                .Select(group =>
                    group
                        .OrderByDescending(ReliabilityRank)
                        .ThenByDescending(item => item.Confidence)
                        .First())
                .ToArray();

        var deterministic =
            independent
                .Where(item =>
                    item.Reliability ==
                        UniversalEvidenceReliability.Deterministic &&
                    item.Confidence >=
                        DeterministicThreshold)
                .ToArray();

        if (HasConflict(deterministic))
        {
            return NeedsVerification(
                "Các bằng chứng xác định đang xung đột; không được phép tự kết luận.",
                independent);
        }

        if (deterministic.Length > 0)
        {
            var chosen =
                deterministic
                    .OrderByDescending(item =>
                        item.Confidence)
                    .First();

            return Final(
                chosen.Passed,
                chosen.Confidence,
                $"Bằng chứng xác định đủ mạnh từ {chosen.Source}: {chosen.Summary}",
                independent);
        }

        var strong =
            independent
                .Where(item =>
                    IsStrong(item))
                .ToArray();

        if (HasConflict(strong))
        {
            return NeedsVerification(
                "Các nguồn bằng chứng mạnh độc lập đang xung đột; cần semantic verification hoặc quan sát thêm.",
                independent);
        }

        var strongGroups =
            strong
                .Select(item => item.SourceGroup)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();

        // Với structured/strong-local không mang tính deterministic,
        // yêu cầu ít nhất 2 nguồn độc lập cùng kết luận.
        if (strongGroups >= 2)
        {
            var passed =
                strong.All(item => item.Passed);

            var confidence =
                strong
                    .OrderBy(item => item.Confidence)
                    .First()
                    .Confidence;

            return Final(
                passed,
                confidence,
                $"Có {strongGroups} nguồn bằng chứng mạnh độc lập cùng kết luận.",
                independent);
        }

        // Semantic AI không được tự complete nếu chỉ có một mình nó.
        // Nó có thể bổ sung cho một structured/strong-local source.
        var semantic =
            independent
                .Where(item =>
                    item.Reliability ==
                        UniversalEvidenceReliability.Semantic &&
                    item.Confidence >= 0.85)
                .ToArray();

        if (strong.Length == 1 &&
            semantic.Length > 0)
        {
            var semanticAgreement =
                semantic
                    .Where(item =>
                        item.Passed ==
                        strong[0].Passed)
                    .OrderByDescending(item =>
                        item.Confidence)
                    .FirstOrDefault();

            var semanticConflict =
                semantic.Any(item =>
                    item.Passed !=
                    strong[0].Passed);

            if (semanticConflict)
            {
                return NeedsVerification(
                    "Semantic evidence xung đột với structured/local evidence; không tự kết luận.",
                    independent);
            }

            if (semanticAgreement is not null)
            {
                return Final(
                    strong[0].Passed,
                    Math.Min(
                        strong[0].Confidence,
                        semanticAgreement.Confidence),
                    $"Structured/local evidence và semantic evidence cùng kết luận: {strong[0].Source} + {semanticAgreement.Source}.",
                    independent);
            }
        }

        return NeedsVerification(
            "Bằng chứng hiện tại chưa đủ độc lập hoặc chưa đủ mạnh để xác nhận outcome.",
            independent);
    }

    private static bool IsStrong(
        UniversalEvidenceSignal item) =>
        item.Reliability switch
        {
            UniversalEvidenceReliability.Structured =>
                item.Confidence >=
                    StructuredThreshold,

            UniversalEvidenceReliability.StrongLocal =>
                item.Confidence >=
                    StrongLocalThreshold,

            _ => false
        };

    private static bool HasConflict(
        IReadOnlyList<UniversalEvidenceSignal> items) =>
        items.Any(item => item.Passed) &&
        items.Any(item => !item.Passed);

    private static int ReliabilityRank(
        UniversalEvidenceSignal item) =>
        item.Reliability switch
        {
            UniversalEvidenceReliability.Deterministic => 5,
            UniversalEvidenceReliability.Structured => 4,
            UniversalEvidenceReliability.StrongLocal => 3,
            UniversalEvidenceReliability.Semantic => 2,
            UniversalEvidenceReliability.WeakLocal => 1,
            _ => 0
        };

    private static string NormalizeReliability(
        string? value)
    {
        var normalized =
            (value ?? string.Empty)
                .Trim()
                .ToLowerInvariant();

        return normalized switch
        {
            UniversalEvidenceReliability.Deterministic =>
                UniversalEvidenceReliability.Deterministic,

            UniversalEvidenceReliability.Structured =>
                UniversalEvidenceReliability.Structured,

            UniversalEvidenceReliability.StrongLocal =>
                UniversalEvidenceReliability.StrongLocal,

            UniversalEvidenceReliability.Semantic =>
                UniversalEvidenceReliability.Semantic,

            _ =>
                UniversalEvidenceReliability.WeakLocal
        };
    }

    private static UniversalEvidenceFusionResult Final(
        bool passed,
        double confidence,
        string reason,
        IReadOnlyList<UniversalEvidenceSignal> accepted) =>
        new(
            passed
                ? UniversalEvidenceFusionStatuses.Verified
                : UniversalEvidenceFusionStatuses.NotAchieved,
            GoalAchieved: passed,
            IndependentlyVerified: true,
            Math.Clamp(confidence, 0, 1),
            Source: "evidence-fusion",
            reason,
            accepted);

    private static UniversalEvidenceFusionResult NeedsVerification(
        string reason,
        IReadOnlyList<UniversalEvidenceSignal> accepted) =>
        new(
            UniversalEvidenceFusionStatuses.NeedsVerification,
            GoalAchieved: false,
            IndependentlyVerified: false,
            Confidence:
                accepted.Count == 0
                    ? 0
                    : accepted.Max(item =>
                        item.Confidence),
            Source: "evidence-fusion",
            reason,
            accepted);
}
