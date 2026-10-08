using PersonalAI.Web.Evaluation.Benchmarks;

namespace PersonalAI.Web.Services;

public sealed record RunComputerOperatorExternalBenchmarkSuiteRequest(
    bool Confirmed);

public sealed record ComputerOperatorExternalBenchmarkSuiteResult(
    string Version,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc,
    int PlannedCases,
    int CompletedCases,
    int ReadyForIndependentEvaluationCases,
    bool Completed,
    IReadOnlyList<ComputerOperatorExternalBenchmarkExecutionResult> Results);

public interface IComputerOperatorExternalBenchmarkSuiteRunner
{
    Task<ComputerOperatorExternalBenchmarkSuiteResult> RunAsync(
        RunComputerOperatorExternalBenchmarkSuiteRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class ComputerOperatorExternalBenchmarkSuiteRunner(
    IComputerOperatorBenchmarkScenarioPackService scenarios,
    IComputerOperatorExternalBenchmarkRunner runner)
    : IComputerOperatorExternalBenchmarkSuiteRunner
{
    public async Task<ComputerOperatorExternalBenchmarkSuiteResult> RunAsync(
        RunComputerOperatorExternalBenchmarkSuiteRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!request.Confirmed)
            throw new ComputerOperatorExternalBenchmarkValidationException(
                "Phải xác nhận trước khi chạy toàn bộ external benchmark suite trên desktop thật.");

        var pack =
            scenarios.GetPack();

        var startedAt =
            DateTimeOffset.UtcNow;

        var results =
            new List<ComputerOperatorExternalBenchmarkExecutionResult>(
                pack.Scenarios.Count);

        foreach (var scenario in pack.Scenarios)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var caseId =
                $"external-{scenario.Id}";

            var result =
                await runner.RunCaseAsync(
                    new(
                        CaseId: caseId,
                        Confirmed: true),
                    cancellationToken);

            results.Add(
                result);
        }

        var ready =
            results.Count(item =>
                item.ReadyForIndependentEvaluation);

        return new(
            pack.Version,
            startedAt,
            DateTimeOffset.UtcNow,
            pack.Scenarios.Count,
            results.Count,
            ready,
            Completed:
                results.Count == pack.Scenarios.Count,
            results);
    }
}
