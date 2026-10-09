namespace PersonalAI.Web.Services;

public static class ComputerOperatorRuntimeStates
{
    public const string Starting = "starting";
    public const string Running = "running";
    public const string Progressing = "progressing";
    public const string WaitingExpected = "waiting-expected";
    public const string WaitingExternal = "waiting-external";
    public const string Blocked = "blocked";
    public const string PossiblyStalled = "possibly-stalled";
    public const string Hung = "hung";
    public const string Failed = "failed";
    public const string Completed = "completed";
}

public static class ComputerOperatorRuntimeDecisions
{
    public const string Wait = "wait";
    public const string Act = "act";
    public const string Replan = "replan";
    public const string Recover = "recover";
    public const string Abort = "abort";
    public const string Complete = "complete";
}

public sealed record ComputerOperatorRuntimeStatePolicy(
    double MinimumStrongEvidenceConfidence,
    double MinimumProgressConfidence,
    double MinimumResourceActivityScore,
    TimeSpan StartingGracePeriod,
    TimeSpan StallSuspicionAfter,
    TimeSpan HungSuspicionAfter)
{
    public static ComputerOperatorRuntimeStatePolicy Default { get; } =
        new(
            MinimumStrongEvidenceConfidence: 0.70,
            MinimumProgressConfidence: 0.65,
            MinimumResourceActivityScore: 0.20,
            StartingGracePeriod: TimeSpan.FromSeconds(3),
            StallSuspicionAfter: TimeSpan.FromSeconds(12),
            HungSuspicionAfter: TimeSpan.FromSeconds(30));
}

public sealed record ComputerOperatorRuntimeEvidence(
    bool ExpectedEffectObserved,
    double ExpectedEffectConfidence,
    bool ExplicitFailureObserved,
    double FailureConfidence,
    bool BlockingConditionObserved,
    double BlockingConfidence,
    bool MeaningfulProgressObserved,
    double ProgressConfidence,
    bool ExternalWaitObserved,
    double ExternalWaitConfidence,
    bool ProcessAlive,
    bool? ProcessResponding,
    double ResourceActivityScore,
    TimeSpan Elapsed,
    TimeSpan TimeSinceMeaningfulProgress);

public sealed record ComputerOperatorRuntimeStateAssessment(
    string State,
    string RecommendedDecision,
    double Confidence,
    bool Terminal,
    string Reason);

public interface IComputerOperatorRuntimeStateIntelligence
{
    ComputerOperatorRuntimeStateAssessment Classify(
        ComputerOperatorRuntimeEvidence evidence,
        ComputerOperatorRuntimeStatePolicy? policy = null);
}

