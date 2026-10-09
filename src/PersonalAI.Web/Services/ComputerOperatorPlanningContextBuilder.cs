namespace PersonalAI.Web.Services;

public interface IComputerOperatorPlanningContextBuilder
{
    string BuildLocalHistory(
        IReadOnlyList<string> history);

    string BuildPlannerHistory(
        IReadOnlyList<string> history,
        ComputerOperatorRecoverySession recovery,
        IReadOnlyCollection<string> verifiedMilestones,
        string currentSubgoal,
        double goalProgress);
}

public sealed class ComputerOperatorPlanningContextBuilder
    : IComputerOperatorPlanningContextBuilder
{
    public string BuildLocalHistory(
        IReadOnlyList<string> history) =>
        string.Join(
            "\n",
            history.TakeLast(40));

    public string BuildPlannerHistory(
        IReadOnlyList<string> history,
        ComputerOperatorRecoverySession recovery,
        IReadOnlyCollection<string> verifiedMilestones,
        string currentSubgoal,
        double goalProgress)
    {
        var historyText = history.Count == 0
            ? "(chưa có hành động trước đó)"
            : string.Join(
                "\n",
                history.TakeLast(14));

        var milestonesText = verifiedMilestones.Count == 0
            ? "(chưa có mốc đã xác minh)"
            : string.Join(
                "\n",
                verifiedMilestones
                    .TakeLast(10)
                    .Select(item => $"- {item}"));

        return
            historyText +
            "\n\nMỤC TIÊU CON TRƯỚC ĐÓ: " +
            (string.IsNullOrWhiteSpace(currentSubgoal)
                ? "(chưa xác định)"
                : currentSubgoal) +
            $"\nTIẾN ĐỘ BÁO CÁO TRƯỚC ĐÓ: {goalProgress * 100:0}%\n" +
            "CÁC MỐC ĐÃ XÁC MINH:\n" +
            milestonesText +
            "\n\n" +
            recovery.BuildContext();
    }
}
