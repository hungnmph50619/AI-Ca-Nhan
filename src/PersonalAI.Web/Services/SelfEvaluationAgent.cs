using PersonalAI.Web.Evaluation.Benchmarks;
using PersonalAI.Web.Evaluation.Comparison;
using PersonalAI.Web.Models;
using PersonalAI.Web.SelfImprovement;

namespace PersonalAI.Web.Services;

public interface ISelfEvaluationAgent
{
    SelfEvaluationReport Evaluate(SelfEvaluationRequest request);
}

public sealed class SelfEvaluationAgent(
    IAuditStore auditStore,
    IWorkspaceContextAccessor workspace,
    IAuditRecorder audit) : ISelfEvaluationAgent
{
    public const int DefaultAuditLimit = 100;
    public const int MaximumAuditLimit = 200;
    public const int MaximumBenchmarkReports = 5;
    public const int MaximumComparisonReports = 5;
    public const int MaximumCorrections = 50;

    private static readonly HashSet<string> FailureResults = new(
        [
            AuditResults.Failed,
            AuditResults.Blocked,
            AuditResults.Interrupted
        ],
        StringComparer.OrdinalIgnoreCase);

    public SelfEvaluationReport Evaluate(SelfEvaluationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var auditLimit = request.AuditLimit ?? DefaultAuditLimit;
        if (auditLimit is < 1 or > MaximumAuditLimit)
            throw new SelfEvaluationValidationException(
                $"AuditLimit phải từ 1 đến {MaximumAuditLimit}.");

        var benchmarks = request.Benchmarks ?? Array.Empty<AgentBenchmarkReport>();
        var comparisons = request.Comparisons ?? Array.Empty<ModelComparisonReport>();
        var corrections = request.Corrections ?? Array.Empty<UserCorrectionSignal>();

        if (benchmarks.Count > MaximumBenchmarkReports)
            throw new SelfEvaluationValidationException(
                $"Chỉ đọc tối đa {MaximumBenchmarkReports} benchmark report mỗi lần.");
        if (comparisons.Count > MaximumComparisonReports)
            throw new SelfEvaluationValidationException(
                $"Chỉ đọc tối đa {MaximumComparisonReports} comparison report mỗi lần.");
        if (corrections.Count > MaximumCorrections)
            throw new SelfEvaluationValidationException(
                $"Chỉ đọc tối đa {MaximumCorrections} user corrections mỗi lần.");

        ValidateWorkspace(benchmarks.Select(item => item.WorkspaceId), "benchmark");
        ValidateWorkspace(comparisons.Select(item => item.WorkspaceId), "comparison");

        var normalizedCorrections = corrections
            .Select(NormalizeCorrection)
            .ToArray();

        var auditResponse = auditStore.GetRecent(
            workspace.CurrentWorkspaceId,
            auditLimit);

        var weaknesses = new List<SelfEvaluationWeakness>();
        AddAuditWeaknesses(auditResponse.Items, weaknesses);
        AddBenchmarkWeaknesses(benchmarks, weaknesses);
        AddComparisonSignals(comparisons, weaknesses);
        AddCorrectionWeaknesses(normalizedCorrections, weaknesses);

        var ordered = weaknesses
            .OrderByDescending(item => SeverityRank(item.Severity))
            .ThenByDescending(item => item.EvidenceCount)
            .ThenBy(item => item.Area, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Signal, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        audit.Record(
            AuditAgents.System,
            "self-improvement.evaluate",
            $"self-evaluation:{workspace.CurrentWorkspaceId}",
            "read-only-analysis",
            AuditResults.Succeeded);

        return new SelfEvaluationReport(
            PersonalAiRelease.Version,
            workspace.CurrentWorkspaceId,
            DateTimeOffset.UtcNow,
            auditResponse.Items.Count,
            benchmarks.Count,
            comparisons.Count,
            normalizedCorrections.Length,
            ordered,
            ReadOnly: true,
            AutomaticChangesEnabled: false);
    }

    private void ValidateWorkspace(IEnumerable<string> workspaceIds, string source)
    {
        foreach (var workspaceId in workspaceIds)
        {
            if (!string.Equals(
                workspaceId,
                workspace.CurrentWorkspaceId,
                StringComparison.OrdinalIgnoreCase))
            {
                throw new SelfEvaluationValidationException(
                    $"{source} report không thuộc workspace hiện tại.");
            }
        }
    }

    private static UserCorrectionSignal NormalizeCorrection(UserCorrectionSignal correction)
    {
        if (correction is null)
            throw new SelfEvaluationValidationException(
                "User correction không được null.");

        var area = (correction.Area ?? string.Empty).Trim();
        var description = (correction.Description ?? string.Empty).Trim();
        if (area.Length is < 2 or > 80)
            throw new SelfEvaluationValidationException(
                "Correction area phải có từ 2 đến 80 ký tự.");
        if (description.Length is < 2 or > 500)
            throw new SelfEvaluationValidationException(
                "Correction description phải có từ 2 đến 500 ký tự.");
        return new UserCorrectionSignal(area, description);
    }

    private static void AddAuditWeaknesses(
        IReadOnlyList<AuditItem> items,
        ICollection<SelfEvaluationWeakness> weaknesses)
    {
        var failures = items
            .Where(item => FailureResults.Contains(item.Result))
            .GroupBy(item => item.Action, StringComparer.OrdinalIgnoreCase);

        foreach (var group in failures)
        {
            var count = group.Count();
            weaknesses.Add(new SelfEvaluationWeakness(
                Area: "audit",
                Signal: $"repeated-{group.Key}",
                Severity: count >= 5 ? "high" : count >= 2 ? "medium" : "low",
                Evidence: $"{count} audit event(s) có result failed/blocked/interrupted cho action '{group.Key}'.",
                EvidenceCount: count));
        }
    }

    private static void AddBenchmarkWeaknesses(
        IReadOnlyList<AgentBenchmarkReport> reports,
        ICollection<SelfEvaluationWeakness> weaknesses)
    {
        foreach (var report in reports)
        {
            if (report.ExecutedCases < 1 ||
                !double.IsFinite(report.PassRate) ||
                !double.IsFinite(report.AverageLatencyMilliseconds))
            {
                throw new SelfEvaluationValidationException(
                    "Benchmark report có metric không hợp lệ.");
            }

            if (report.PassRate < 1d)
            {
                var failed = report.Cases.Where(item => !item.Passed).ToArray();
                weaknesses.Add(new SelfEvaluationWeakness(
                    Area: "agent-benchmark",
                    Signal: "benchmark-failures",
                    Severity: report.PassRate < 0.5 ? "high" : "medium",
                    Evidence: $"{failed.Length}/{report.ExecutedCases} case benchmark không đạt expected checks.",
                    EvidenceCount: failed.Length));
            }

            if (report.AverageLatencyMilliseconds >= 10_000)
            {
                weaknesses.Add(new SelfEvaluationWeakness(
                    Area: "latency",
                    Signal: "high-agent-latency",
                    Severity: report.AverageLatencyMilliseconds >= 30_000 ? "high" : "medium",
                    Evidence: $"Average benchmark latency là {report.AverageLatencyMilliseconds:F0} ms.",
                    EvidenceCount: report.ExecutedCases));
            }

            var failureKinds = report.Cases
                .SelectMany(item => item.Failures)
                .Select(value => value.Split(':', 2)[0])
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .GroupBy(value => value, StringComparer.OrdinalIgnoreCase);

            foreach (var group in failureKinds)
            {
                var count = group.Count();
                weaknesses.Add(new SelfEvaluationWeakness(
                    Area: "agent-benchmark",
                    Signal: $"failure-{group.Key}",
                    Severity: count >= 3 ? "medium" : "low",
                    Evidence: $"{count} benchmark failure(s) thuộc nhóm '{group.Key}'.",
                    EvidenceCount: count));
            }
        }
    }

    private static void AddComparisonSignals(
        IReadOnlyList<ModelComparisonReport> reports,
        ICollection<SelfEvaluationWeakness> weaknesses)
    {
        foreach (var report in reports)
        {
            if (report.Candidates.Count < 2 || report.Cases < 1)
                throw new SelfEvaluationValidationException(
                    "Model comparison report không hợp lệ.");

            var baseline = report.Candidates.FirstOrDefault(candidate =>
                string.Equals(
                    candidate.Label,
                    report.BaselineLabel,
                    StringComparison.OrdinalIgnoreCase));

            if (baseline is null)
                throw new SelfEvaluationValidationException(
                    "Model comparison report thiếu baseline.");

            foreach (var candidate in report.Candidates)
            {
                if (!double.IsFinite(candidate.PassRate) ||
                    !double.IsFinite(candidate.AverageLatencyMilliseconds))
                    throw new SelfEvaluationValidationException(
                        "Model comparison report có metric không hữu hạn.");

                if (candidate.PassRate < baseline.PassRate)
                {
                    weaknesses.Add(new SelfEvaluationWeakness(
                        Area: "model-comparison",
                        Signal: "lower-pass-rate-than-baseline",
                        Severity: baseline.PassRate - candidate.PassRate >= 0.2 ? "medium" : "low",
                        Evidence: $"Candidate '{candidate.Label}' có pass rate thấp hơn baseline '{baseline.Label}' {(baseline.PassRate - candidate.PassRate):P1}.",
                        EvidenceCount: candidate.FailedCases));
                }

                if (candidate.AverageLatencyMilliseconds >
                    baseline.AverageLatencyMilliseconds * 1.5 &&
                    candidate.AverageLatencyMilliseconds -
                    baseline.AverageLatencyMilliseconds >= 1000)
                {
                    weaknesses.Add(new SelfEvaluationWeakness(
                        Area: "model-comparison",
                        Signal: "higher-latency-than-baseline",
                        Severity: "low",
                        Evidence: $"Candidate '{candidate.Label}' chậm hơn baseline '{baseline.Label}' {candidate.AverageLatencyMilliseconds - baseline.AverageLatencyMilliseconds:F0} ms trung bình.",
                        EvidenceCount: candidate.Cases));
                }
            }
        }
    }

    private static void AddCorrectionWeaknesses(
        IReadOnlyList<UserCorrectionSignal> corrections,
        ICollection<SelfEvaluationWeakness> weaknesses)
    {
        foreach (var group in corrections.GroupBy(
            correction => correction.Area,
            StringComparer.OrdinalIgnoreCase))
        {
            var items = group.ToArray();
            weaknesses.Add(new SelfEvaluationWeakness(
                Area: group.Key,
                Signal: "user-corrections",
                Severity: items.Length >= 3 ? "medium" : "low",
                Evidence: $"{items.Length} correction(s) do người dùng cung cấp cho khu vực này. Ví dụ: {items[0].Description}",
                EvidenceCount: items.Length));
        }
    }

    private static int SeverityRank(string severity) => severity switch
    {
        "high" => 3,
        "medium" => 2,
        _ => 1
    };
}
