using PersonalAI.Web.Evaluation.Benchmarks;
using PersonalAI.Web.Evaluation.Comparison;
using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public interface IModelComparisonService
{
    ModelComparisonReport Compare(ModelComparisonRequest request);
}

public sealed class ModelComparisonService(
    IWorkspaceContextAccessor workspace,
    IAuditRecorder audit) : IModelComparisonService
{
    public const int MinimumCandidates = 2;
    public const int MaximumCandidates = 5;
    public const int MaximumCases = 100;

    public ModelComparisonReport Compare(ModelComparisonRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Candidates is null ||
            request.Candidates.Count < MinimumCandidates ||
            request.Candidates.Count > MaximumCandidates)
        {
            throw new ModelComparisonValidationException(
                $"Cần từ {MinimumCandidates} đến {MaximumCandidates} báo cáo benchmark để so sánh.");
        }

        var labels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in request.Candidates)
        {
            var label = NormalizeLabel(candidate.Label);
            if (!labels.Add(label))
                throw new ModelComparisonValidationException(
                    $"Nhãn model/candidate bị trùng: '{label}'.");

            ValidateReport(candidate.Report, label);
        }

        var baseline = request.Candidates[0];
        var baselineCases = IndexCases(baseline.Report, NormalizeLabel(baseline.Label));
        if (baselineCases.Count > MaximumCases)
            throw new ModelComparisonValidationException(
                $"Model Comparison hỗ trợ tối đa {MaximumCases} case.");

        foreach (var candidate in request.Candidates.Skip(1))
        {
            var current = IndexCases(candidate.Report, NormalizeLabel(candidate.Label));
            if (!baselineCases.Keys.ToHashSet(StringComparer.Ordinal)
                .SetEquals(current.Keys))
            {
                throw new ModelComparisonValidationException(
                    "Tất cả benchmark report phải chứa cùng tập case ID.");
            }

            foreach (var caseId in baselineCases.Keys)
            {
                var expected = baselineCases[caseId];
                var actual = current[caseId];
                if (!string.Equals(expected.Title, actual.Title, StringComparison.Ordinal) ||
                    !string.Equals(expected.AgentId, actual.AgentId, StringComparison.Ordinal))
                {
                    throw new ModelComparisonValidationException(
                        $"Case '{caseId}' không cùng title/agent giữa các report.");
                }
            }
        }

        var baselineLabel = NormalizeLabel(baseline.Label);
        var baselinePassRate = baseline.Report.PassRate;
        var baselineLatency = baseline.Report.AverageLatencyMilliseconds;

        var summaries = request.Candidates
            .Select(candidate =>
            {
                var label = NormalizeLabel(candidate.Label);
                var provider = DistinctOrNull(
                    candidate.Report.Cases.Select(item => item.Provider));
                var model = DistinctOrNull(
                    candidate.Report.Cases.Select(item => item.Model));

                return new ModelComparisonCandidateSummary(
                    label,
                    provider,
                    model,
                    candidate.Report.ExecutedCases,
                    candidate.Report.PassedCases,
                    candidate.Report.FailedCases,
                    candidate.Report.PassRate,
                    candidate.Report.AverageLatencyMilliseconds,
                    candidate.Report.PassRate - baselinePassRate,
                    candidate.Report.AverageLatencyMilliseconds - baselineLatency);
            })
            .ToArray();

        var perCase = baselineCases.Keys
            .OrderBy(id => id, StringComparer.Ordinal)
            .Select(caseId =>
            {
                var reference = baselineCases[caseId];
                var values = request.Candidates
                    .Select(candidate =>
                    {
                        var item = candidate.Report.Cases.Single(
                            value => string.Equals(
                                value.CaseId,
                                caseId,
                                StringComparison.Ordinal));
                        return new ModelComparisonCaseValue(
                            NormalizeLabel(candidate.Label),
                            item.Passed,
                            item.Score,
                            item.LatencyMilliseconds,
                            item.Provider,
                            item.Model,
                            item.Failures);
                    })
                    .ToArray();

                return new ModelComparisonCaseRow(
                    caseId,
                    reference.Title,
                    reference.AgentId,
                    values);
            })
            .ToArray();

        audit.Record(
            AuditAgents.User,
            "evaluation.model-comparison.compare",
            $"comparison:{workspace.CurrentWorkspaceId}",
            "user-supplied-benchmark-reports",
            AuditResults.Succeeded);

        return new ModelComparisonReport(
            PersonalAiRelease.Version,
            workspace.CurrentWorkspaceId,
            baselineLabel,
            baselineCases.Count,
            summaries,
            perCase);
    }

    private void ValidateReport(AgentBenchmarkReport report, string label)
    {
        if (report is null)
            throw new ModelComparisonValidationException(
                $"Candidate '{label}' thiếu benchmark report.");

        if (!string.Equals(
            report.WorkspaceId,
            workspace.CurrentWorkspaceId,
            StringComparison.OrdinalIgnoreCase))
        {
            throw new ModelComparisonValidationException(
                $"Candidate '{label}' không thuộc workspace hiện tại.");
        }

        if (report.ExecutedCases < 1 ||
            report.ExecutedCases != report.Cases.Count ||
            report.PassedCases + report.FailedCases != report.ExecutedCases)
        {
            throw new ModelComparisonValidationException(
                $"Benchmark report '{label}' có bộ đếm không nhất quán.");
        }

        if (!double.IsFinite(report.PassRate) ||
            report.PassRate < 0 || report.PassRate > 1 ||
            !double.IsFinite(report.AverageLatencyMilliseconds) ||
            report.AverageLatencyMilliseconds < 0)
        {
            throw new ModelComparisonValidationException(
                $"Benchmark report '{label}' có metric không hợp lệ.");
        }

        var calculatedPassRate =
            (double)report.PassedCases / report.ExecutedCases;
        if (Math.Abs(calculatedPassRate - report.PassRate) > 0.000001)
        {
            throw new ModelComparisonValidationException(
                $"Benchmark report '{label}' có passRate không khớp bộ đếm.");
        }

        _ = IndexCases(report, label);
    }

    private static Dictionary<string, AgentBenchmarkCaseResult> IndexCases(
        AgentBenchmarkReport report,
        string label)
    {
        var result = new Dictionary<string, AgentBenchmarkCaseResult>(
            StringComparer.Ordinal);

        foreach (var item in report.Cases)
        {
            if (string.IsNullOrWhiteSpace(item.CaseId) ||
                string.IsNullOrWhiteSpace(item.Title) ||
                string.IsNullOrWhiteSpace(item.AgentId) ||
                !double.IsFinite(item.Score) ||
                !double.IsFinite(item.LatencyMilliseconds) ||
                item.LatencyMilliseconds < 0)
            {
                throw new ModelComparisonValidationException(
                    $"Benchmark report '{label}' chứa case không hợp lệ.");
            }

            if (!result.TryAdd(item.CaseId, item))
            {
                throw new ModelComparisonValidationException(
                    $"Benchmark report '{label}' có case ID trùng '{item.CaseId}'.");
            }
        }

        return result;
    }

    private static string NormalizeLabel(string? value)
    {
        var label = (value ?? string.Empty).Trim();
        if (label.Length is < 1 or > 80)
            throw new ModelComparisonValidationException(
                "Nhãn model/candidate phải có từ 1 đến 80 ký tự.");
        return label;
    }

    private static string? DistinctOrNull(IEnumerable<string?> values)
    {
        var distinct = values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return distinct.Length == 1 ? distinct[0] : null;
    }
}
