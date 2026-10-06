using PersonalAI.Web.Evaluation.Benchmarks;
using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public interface IComputerOperatorExternalBenchmarkExecutionStore
{
    void Record(ComputerOperatorExternalBenchmarkExecutionResult result);

    ComputerOperatorExternalBenchmarkExecutionResult? GetLatest(
        string caseId);
}

public sealed class ComputerOperatorExternalBenchmarkExecutionStore
    : IComputerOperatorExternalBenchmarkExecutionStore
{
    private readonly object _sync = new();
    private readonly Dictionary<string, ComputerOperatorExternalBenchmarkExecutionResult>
        _latestByCase =
            new(StringComparer.Ordinal);

    public void Record(
        ComputerOperatorExternalBenchmarkExecutionResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        lock (_sync)
            _latestByCase[result.CaseId] = result;
    }

    public ComputerOperatorExternalBenchmarkExecutionResult? GetLatest(
        string caseId)
    {
        var normalized =
            (caseId ?? string.Empty)
                .Trim();

        if (normalized.Length == 0)
            return null;

        lock (_sync)
            return _latestByCase.TryGetValue(
                normalized,
                out var result)
                ? result
                : null;
    }
}

public interface IComputerOperatorExternalBenchmarkEvaluationService
{
    ComputerOperatorExternalBenchmarkEvaluationResult Evaluate(
        EvaluateComputerOperatorExternalBenchmarkCaseRequest request);
}

public sealed class ComputerOperatorExternalBenchmarkEvaluationService(
    IComputerOperatorExternalBenchmarkExecutionStore executions,
    IWorkspaceContextAccessor workspace)
    : IComputerOperatorExternalBenchmarkEvaluationService
{
    public const int MaximumEvidenceSummaryLength = 2000;

    public ComputerOperatorExternalBenchmarkEvaluationResult Evaluate(
        EvaluateComputerOperatorExternalBenchmarkCaseRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!request.Confirmed)
            throw new ComputerOperatorExternalBenchmarkValidationException(
                "Phải xác nhận đánh giá độc lập trước khi chốt kết quả benchmark.");

        var caseId =
            (request.CaseId ?? string.Empty)
                .Trim();

        if (caseId.Length == 0)
            throw new ComputerOperatorExternalBenchmarkValidationException(
                "Phải chọn benchmark case cần đánh giá.");

        var execution =
            executions.GetLatest(
                caseId);

        if (execution is null)
            throw new ComputerOperatorExternalBenchmarkValidationException(
                "Chưa có lần chạy benchmark nào của case này để đánh giá.");

        if (!execution.ReadyForIndependentEvaluation)
            throw new ComputerOperatorExternalBenchmarkValidationException(
                "Lần chạy benchmark gần nhất chưa đủ điều kiện để đánh giá độc lập.");

        var evidenceSummary =
            (request.EvidenceSummary ?? string.Empty)
                .Trim();

        if (evidenceSummary.Length is < 8 or > MaximumEvidenceSummaryLength)
            throw new ComputerOperatorExternalBenchmarkValidationException(
                $"Mô tả evidence phải có từ 8 đến {MaximumEvidenceSummaryLength} ký tự.");

        var passed =
            DeterminePassed(
                execution.TaskCompleted,
                execution.WithinStepBudget,
                request.ExpectedEffectObserved);

        return new(
            PersonalAiRelease.Version,
            workspace.CurrentWorkspaceId,
            execution.CaseId,
            execution.Title,
            DateTimeOffset.UtcNow,
            execution.ExpectedEffect,
            execution.TaskCompleted,
            execution.WithinStepBudget,
            request.ExpectedEffectObserved,
            evidenceSummary,
            passed,
            passed ? "pass" : "fail",
            "independent-evidence-review");
    }

    internal static bool DeterminePassedForAcceptance(
        bool taskCompleted,
        bool withinStepBudget,
        bool expectedEffectObserved) =>
        DeterminePassed(
            taskCompleted,
            withinStepBudget,
            expectedEffectObserved);

    private static bool DeterminePassed(
        bool taskCompleted,
        bool withinStepBudget,
        bool expectedEffectObserved) =>
        taskCompleted &&
        withinStepBudget &&
        expectedEffectObserved;
}
