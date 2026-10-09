namespace PersonalAI.Web.Services;

public static class PlanningAuthorityKinds
{
    public const string SingleAction = "single-action";
    public const string MultiStepTask = "multi-step-task";
    public const string Ambiguous = "ambiguous";
}

public sealed record PlanningAuthorityDecision(
    string Authority,
    bool AllowToolOrchestration,
    bool PreferTaskEngine,
    int SequentialSignalCount,
    string Reason);

public interface IPlanningAuthorityPolicy
{
    PlanningAuthorityDecision Classify(string intent);
}

/// <summary>
/// Ranh giới trách nhiệm giữa Tool Orchestration và Task Engine.
/// Chỉ dùng tín hiệu cấu trúc của yêu cầu, tuyệt đối không hard-code app cụ thể.
/// </summary>
public sealed class PlanningAuthorityPolicy : IPlanningAuthorityPolicy
{
    private static readonly string[] SequentialSignals =
    [
        " sau đó ",
        " rồi ",
        " tiếp theo ",
        " kế tiếp ",
        " sau khi ",
        " and then ",
        " then ",
        " next ",
        " after that ",
        " finally ",
        " cuối cùng "
    ];

    public PlanningAuthorityDecision Classify(string intent)
    {
        var normalized =
            $" {(intent ?? string.Empty).Trim().ToLowerInvariant()} ";

        if (normalized.Trim().Length == 0)
        {
            return new(
                PlanningAuthorityKinds.Ambiguous,
                AllowToolOrchestration: true,
                PreferTaskEngine: false,
                SequentialSignalCount: 0,
                "Intent rỗng/không rõ; không tự chuyển sang multi-step.");
        }

        var sequentialCount =
            SequentialSignals.Count(signal =>
                normalized.Contains(
                    signal,
                    StringComparison.OrdinalIgnoreCase));

        var numberedSteps =
            CountNumberedStepSignals(
                normalized);

        var effectiveSignals =
            sequentialCount + numberedSteps;

        if (effectiveSignals >= 2 ||
            numberedSteps >= 2)
        {
            return new(
                PlanningAuthorityKinds.MultiStepTask,
                AllowToolOrchestration: false,
                PreferTaskEngine: true,
                effectiveSignals,
                "Yêu cầu có nhiều tín hiệu tuần tự/step; Tool Orchestration không được tự biến thành planner nhiều bước.");
        }

        if (effectiveSignals == 1)
        {
            return new(
                PlanningAuthorityKinds.Ambiguous,
                AllowToolOrchestration: true,
                PreferTaskEngine: false,
                effectiveSignals,
                "Có một tín hiệu tuần tự nhưng chưa đủ để kết luận đây là task nhiều bước; cho phép single-tool proposal nếu capability phù hợp.");
        }

        return new(
            PlanningAuthorityKinds.SingleAction,
            AllowToolOrchestration: true,
            PreferTaskEngine: false,
            0,
            "Không có tín hiệu multi-step mạnh; Tool Orchestration giữ vai trò đề xuất đúng một tool.");
    }

    private static int CountNumberedStepSignals(
        string value)
    {
        var count = 0;

        for (var number = 1;
             number <= 8;
             number++)
        {
            if (value.Contains(
                    $" {number}. ",
                    StringComparison.Ordinal) ||
                value.Contains(
                    $" bước {number} ",
                    StringComparison.OrdinalIgnoreCase) ||
                value.Contains(
                    $" step {number} ",
                    StringComparison.OrdinalIgnoreCase))
            {
                count++;
            }
        }

        return count;
    }
}
