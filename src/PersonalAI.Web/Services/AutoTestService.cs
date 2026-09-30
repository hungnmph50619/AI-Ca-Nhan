using System.Globalization;
using PersonalAI.Web.Evaluation.Benchmarks;
using PersonalAI.Web.Evaluation.Contracts;
using PersonalAI.Web.Evaluation.Core;
using PersonalAI.Web.Evaluation.Regression;
using PersonalAI.Web.Models;
using PersonalAI.Web.SelfImprovement;

namespace PersonalAI.Web.Services;

public interface IAutoTestService
{
    Task<AutoTestResult> RunAsync(
        RunAutoTestRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class AutoTestService(
    IRegressionDatasetStore regressionStore,
    IEvaluationEngine evaluation,
    IAgentBenchmarkService agentBenchmark,
    IDevelopmentAgentService development,
    IWorkspaceContextAccessor workspace,
    IAuditRecorder audit) : IAutoTestService
{
    public const int DefaultMaximumCases = 50;
    public const int MaximumCases = 100;

    public async Task<AutoTestResult> RunAsync(
        RunAutoTestRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Experiment);
        ArgumentNullException.ThrowIfNull(request.SelfCoding);
        ArgumentNullException.ThrowIfNull(request.Review);

        ValidateSequence(request);

        var maximumCases = request.MaximumCases ?? DefaultMaximumCases;
        if (maximumCases is < 1 or > MaximumCases)
            throw new AutoTestValidationException(
                $"MaximumCases phải từ 1 đến {MaximumCases}.");

        if (request.IncludeAgentBenchmark && !request.ConfirmExternalExecution)
            throw new AutoTestValidationException(
                "Agent Benchmark có thể gọi AI provider; cần confirmExternalExecution=true.");

        var branch = await development.GetCurrentBranchAsync(
            request.Experiment.RepositoryPath,
            cancellationToken);
        if (!branch.Succeeded ||
            !string.Equals(
                branch.Branch,
                request.Experiment.ExperimentBranch,
                StringComparison.Ordinal))
        {
            throw new AutoTestValidationException(
                "Auto Test chỉ chạy khi repository đang ở đúng experiment branch.");
        }

        var startedAt = DateTimeOffset.UtcNow;
        var dataset = regressionStore.GetAll()
            .Where(item => item.Enabled)
            .Take(maximumCases)
            .ToArray();

        var supported = evaluation.Categories.ToHashSet(
            StringComparer.OrdinalIgnoreCase);
        var caseResults = new List<AutoTestCaseResult>();
        var evaluationResults = new List<EvaluationResult>();

        foreach (var item in dataset)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.Equals(
                item.Category,
                AgentBenchmarkService.BenchmarkCategory,
                StringComparison.OrdinalIgnoreCase))
            {
                caseResults.Add(new AutoTestCaseResult(
                    item.Id,
                    item.Title,
                    item.Category,
                    AutoTestStatuses.Skipped,
                    null,
                    [],
                    request.IncludeAgentBenchmark
                        ? "Case được chạy trong Agent Benchmark phase."
                        : "Agent Benchmark chưa được yêu cầu."));
                continue;
            }

            if (!supported.Contains(item.Category))
            {
                caseResults.Add(new AutoTestCaseResult(
                    item.Id,
                    item.Title,
                    item.Category,
                    AutoTestStatuses.Skipped,
                    null,
                    [],
                    "Chưa có evaluator đăng ký cho category này."));
                continue;
            }

            try
            {
                var result = evaluation.Evaluate(new EvaluationCase
                {
                    Id = item.Id,
                    Category = item.Category,
                    Input = item.Input,
                    Expected = item.Expected,
                    Metadata = item.Metadata.ToDictionary(
                        pair => pair.Key,
                        pair => pair.Value,
                        StringComparer.OrdinalIgnoreCase)
                });

                evaluationResults.Add(result);
                var minimumScore = ReadMinimumScore(item);
                var passed =
                    result.Errors.Count == 0 &&
                    (minimumScore is null || result.Score >= minimumScore.Value);

                caseResults.Add(new AutoTestCaseResult(
                    item.Id,
                    item.Title,
                    item.Category,
                    passed ? AutoTestStatuses.Passed : AutoTestStatuses.Failed,
                    result.Score,
                    result.Errors,
                    minimumScore is null
                        ? "Pass khi evaluator không trả error."
                        : $"Pass khi không có error và score >= {minimumScore.Value.ToString("0.###", CultureInfo.InvariantCulture)}."));
            }
            catch (EvaluationEngineException exception)
            {
                caseResults.Add(new AutoTestCaseResult(
                    item.Id,
                    item.Title,
                    item.Category,
                    AutoTestStatuses.Failed,
                    null,
                    ["evaluation-engine-error"],
                    exception.Message));
            }
        }

        AgentBenchmarkReport? benchmark = null;
        if (request.IncludeAgentBenchmark)
        {
            var agentCases = dataset
                .Where(item =>
                    string.Equals(
                        item.Category,
                        AgentBenchmarkService.BenchmarkCategory,
                        StringComparison.OrdinalIgnoreCase))
                .Select(item => item.Id)
                .Take(AgentBenchmarkService.HardMaximumCases)
                .ToArray();

            if (agentCases.Length > 0)
            {
                benchmark = await agentBenchmark.RunAsync(
                    new RunAgentBenchmarkRequest(
                        ConfirmExternalExecution: true,
                        CaseIds: agentCases,
                        MaximumCases: agentCases.Length),
                    cancellationToken);

                foreach (var item in benchmark.Cases)
                {
                    var existingIndex = caseResults.FindIndex(value =>
                        string.Equals(
                            value.CaseId,
                            item.CaseId,
                            StringComparison.Ordinal));

                    var converted = new AutoTestCaseResult(
                        item.CaseId,
                        item.Title,
                        AgentBenchmarkService.BenchmarkCategory,
                        item.Passed
                            ? AutoTestStatuses.Passed
                            : AutoTestStatuses.Failed,
                        item.Score,
                        item.Failures,
                        $"Agent={item.AgentId}; latency={item.LatencyMilliseconds:F0} ms; provider={item.Provider ?? "n/a"}; model={item.Model ?? "n/a"}.");

                    if (existingIndex >= 0)
                        caseResults[existingIndex] = converted;
                    else
                        caseResults.Add(converted);
                }
            }
        }

        var executed = caseResults.Count(item =>
            item.Status is AutoTestStatuses.Passed or AutoTestStatuses.Failed);
        var passedCases = caseResults.Count(item =>
            item.Status == AutoTestStatuses.Passed);
        var failedCases = caseResults.Count(item =>
            item.Status == AutoTestStatuses.Failed);
        var skippedCases = caseResults.Count(item =>
            item.Status == AutoTestStatuses.Skipped);

        EvaluationSummary? summary = evaluationResults.Count > 0
            ? EvaluationMetricsAggregator.Summarize(evaluationResults)
            : null;

        var ready =
            request.Review.ReadyForHumanDecision &&
            failedCases == 0 &&
            executed > 0;

        var status = ready
            ? AutoTestStatuses.ReadyForPromotionReview
            : AutoTestStatuses.ChangesRequired;

        audit.Record(
            AuditAgents.System,
            "self-improvement.auto-test",
            $"experiment:{request.Experiment.ExperimentId}",
            request.IncludeAgentBenchmark
                ? "local-regression-plus-confirmed-agent-benchmark"
                : "local-regression-only",
            ready ? AuditResults.Prepared : AuditResults.Failed);

        return new AutoTestResult(
            PersonalAiRelease.Version,
            workspace.CurrentWorkspaceId,
            request.Experiment.ExperimentId,
            request.Experiment.ExperimentBranch,
            startedAt,
            DateTimeOffset.UtcNow,
            dataset.Length,
            executed,
            passedCases,
            failedCases,
            skippedCases,
            caseResults
                .OrderBy(item => item.CaseId, StringComparer.Ordinal)
                .ToArray(),
            summary,
            benchmark,
            status,
            ReadyForPromotionReview: ready,
            CommitCreated: false,
            Pushed: false,
            Merged: false,
            Deployed: false);
    }

    private void ValidateSequence(RunAutoTestRequest request)
    {
        foreach (var workspaceId in new[]
        {
            request.Experiment.WorkspaceId,
            request.SelfCoding.WorkspaceId,
            request.Review.WorkspaceId
        })
        {
            if (!string.Equals(
                workspaceId,
                workspace.CurrentWorkspaceId,
                StringComparison.OrdinalIgnoreCase))
            {
                throw new AutoTestValidationException(
                    "Experiment/Self Coding/Review không thuộc workspace hiện tại.");
            }
        }

        if (!string.Equals(
                request.Experiment.ExperimentId,
                request.SelfCoding.ExperimentId,
                StringComparison.Ordinal) ||
            !string.Equals(
                request.Experiment.ExperimentId,
                request.Review.ExperimentId,
                StringComparison.Ordinal) ||
            !string.Equals(
                request.Experiment.ExperimentBranch,
                request.SelfCoding.Branch,
                StringComparison.Ordinal) ||
            !string.Equals(
                request.Experiment.ExperimentBranch,
                request.Review.Branch,
                StringComparison.Ordinal))
        {
            throw new AutoTestValidationException(
                "Experiment, Self Coding và Automated Review không khớp nhau.");
        }

        if (!request.Experiment.ExperimentBranch.StartsWith(
            "experiment/",
            StringComparison.Ordinal))
        {
            throw new AutoTestValidationException(
                "Auto Test chỉ nhận branch experiment/*.");
        }

        if (request.SelfCoding.FilesChanged < 1 ||
            request.SelfCoding.CommitCreated ||
            request.SelfCoding.Pushed ||
            request.SelfCoding.Merged ||
            request.Review.CommitCreated ||
            request.Review.Pushed ||
            request.Review.Merged ||
            request.Review.Deployed)
        {
            throw new AutoTestValidationException(
                "Auto Test chỉ chạy trước commit/push/merge/deploy.");
        }
    }

    private static double? ReadMinimumScore(RegressionDatasetItem item)
    {
        if (!item.Metadata.TryGetValue("minimumScore", out var raw) ||
            string.IsNullOrWhiteSpace(raw))
            return null;

        if (!double.TryParse(
            raw,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out var value) ||
            !double.IsFinite(value))
        {
            throw new EvaluationEngineException(
                $"{item.Id}: metadata minimumScore không hợp lệ.");
        }

        return value;
    }
}
