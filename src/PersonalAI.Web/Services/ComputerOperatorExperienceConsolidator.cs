namespace PersonalAI.Web.Services;

public sealed record ComputerOperatorConsolidationResult(
    int TrustedExperiencesScanned,
    int PatternsWritten,
    string Reason);

public interface IComputerOperatorExperienceConsolidator
{
    ComputerOperatorConsolidationResult Consolidate();
}

public sealed class ComputerOperatorExperienceConsolidator(
    IComputerOperatorExperienceRepository experiences,
    IComputerOperatorExperienceAggregateStore aggregates)
    : IComputerOperatorExperienceConsolidator
{
    public ComputerOperatorConsolidationResult Consolidate()
    {
        var trusted =
            experiences.GetRecent(
                    IComputerOperatorExperienceRepository.MaximumValidationScan)
                .Where(item =>
                    item.Kind ==
                        ComputerOperatorExperienceKinds.Experience &&
                    item.Verified &&
                    item.Outcome ==
                        "trusted-verified")
                .ToArray();

        var groups =
            trusted
                .GroupBy(item => new
                {
                    item.ContextFingerprint,
                    item.FailureCode,
                    item.StrategyKey
                })
                .ToArray();

        foreach (var group in groups)
        {
            aggregates.UpsertPattern(
                new(
                    group.Key.ContextFingerprint,
                    group.Key.FailureCode,
                    group.Key.StrategyKey,
                    group.Count(),
                    group.Average(item =>
                        item.Confidence),
                    (long)Math.Round(
                        group.Average(item =>
                            (double)item.DurationMilliseconds)),
                    group.Max(item =>
                        item.CreatedAt)));
        }

        return new(
            trusted.Length,
            groups.Length,
            groups.Length == 0
                ? "Chưa có trusted experience để consolidate."
                : $"Đã cô đọng {trusted.Length} trusted experience thành {groups.Length} pattern.");
    }
}