/// <summary>
/// Phân loại trạng thái runtime từ bằng chứng tổng hợp.
/// Không biết tên ứng dụng và không dùng timeout cố định để tự kết luận lỗi.
/// </summary>
public sealed class ComputerOperatorRuntimeStateIntelligence
    : IComputerOperatorRuntimeStateIntelligence
{
    public ComputerOperatorRuntimeStateAssessment Classify(
        ComputerOperatorRuntimeEvidence evidence,
        ComputerOperatorRuntimeStatePolicy? policy = null)
    {
        ArgumentNullException.ThrowIfNull(evidence);

        var resolved = policy ??
            ComputerOperatorRuntimeStatePolicy.Default;

        Validate(resolved, evidence);

        if (evidence.ExpectedEffectObserved &&
            evidence.ExpectedEffectConfidence >=
                resolved.MinimumStrongEvidenceConfidence)
        {
            return Assessment(
                ComputerOperatorRuntimeStates.Completed,
                ComputerOperatorRuntimeDecisions.Complete,
                evidence.ExpectedEffectConfidence,
                terminal: true,
                "Đã có bằng chứng đủ mạnh rằng kết quả mong đợi đã xuất hiện.");
        }

        if (evidence.ExplicitFailureObserved &&
            evidence.FailureConfidence >=
                resolved.MinimumStrongEvidenceConfidence)
        {
            return Assessment(
                ComputerOperatorRuntimeStates.Failed,
                ComputerOperatorRuntimeDecisions.Recover,
                evidence.FailureConfidence,
                terminal: true,
                "Đã có bằng chứng lỗi rõ ràng; không suy ra lỗi chỉ từ việc chờ lâu.");
        }

        if (evidence.BlockingConditionObserved &&
            evidence.BlockingConfidence >=
                resolved.MinimumStrongEvidenceConfidence)
        {
            return Assessment(
                ComputerOperatorRuntimeStates.Blocked,
                ComputerOperatorRuntimeDecisions.Act,
                evidence.BlockingConfidence,
                terminal: false,
                "Phát hiện điều kiện đang chặn tiến trình và cần một hành động xử lý.");
        }

        if (evidence.MeaningfulProgressObserved &&
            evidence.ProgressConfidence >=
                resolved.MinimumProgressConfidence)
        {
            return Assessment(
                ComputerOperatorRuntimeStates.Progressing,
                ComputerOperatorRuntimeDecisions.Wait,
                evidence.ProgressConfidence,
                terminal: false,
                "Đang có tiến triển có ý nghĩa; tiếp tục chờ thay vì replan sớm.");
        }

        if (evidence.ExternalWaitObserved &&
            evidence.ExternalWaitConfidence >=
                resolved.MinimumStrongEvidenceConfidence)
        {
            return Assessment(
                ComputerOperatorRuntimeStates.WaitingExternal,
                ComputerOperatorRuntimeDecisions.Wait,
                evidence.ExternalWaitConfidence,
                terminal: false,
                "Đang chờ dịch vụ hoặc điều kiện bên ngoài đã được nhận diện.");
        }

        var resourceActivity =
            Math.Clamp(
                evidence.ResourceActivityScore,
                0,
                1);

        var hasActivity =
            resourceActivity >=
                resolved.MinimumResourceActivityScore;

        if (evidence.ProcessAlive &&
            evidence.ProcessResponding == false &&
            !hasActivity &&
            evidence.TimeSinceMeaningfulProgress >=
                resolved.HungSuspicionAfter)
        {
            return Assessment(
                ComputerOperatorRuntimeStates.Hung,
                ComputerOperatorRuntimeDecisions.Recover,
                0.90,
                terminal: false,
                "Process còn tồn tại nhưng không phản hồi, không có hoạt động đáng kể và không tiến triển đủ lâu.");
        }

        if (evidence.TimeSinceMeaningfulProgress >=
                resolved.StallSuspicionAfter &&
            !hasActivity)
        {
            return Assessment(
                ComputerOperatorRuntimeStates.PossiblyStalled,
                ComputerOperatorRuntimeDecisions.Replan,
                0.72,
                terminal: false,
                "Chưa có bằng chứng lỗi, nhưng thiếu cả tiến triển lẫn hoạt động runtime; cần chẩn đoán/replan.");
        }

        if (evidence.Elapsed <=
            resolved.StartingGracePeriod)
        {
            return Assessment(
                ComputerOperatorRuntimeStates.Starting,
                ComputerOperatorRuntimeDecisions.Wait,
                0.75,
                terminal: false,
                "Tác vụ mới bắt đầu và chưa có đủ bằng chứng để kết luận.");
        }

        if (evidence.ProcessAlive || hasActivity)
        {
            return Assessment(
                ComputerOperatorRuntimeStates.WaitingExpected,
                ComputerOperatorRuntimeDecisions.Wait,
                Math.Max(0.60, resourceActivity),
                terminal: false,
                "Chưa đạt kết quả mong đợi nhưng runtime vẫn còn dấu hiệu sống; tiếp tục quan sát.");
        }

        return Assessment(
            ComputerOperatorRuntimeStates.Running,
            ComputerOperatorRuntimeDecisions.Wait,
            0.50,
            terminal: false,
            "Chưa đủ bằng chứng để kết luận hoàn thành, lỗi, treo hoặc bị chặn.");
    }

    private static ComputerOperatorRuntimeStateAssessment Assessment(
        string state,
        string decision,
        double confidence,
        bool terminal,
        string reason) =>
        new(
            state,
            decision,
            Math.Clamp(confidence, 0, 1),
            terminal,
            reason);

    private static void Validate(
        ComputerOperatorRuntimeStatePolicy policy,
        ComputerOperatorRuntimeEvidence evidence)
    {
        if (policy.MinimumStrongEvidenceConfidence is < 0 or > 1 ||
            policy.MinimumProgressConfidence is < 0 or > 1 ||
            policy.MinimumResourceActivityScore is < 0 or > 1 ||
            policy.StartingGracePeriod < TimeSpan.Zero ||
            policy.StallSuspicionAfter <= TimeSpan.Zero ||
            policy.HungSuspicionAfter <
                policy.StallSuspicionAfter)
        {
            throw new ArgumentOutOfRangeException(
                nameof(policy),
                "Cấu hình Runtime State Intelligence không hợp lệ.");
        }

        if (evidence.ExpectedEffectConfidence is < 0 or > 1 ||
            evidence.FailureConfidence is < 0 or > 1 ||
            evidence.BlockingConfidence is < 0 or > 1 ||
            evidence.ProgressConfidence is < 0 or > 1 ||
            evidence.ExternalWaitConfidence is < 0 or > 1 ||
            evidence.ResourceActivityScore is < 0 or > 1 ||
            evidence.Elapsed < TimeSpan.Zero ||
            evidence.TimeSinceMeaningfulProgress <
                TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(evidence),
                "Bằng chứng Runtime State Intelligence không hợp lệ.");
        }
    }
}
