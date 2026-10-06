using System.Diagnostics;
using PersonalAI.Web.Evaluation.Benchmarks;

namespace PersonalAI.Web.Services;

public interface IComputerOperatorExternalBenchmarkRunner
{
    Task<ComputerOperatorExternalBenchmarkExecutionResult> RunCaseAsync(
        RunComputerOperatorExternalBenchmarkCaseRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class ComputerOperatorExternalBenchmarkRunner(
    IComputerOperatorExternalBenchmarkService benchmark,
    IComputerOperatorTaskService computerOperator,
    IComputerUseService computer,
    ComputerOperatorProgressStore progress,
    IWorkspaceContextAccessor workspace)
    : IComputerOperatorExternalBenchmarkRunner
{
    public const int HardTimeoutSeconds = 180;

    public async Task<ComputerOperatorExternalBenchmarkExecutionResult> RunCaseAsync(
        RunComputerOperatorExternalBenchmarkCaseRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        EnsureConfirmed(
            request.Confirmed);

        var caseId =
            (request.CaseId ?? string.Empty)
                .Trim();

        if (caseId.Length == 0)
            throw new ComputerOperatorExternalBenchmarkValidationException(
                "Phải chọn một benchmark case để chạy.");

        var plan =
            benchmark.Prepare(
                new(
                    CaseIds: [caseId],
                    MaximumCases: 1));

        var scenario =
            plan.Cases.Single();

        if (scenario.RequiresInteractiveDesktop)
        {
            var status =
                computer.GetStatus();

            EnsureInteractiveDesktop(
                status);
        }

        var startedAt =
            DateTimeOffset.UtcNow;

        var stopwatch =
            Stopwatch.StartNew();

        using var timeout =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);

        timeout.CancelAfter(
            TimeSpan.FromSeconds(
                HardTimeoutSeconds));

        ComputerOperatorTaskResult result;

        try
        {
            result =
                await computerOperator.RunAsync(
                    scenario.Goal,
                    timeout.Token);
        }
        catch (OperationCanceledException)
            when (!cancellationToken.IsCancellationRequested)
        {
            throw new ComputerOperatorExternalBenchmarkValidationException(
                $"Benchmark case vượt quá thời gian chạy tối đa {HardTimeoutSeconds} giây.");
        }

        stopwatch.Stop();

        var snapshot =
            progress.Get();

        var recordedSteps =
            result.Steps.Count;

        var withinStepBudget =
            recordedSteps <= scenario.MaximumSteps;

        return new(
            result.Provider is null
                ? plan.Version
                : plan.Version,
            workspace.CurrentWorkspaceId,
            scenario.CaseId,
            scenario.Title,
            startedAt,
            DateTimeOffset.UtcNow,
            result.Completed,
            withinStepBudget,
            result.Completed && withinStepBudget,
            scenario.MaximumSteps,
            recordedSteps,
            snapshot.ObservationCount,
            snapshot.ActionCount,
            result.Summary,
            result.Provider,
            result.Model,
            stopwatch.Elapsed.TotalMilliseconds);
    }

    internal static void EnsureConfirmedForAcceptance(
        bool confirmed) =>
        EnsureConfirmed(
            confirmed);

    internal static void EnsureInteractiveDesktopForAcceptance(
        bool supported,
        bool interactiveSession)
    {
        EnsureInteractiveDesktop(
            new(
                Version: "acceptance",
                Platform: "windows",
                Supported: supported,
                InteractiveSession: interactiveSession,
                AvailableCapabilities: Array.Empty<string>(),
                Limitations: Array.Empty<string>()));
    }

    private static void EnsureConfirmed(
        bool confirmed)
    {
        if (!confirmed)
            throw new ComputerOperatorExternalBenchmarkValidationException(
                "Phải xác nhận trước khi chạy benchmark trên desktop thật.");
    }

    private static void EnsureInteractiveDesktop(
        PersonalAI.Web.Models.ComputerUseStatusResponse status)
    {
        if (!status.Supported)
            throw new ComputerOperatorExternalBenchmarkValidationException(
                "Máy hiện tại không hỗ trợ Computer Use.");

        if (!status.InteractiveSession)
            throw new ComputerOperatorExternalBenchmarkValidationException(
                "External benchmark cần một Windows interactive desktop session.");
    }
}
