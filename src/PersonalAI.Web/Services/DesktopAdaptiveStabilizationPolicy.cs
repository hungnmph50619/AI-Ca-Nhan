namespace PersonalAI.Web.Services;

public static class DesktopAdaptiveStabilizationPolicy
{
    public const double SignatureDifferenceThreshold = 7.5;
    public const int RequiredConsecutiveStableComparisons = 2;
    public static readonly TimeSpan MinimumSampleFloor =
        TimeSpan.FromMilliseconds(80);
    public static readonly TimeSpan MaximumSampleInterval =
        TimeSpan.FromMilliseconds(160);
}

public sealed class DesktopAdaptiveStabilityTracker
{
    private int consecutiveStableComparisons;

    public int ConsecutiveStableComparisons =>
        consecutiveStableComparisons;

    public bool Observe(
        bool comparable,
        double signatureDifference)
    {
        var stable =
            comparable &&
            signatureDifference <=
                DesktopAdaptiveStabilizationPolicy
                    .SignatureDifferenceThreshold;

        consecutiveStableComparisons =
            stable
                ? consecutiveStableComparisons + 1
                : 0;

        return consecutiveStableComparisons >=
            DesktopAdaptiveStabilizationPolicy
                .RequiredConsecutiveStableComparisons;
    }

    public void Reset() =>
        consecutiveStableComparisons = 0;
}
