using System.Diagnostics;
using PersonalAI.Web.Evaluation.Benchmarks;
using PersonalAI.Web.Evaluation.Contracts;
using PersonalAI.Web.Evaluation.Core;
using PersonalAI.Web.Evaluation.Regression;
using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public interface IDevelopmentBenchmarkService
{
    DevelopmentBenchmarkStatus GetStatus();
    IReadOnlyList<DevelopmentBenchmarkReport> GetAll();
    DevelopmentBenchmarkReport? Get(Guid id);
    Task<DevelopmentBenchmarkReport> RunAsync(
        RunDevelopmentBenchmarkRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class DevelopmentBenchmarkService(
    IDevelopmentRunService runs,
    IRegressionDatasetStore regressionStore,
    IEvaluationEngine evaluation,
    IAgentBenchmarkService agentBenchmark,
    IWorkspaceContextAccessor workspace,
    IDevelopmentBenchmarkReportStore store,
    IAuditRecorder audit) : IDevelopmentBenchmarkService
{
    public const int HardMaximumCases = 100;

    public DevelopmentBenchmarkStatus GetStatus()
    {
        var all = store.GetAll();
        return new(
            PersonalAiRelease.Version,
            workspace.CurrentWorkspaceId,
            all.Count,
            all.Count(x => x.Passed),
            all.Count(x => !x.Passed),
            ReportsPersisted: true,
            FailedBenchmarkBlocksContinuation: true,
            DevelopmentBenchmarkGateNames.Required);
    }

    public IReadOnlyList<DevelopmentBenchmarkReport> GetAll() =>
        store.GetAll();

    public DevelopmentBenchmarkReport? Get(Guid id) =>
        store.Get(id);

    public async Task<DevelopmentBenchmarkReport> RunAsync(
        RunDevelopmentBenchmarkRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!request.ConfirmExecution)
            throw new DevelopmentBenchmarkValidationException(
                "Cần ConfirmExecution=true để chạy benchmark gate.");

        if (request.MaximumCases is < 1 or > HardMaximumCases)
            throw new DevelopmentBenchmarkValidationException(
                $"MaximumCases phải từ 1 đến {HardMaximumCases}.");

        ValidateThreshold(request.MinimumRagScore, nameof(request.MinimumRagScore), 0, 1);
        ValidateThreshold(request.MinimumToolScore, nameof(request.MinimumToolScore), 0, 1);
        ValidateThreshold(request.MinimumTaskScore, nameof(request.MinimumTaskScore), 0, 1);
        ValidateThreshold(request.MinimumAgentPassRate, nameof(request.MinimumAgentPassRate), 0, 1);

        if (!double.IsFinite(request.MaximumAverageAgentLatencyMs) ||
            request.MaximumAverageAgentLatencyMs <= 0 ||
            request.MaximumDurationMs <= 0 ||
            request.MaximumWorkingSetBytes <= 0)
        {
            throw new DevelopmentBenchmarkValidationException(
                "Các ngưỡng latency/duration/resource phải lớn hơn 0.");
        }

        if (request.IncludeAgentBenchmark && !request.ConfirmExternalExecution)
            throw new DevelopmentBenchmarkValidationException(
                "Agent benchmark có thể gọi AI provider; cần ConfirmExternalExecution=true.");

        var run = runs.Get(request.DevelopmentRunId)
            ?? throw new KeyNotFoundException("Không tìm thấy DevelopmentRun.");

        if (run.WorkspaceId != workspace.CurrentWorkspaceId)
            throw new DevelopmentBenchmarkValidationException(
                "DevelopmentRun không thuộc workspace hiện tại.");

        if (run.Status != "active" ||
            run.Stage != DevelopmentRunStages.Benchmark)
        {
            throw new DevelopmentBenchmarkValidationException(
                "Benchmark gate chỉ chạy khi DevelopmentRun đang ở stage benchmark.");
        }

        if (!run.Branch.StartsWith("experiment/", StringComparison.Ordinal))
            throw new DevelopmentBenchmarkValidationException(
                "Benchmark gate chỉ chạy trên experiment branch.");

        var process = Process.GetCurrentProcess();
        process.Refresh();
        var workingSetBefore = process.WorkingSet64;
        var stopwatch = Stopwatch.StartNew();
        var startedAt = DateTimeOffset.UtcNow;

        var dataset = regressionStore.GetAll()
            .Where(x => x.Enabled)
            .Take(request.MaximumCases)
            .ToArray();

        var supported = evaluation.Categories
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var results = new List<EvaluationResult>();
        var evaluationFailures = 0;

        foreach (var item in dataset)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.Equals(
                item.Category,
                AgentBenchmarkService.BenchmarkCategory,
                StringComparison.OrdinalIgnoreCase))
                continue;

            if (!supported.Contains(item.Category))
                continue;

            try
            {
                results.Add(evaluation.Evaluate(new EvaluationCase
                {
                    Id = item.Id,
                    Category = item.Category,
                    Input = item.Input,
                    Expected = item.Expected,
                    Metadata = item.Metadata.ToDictionary(
                        x => x.Key,
                        x => x.Value,
                        StringComparer.OrdinalIgnoreCase)
                }));
            }
            catch (EvaluationEngineException)
            {
                evaluationFailures++;
            }
        }

        var gates = new List<DevelopmentBenchmarkGateResult>();

        AddMetricGate(
            gates,
            DevelopmentBenchmarkGateNames.Rag,
            results,
            EvaluationMetricCatalog.RagAccuracy,
            request.MinimumRagScore);

        AddMetricGate(
            gates,
            DevelopmentBenchmarkGateNames.Tool,
            results,
            EvaluationMetricCatalog.ToolSuccessRate,
            request.MinimumToolScore);

        AddMetricGate(
            gates,
            DevelopmentBenchmarkGateNames.Task,
            results,
            EvaluationMetricCatalog.TaskSuccessRate,
            request.MinimumTaskScore);

        if (request.IncludeAgentBenchmark)
        {
            var agentCaseIds = dataset
                .Where(x => string.Equals(
                    x.Category,
                    AgentBenchmarkService.BenchmarkCategory,
                    StringComparison.OrdinalIgnoreCase))
                .Select(x => x.Id)
                .Take(AgentBenchmarkService.HardMaximumCases)
                .ToArray();

            if (agentCaseIds.Length == 0)
            {
                gates.Add(new(
                    DevelopmentBenchmarkGateNames.Agent,
                    Passed: false,
                    Observed: null,
                    Threshold: request.MinimumAgentPassRate,
                    Detail: "Không có agent.benchmark case đang bật."));
            }
            else
            {
                var report = await agentBenchmark.RunAsync(
                    new RunAgentBenchmarkRequest(
                        ConfirmExternalExecution: true,
                        CaseIds: agentCaseIds,
                        MaximumCases: agentCaseIds.Length),
                    cancellationToken);

                var passRatePassed =
                    report.PassRate >= request.MinimumAgentPassRate;
                var latencyPassed =
                    report.AverageLatencyMilliseconds <=
                    request.MaximumAverageAgentLatencyMs;

                gates.Add(new(
                    DevelopmentBenchmarkGateNames.Agent,
                    passRatePassed && latencyPassed,
                    report.PassRate,
                    request.MinimumAgentPassRate,
                    $"passRate={report.PassRate:0.###}; avgLatencyMs={report.AverageLatencyMilliseconds:0.##}; maxLatencyMs={request.MaximumAverageAgentLatencyMs:0.##}"));
            }
        }
        else
        {
            gates.Add(new(
                DevelopmentBenchmarkGateNames.Agent,
                Passed: false,
                Observed: null,
                Threshold: request.MinimumAgentPassRate,
                Detail: "Agent benchmark là gate bắt buộc; cần IncludeAgentBenchmark=true và explicit external confirmation."));
        }

        stopwatch.Stop();
        process.Refresh();
        var workingSetAfter = process.WorkingSet64;
        var observedWorkingSet = Math.Max(workingSetBefore, workingSetAfter);

        gates.Add(new(
            DevelopmentBenchmarkGateNames.Performance,
            stopwatch.ElapsedMilliseconds <= request.MaximumDurationMs,
            stopwatch.ElapsedMilliseconds,
            request.MaximumDurationMs,
            $"durationMs={stopwatch.ElapsedMilliseconds}; maxDurationMs={request.MaximumDurationMs}"));

        gates.Add(new(
            DevelopmentBenchmarkGateNames.Resource,
            observedWorkingSet <= request.MaximumWorkingSetBytes,
            observedWorkingSet,
            request.MaximumWorkingSetBytes,
            $"workingSetBytes={observedWorkingSet}; maxWorkingSetBytes={request.MaximumWorkingSetBytes}"));

        var failedCases = evaluationFailures +
            results.Count(x => x.Errors.Count > 0);

        var passed = gates.All(x => x.Passed);

        var reportOut = new DevelopmentBenchmarkReport(
            Guid.NewGuid(),
            workspace.CurrentWorkspaceId,
            run.Id,
            gates,
            results.Count,
            failedCases,
            passed,
            stopwatch.ElapsedMilliseconds,
            observedWorkingSet,
            startedAt,
            DateTimeOffset.UtcNow);

        store.Save(reportOut);

        audit.Record(
            AuditAgents.System,
            "development.benchmark.complete",
            $"development-run:{run.Id:D}",
            $"report:{reportOut.Id:D};passed:{passed};failed-gates:{gates.Count(x => !x.Passed)};cases:{results.Count}",
            passed ? AuditResults.Succeeded : AuditResults.Failed);

        return reportOut;
    }

    private static void AddMetricGate(
        List<DevelopmentBenchmarkGateResult> gates,
        string gateName,
        IReadOnlyList<EvaluationResult> results,
        string metricName,
        double threshold)
    {
        var values = results
            .Where(x => x.Metrics is not null &&
                        x.Metrics.ContainsKey(metricName))
            .Select(x => x.Metrics[metricName])
            .Where(double.IsFinite)
            .ToArray();

        if (values.Length == 0)
        {
            gates.Add(new(
                gateName,
                Passed: false,
                Observed: null,
                Threshold: threshold,
                Detail: $"Không có metric '{metricName}' trong regression dataset hiện tại."));
            return;
        }

        var observed = values.Average();
        gates.Add(new(
            gateName,
            observed >= threshold,
            observed,
            threshold,
            $"metric={metricName}; samples={values.Length}; average={observed:0.###}; minimum={threshold:0.###}"));
    }

    private static void ValidateThreshold(
        double value,
        string name,
        double minimum,
        double maximum)
    {
        if (!double.IsFinite(value) || value < minimum || value > maximum)
            throw new DevelopmentBenchmarkValidationException(
                $"{name} phải nằm trong khoảng {minimum}..{maximum}.");
    }

}
