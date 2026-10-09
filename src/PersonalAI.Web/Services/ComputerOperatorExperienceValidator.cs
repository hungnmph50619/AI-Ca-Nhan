namespace PersonalAI.Web.Services;

public static class ComputerOperatorExperienceValidationStatuses
{
    public const string Rejected = "rejected";
    public const string Pending = "pending";
    public const string Promoted = "promoted";
}

public sealed record ComputerOperatorExperienceValidationResult(
    string Status,
    bool Promoted,
    Guid CandidateId,
    Guid? ExperienceId,
    int VerifiedRecoveryCount,
    int ConflictingFailureCount,
    double Confidence,
    string Reason);

public interface IComputerOperatorExperienceValidator
{
    ComputerOperatorExperienceValidationResult Validate(
        Guid candidateId);
}

/// <summary>
/// Chỉ promote candidate khi có bằng chứng lặp lại đã VERIFY PASS.
/// Không tin một lần recovery đơn lẻ và không dùng candidate chưa verified làm fast path.
/// </summary>
public sealed class ComputerOperatorExperienceValidator(
    IComputerOperatorExperienceRepository experiences)
    : IComputerOperatorExperienceValidator
{
    public const int MinimumVerifiedRecoveries = 2;
    public const double MinimumAverageRecoveryConfidence = 0.85;

    public ComputerOperatorExperienceValidationResult Validate(
        Guid candidateId)
    {
        var recent =
            experiences.GetRecent(
                IComputerOperatorExperienceRepository.MaximumValidationScan);

        var candidate =
            recent.FirstOrDefault(item =>
                item.Id == candidateId &&
                item.Kind ==
                    ComputerOperatorExperienceKinds.Candidate);

        if (candidate is null)
        {
            return new(
                ComputerOperatorExperienceValidationStatuses.Rejected,
                false,
                candidateId,
                null,
                0,
                0,
                0,
                "Không tìm thấy candidate trong workspace hiện tại.");
        }

        if (candidate.Verified)
        {
            return new(
                ComputerOperatorExperienceValidationStatuses.Rejected,
                false,
                candidateId,
                null,
                0,
                0,
                candidate.Confidence,
                "Candidate không được phép tự mang trạng thái trusted/verified.");
        }

        var matchingRecoveries =
            recent
                .Where(item =>
                    item.Kind ==
                        ComputerOperatorExperienceKinds.Recovery &&
                    item.Verified &&
                    item.Outcome ==
                        "verified-pass" &&
                    item.ContextFingerprint ==
                        candidate.ContextFingerprint &&
                    item.FailureCode ==
                        candidate.FailureCode &&
                    item.StrategyKey ==
                        candidate.StrategyKey)
                .ToArray();

        var conflictingFailures =
            recent
                .Where(item =>
                    item.Kind ==
                        ComputerOperatorExperienceKinds.Failure &&
                    item.ContextFingerprint ==
                        candidate.ContextFingerprint &&
                    item.StrategyKey ==
                        candidate.StrategyKey &&
                    item.CreatedAt >=
                        candidate.CreatedAt)
                .ToArray();

        var confidence =
            matchingRecoveries.Length == 0
                ? 0
                : matchingRecoveries
                    .Average(item =>
                        item.Confidence);

        if (conflictingFailures.Length > 0)
        {
            return new(
                ComputerOperatorExperienceValidationStatuses.Rejected,
                false,
                candidateId,
                null,
                matchingRecoveries.Length,
                conflictingFailures.Length,
                confidence,
                "Strategy đã phát sinh failure mới trong cùng context; không promote candidate.");
        }

        if (matchingRecoveries.Length <
            MinimumVerifiedRecoveries)
        {
            return new(
                ComputerOperatorExperienceValidationStatuses.Pending,
                false,
                candidateId,
                null,
                matchingRecoveries.Length,
                0,
                confidence,
                $"Chưa đủ bằng chứng lặp lại: cần ít nhất {MinimumVerifiedRecoveries} recovery VERIFY PASS.");
        }

        if (confidence <
            MinimumAverageRecoveryConfidence)
        {
            return new(
                ComputerOperatorExperienceValidationStatuses.Pending,
                false,
                candidateId,
                null,
                matchingRecoveries.Length,
                0,
                confidence,
                $"Recovery confidence trung bình {confidence:0.00} chưa đạt ngưỡng {MinimumAverageRecoveryConfidence:0.00}.");
        }

        var duplicate =
            recent.FirstOrDefault(item =>
                item.Kind ==
                    ComputerOperatorExperienceKinds.Experience &&
                item.Verified &&
                item.ContextFingerprint ==
                    candidate.ContextFingerprint &&
                item.FailureCode ==
                    candidate.FailureCode &&
                item.StrategyKey ==
                    candidate.StrategyKey);

        if (duplicate is not null)
        {
            return new(
                ComputerOperatorExperienceValidationStatuses.Promoted,
                true,
                candidateId,
                duplicate.Id,
                matchingRecoveries.Length,
                0,
                duplicate.Confidence,
                "Đã có trusted experience tương đương; không tạo bản ghi trùng.");
        }

        var trustedConfidence =
            Math.Clamp(
                confidence *
                Math.Min(
                    1.0,
                    0.75 +
                    (matchingRecoveries.Length * 0.10)),
                0,
                0.98);

        var experience =
            experiences.Append(
                ComputerOperatorExperienceKinds.Experience,
                candidate.ContextFingerprint,
                failureCode: candidate.FailureCode,
                strategyKey: candidate.StrategyKey,
                outcome: "trusted-verified",
                durationMilliseconds:
                    (long)Math.Round(
                        matchingRecoveries.Average(item =>
                            (double)item.DurationMilliseconds)),
                confidence: trustedConfidence,
                verified: true);

        return new(
            ComputerOperatorExperienceValidationStatuses.Promoted,
            true,
            candidateId,
            experience.Id,
            matchingRecoveries.Length,
            0,
            trustedConfidence,
            "Candidate đã có đủ recovery lặp lại được VERIFY PASS và được promote thành trusted experience.");
    }
}
