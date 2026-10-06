namespace PersonalAI.Web.Services;

public sealed record ComputerOperatorLearningCandidateResult(
    bool Created,
    Guid? CandidateId,
    string Reason);

/// <summary>
/// Bộ nhớ học cục bộ theo task.
/// Chỉ tạo candidate sau khi đã có failure thật và một recovery được VERIFY PASS.
/// Candidate chưa phải trusted experience; v4.9.9 mới có quyền validate/promote.
/// </summary>
public sealed class ComputerOperatorLearningSession(
    IComputerOperatorExperienceRepository experiences)
{
    private sealed record PendingFailure(
        string ContextFingerprint,
        string FailureCode,
        string FailedStrategyKey,
        DateTimeOffset RecordedAtUtc);

    private PendingFailure? pending;

    public void RecordFailure(
        string context,
        string failureCode,
        string failedStrategyKey,
        double confidence)
    {
        var fingerprint =
            experiences.FingerprintContext(
                context);

        var normalizedFailure =
            Normalize(
                failureCode,
                80);

        var normalizedStrategy =
            Normalize(
                failedStrategyKey,
                120);

        experiences.Append(
            ComputerOperatorExperienceKinds.Failure,
            fingerprint,
            failureCode: normalizedFailure,
            strategyKey: normalizedStrategy,
            outcome: "observed-failure",
            durationMilliseconds: 0,
            confidence: confidence,
            verified: false);

        pending =
            new(
                fingerprint,
                normalizedFailure,
                normalizedStrategy,
                DateTimeOffset.UtcNow);
    }

    public ComputerOperatorLearningCandidateResult TryRecordVerifiedRecovery(
        string recoveryStrategyKey,
        double verificationConfidence)
    {
        if (pending is null)
        {
            return new(
                false,
                null,
                "Không có failure đang chờ recovery; không tạo learning candidate.");
        }

        var failure =
            pending;

        pending = null;

        var strategy =
            Normalize(
                recoveryStrategyKey,
                120);

        var elapsed =
            Math.Max(
                0,
                (long)(
                    DateTimeOffset.UtcNow -
                    failure.RecordedAtUtc)
                .TotalMilliseconds);

        experiences.Append(
            ComputerOperatorExperienceKinds.Recovery,
            failure.ContextFingerprint,
            failureCode: failure.FailureCode,
            strategyKey: strategy,
            outcome: "verified-pass",
            durationMilliseconds: elapsed,
            confidence: verificationConfidence,
            verified: true);

        var candidate =
            experiences.Append(
                ComputerOperatorExperienceKinds.Candidate,
                failure.ContextFingerprint,
                failureCode: failure.FailureCode,
                strategyKey: strategy,
                outcome: "awaiting-validation",
                durationMilliseconds: elapsed,
                confidence: Math.Clamp(
                    verificationConfidence * 0.60,
                    0,
                    0.60),
                verified: false);

        return new(
            true,
            candidate.Id,
            $"Đã tạo experience candidate từ failure '{failure.FailureCode}' sau recovery VERIFY PASS; candidate chưa trusted.");
    }

    private static string Normalize(
        string? value,
        int maximum)
    {
        var normalized =
            (value ?? string.Empty)
                .Trim()
                .ToLowerInvariant();

        if (normalized.Length > maximum)
        {
            normalized =
                normalized[..maximum];
        }

        return normalized;
    }
}
