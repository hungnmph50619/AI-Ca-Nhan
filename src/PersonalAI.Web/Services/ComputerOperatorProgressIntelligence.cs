namespace PersonalAI.Web.Services;

public sealed record ComputerOperatorProgressObservation(
    bool ExpectedEffectObserved,
    double ExpectedEffectConfidence,
    bool ExplicitFailureObserved,
    double FailureConfidence,
    bool BlockingConditionObserved,
    double BlockingConfidence,
    bool ExternalWaitObserved,
    double ExternalWaitConfidence,
    bool ProcessAlive,
    bool? ProcessResponding,
    double CpuActivityScore,
    double DiskActivityScore,
    double NetworkActivityScore,
    double WindowTransitionScore,
    double StructuredUiChangeScore,
    double VisualChangeScore,
    double RelevantSystemEventScore,
    TimeSpan Elapsed,
    TimeSpan TimeSinceMeaningfulProgress);

public sealed record ComputerOperatorProgressAssessment(
    double ProgressScore,
    double ResourceActivityScore,
    int CorroboratingProgressSignals,
    bool MeaningfulProgress,
    ComputerOperatorRuntimeStateAssessment RuntimeState,
    AdaptiveProgressSample AdaptiveSample,
    string Reason);

public interface IComputerOperatorProgressIntelligence
{
    ComputerOperatorProgressAssessment Assess(
        ComputerOperatorProgressObservation observation);
}

/// <summary>
/// Hợp nhất các tín hiệu tiến triển độc lập trước khi đưa vào Runtime State Intelligence.
/// Core chỉ hiểu loại tín hiệu, không biết tên ứng dụng cụ thể.
/// </summary>
public sealed class ComputerOperatorProgressIntelligence(
    IComputerOperatorRuntimeStateIntelligence runtimeState)
    : IComputerOperatorProgressIntelligence
{
    public ComputerOperatorProgressAssessment Assess(
        ComputerOperatorProgressObservation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);
        Validate(observation);

        var progressSignals = new[]
        {
            observation.WindowTransitionScore,
            observation.StructuredUiChangeScore,
            observation.VisualChangeScore,
            observation.RelevantSystemEventScore
        }
        .Select(value => Math.Clamp(value, 0, 1))
        .ToArray();

        var strongSignals =
            progressSignals.Count(value => value >= 0.65);

        var moderateSignals =
            progressSignals.Count(value => value >= 0.45);

        var strongestProgress =
            progressSignals.Max();

        var corroborationBoost =
            strongSignals >= 2
                ? 0.15
                : moderateSignals >= 2
                    ? 0.08
                    : 0;

        var progressScore =
            Math.Clamp(
                strongestProgress + corroborationBoost,
                0,
                1);

        // Resource activity là bằng chứng "còn sống", không tự chứng minh
        // expected effect. Dùng max + boost nhẹ khi nhiều nguồn cùng hoạt động.
        var resources = new[]
        {
            observation.CpuActivityScore,
            observation.DiskActivityScore,
            observation.NetworkActivityScore
        }
        .Select(value => Math.Clamp(value, 0, 1))
        .ToArray();

        var activeResources =
            resources.Count(value => value >= 0.20);

        var resourceScore =
            Math.Clamp(
                resources.Max() +
                (activeResources >= 2 ? 0.10 : 0),
                0,
                1);

        var meaningfulProgress =
            progressScore >= 0.65;

        var evidence =
            new ComputerOperatorRuntimeEvidence(
                observation.ExpectedEffectObserved,
                observation.ExpectedEffectConfidence,
                observation.ExplicitFailureObserved,
                observation.FailureConfidence,
                observation.BlockingConditionObserved,
                observation.BlockingConfidence,
                meaningfulProgress,
                progressScore,
                observation.ExternalWaitObserved,
                observation.ExternalWaitConfidence,
                observation.ProcessAlive,
                observation.ProcessResponding,
                resourceScore,
                observation.Elapsed,
                observation.TimeSinceMeaningfulProgress);

        var runtime =
            runtimeState.Classify(evidence);

        var adaptive =
            ToAdaptiveSample(
                runtime,
                progressScore,
                meaningfulProgress);

        var reason =
            $"progress={progressScore:0.00}; resources={resourceScore:0.00}; " +
            $"corroborating={strongSignals}; state={runtime.State}. {runtime.Reason}";

        return new(
            progressScore,
            resourceScore,
            strongSignals,
            meaningfulProgress,
            runtime,
            adaptive,
            reason);
    }

    internal static AdaptiveProgressSample ToAdaptiveSample(
        ComputerOperatorRuntimeStateAssessment runtime,
        double progressScore,
        bool meaningfulProgress)
    {
        return runtime.State switch
        {
            ComputerOperatorRuntimeStates.Completed =>
                new(
                    AdaptiveWaitStatuses.Verified,
                    runtime.Confidence,
                    runtime.Reason,
                    MeaningfulProgress: true),

            ComputerOperatorRuntimeStates.Failed =>
                new(
                    AdaptiveWaitStatuses.Failed,
                    runtime.Confidence,
                    runtime.Reason,
                    MeaningfulProgress: false),

            ComputerOperatorRuntimeStates.Hung or
            ComputerOperatorRuntimeStates.PossiblyStalled =>
                new(
                    AdaptiveWaitStatuses.Stalled,
                    runtime.Confidence,
                    runtime.Reason,
                    MeaningfulProgress: false),

            ComputerOperatorRuntimeStates.Progressing =>
                new(
                    AdaptiveWaitStatuses.Progressing,
                    Math.Max(runtime.Confidence, progressScore),
                    runtime.Reason,
                    MeaningfulProgress: meaningfulProgress),

            _ =>
                new(
                    AdaptiveWaitStatuses.Pending,
                    runtime.Confidence,
                    runtime.Reason,
                    MeaningfulProgress: false)
        };
    }

    private static void Validate(
        ComputerOperatorProgressObservation observation)
    {
        var scores = new[]
        {
            observation.ExpectedEffectConfidence,
            observation.FailureConfidence,
            observation.BlockingConfidence,
            observation.ExternalWaitConfidence,
            observation.CpuActivityScore,
            observation.DiskActivityScore,
            observation.NetworkActivityScore,
            observation.WindowTransitionScore,
            observation.StructuredUiChangeScore,
            observation.VisualChangeScore,
            observation.RelevantSystemEventScore
        };

        if (scores.Any(value => value is < 0 or > 1) ||
            observation.Elapsed < TimeSpan.Zero ||
            observation.TimeSinceMeaningfulProgress < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(observation),
                "Bằng chứng Progress Intelligence không hợp lệ.");
        }
    }
}
