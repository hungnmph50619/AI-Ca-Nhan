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
    IComputerOperatorBenchmarkIsolationService isolation,
    ComputerOperatorProgressStore progress,
    IWorkspaceContextAccessor workspace,
    IComputerOperatorExternalBenchmarkExecutionStore executions)
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

        var isolationResult =
            await isolation.PrepareAsync(
                scenario.Id,
                cancellationToken);

        if (!isolationResult.Ready)
        {
            throw new ComputerOperatorExternalBenchmarkValidationException(
                $"Benchmark precondition chưa sạch: {isolationResult.Detail}");
        }

        var runToken =
            BuildRunToken();

        var materializedGoal =
            MaterializeRunText(
                scenario.Goal,
                runToken);

        var materializedExpectedEffect =
            MaterializeRunText(
                scenario.ExpectedEffect,
                runToken);

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
                    materializedGoal,
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

        var readyForIndependentEvaluation =
            IsReadyForIndependentEvaluation(
                result.Completed,
                recordedSteps,
                scenario.MaximumSteps,
                result.VerifiedActionCount,
                result.VerificationFailureCount,
                result.RecoveryCount);

        var execution =
            new ComputerOperatorExternalBenchmarkExecutionResult(
                plan.Version,
                workspace.CurrentWorkspaceId,
                scenario.CaseId,
                scenario.Title,
                materializedExpectedEffect,
                startedAt,
            DateTimeOffset.UtcNow,
            result.Completed,
            withinStepBudget,
            readyForIndependentEvaluation,
            scenario.MaximumSteps,
            recordedSteps,
            snapshot.ObservationCount,
            snapshot.ActionCount,
            result.Summary,
            result.Provider,
            result.Model,
            stopwatch.Elapsed.TotalMilliseconds,
            result.VerifiedActionCount,
            result.VerificationFailureCount,
            result.VerificationInconclusiveCount,
            result.RecoveryCount);

        executions.Record(
            execution);

        return execution;
    }

    internal static bool IsReadyForIndependentEvaluationForAcceptance(
        bool taskCompleted,
        int recordedSteps,
        int maximumSteps,
        int verifiedActionCount = 1,
        int verificationFailureCount = 0,
        int recoveryCount = 0) =>
        IsReadyForIndependentEvaluation(
            taskCompleted,
            recordedSteps,
            maximumSteps,
            verifiedActionCount,
            verificationFailureCount,
            recoveryCount);

    private static bool IsReadyForIndependentEvaluation(
        bool taskCompleted,
        int recordedSteps,
        int maximumSteps,
        int verifiedActionCount,
        int verificationFailureCount,
        int recoveryCount) =>
        taskCompleted &&
        maximumSteps > 0 &&
        recordedSteps >= 0 &&
        recordedSteps <= maximumSteps &&
        verifiedActionCount > 0 &&
        verificationFailureCount >= 0 &&
        recoveryCount >= verificationFailureCount;

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

    internal static string MaterializeRunTextForAcceptance(
        string value,
        string runToken) =>
        MaterializeRunText(
            value,
            runToken);

    private static string BuildRunToken() =>
        DateTimeOffset.UtcNow.ToString(
            "yyyyMMddHHmmssfff",
            System.Globalization.CultureInfo.InvariantCulture);

    private static string MaterializeRunText(
        string value,
        string runToken)
    {
        const string marker =
            "PersonalAI benchmark text entry";

        if (string.IsNullOrWhiteSpace(value) ||
            !value.Contains(
                marker,
                StringComparison.Ordinal))
        {
            return value;
        }

        return value.Replace(
            marker,
            $"{marker} {runToken}",
            StringComparison.Ordinal);
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
