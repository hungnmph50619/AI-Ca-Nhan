using System.Text;

namespace PersonalAI.Web.Services;

public sealed class ComputerOperatorCycleTrace(
    int cycle,
    string goal)
{
    public int Cycle { get; } =
        cycle;

    public string Goal { get; } =
        goal;

    public string SceneId { get; set; } =
        "-";

    public string CurrentSubgoal { get; set; } =
        "-";

    public string BeforeForeground { get; set; } =
        "-";

    public string AfterForeground { get; set; } =
        "-";

    public string WindowDelta { get; set; } =
        "-";

    public string PlannerRoute { get; set; } =
        "-";

    public List<string> PlannerTrace { get; } =
        [];

    public string Action { get; set; } =
        "-";

    public string Target { get; set; } =
        "-";

    public string TargetElementId { get; set; } =
        "-";

    public string TargetBox { get; set; } =
        "-";

    public double DecisionConfidence { get; set; }

    public string ExpectedEffect { get; set; } =
        "-";

    public string DecisionReason { get; set; } =
        "-";

    public bool? Executed { get; set; }

    public long ExecutionMilliseconds { get; set; } =
        -1;

    public string Executor { get; set; } =
        "-";

    public List<string> Evidence { get; } =
        [];

    public string Verification { get; set; } =
        "-";

    public double VerificationConfidence { get; set; }

    public string RecoveryCode { get; set; } =
        "none";

    public string RecoveryDetail { get; set; } =
        "-";

    public string Result { get; set; } =
        "-";

    public string Next { get; set; } =
        "-";

    public string RenderSummary()
    {
        var builder =
            new StringBuilder();

        builder.AppendLine(
            $"[CYCLE {Cycle} SUMMARY]");
        builder.AppendLine(
            $"Goal: {Limit(Goal, 180)}");
        builder.AppendLine(
            $"Subgoal: {Limit(CurrentSubgoal, 160)}");
        builder.AppendLine(
            $"SceneId: {SceneId}");
        builder.AppendLine(
            $"BeforeForeground: {Limit(BeforeForeground, 160)}");
        builder.AppendLine(
            $"AfterForeground: {Limit(AfterForeground, 160)}");
        builder.AppendLine(
            $"WindowDelta: {Limit(WindowDelta, 220)}");
        builder.AppendLine(
            $"PlannerRoute: {PlannerRoute}");

        if (PlannerTrace.Count > 0)
        {
            builder.AppendLine(
                $"PlannerTrace: {string.Join(" -> ", PlannerTrace.Select(item => Limit(item, 120)))}");
        }

        builder.AppendLine(
            $"Decision: action={Action}; target={Limit(Target, 100)}; element={Limit(TargetElementId, 80)}; bbox={TargetBox}; confidence={DecisionConfidence:0.000}");
        builder.AppendLine(
            $"ExpectedEffect: {Limit(ExpectedEffect, 220)}");
        builder.AppendLine(
            $"DecisionReason: {Limit(DecisionReason, 240)}");
        builder.AppendLine(
            $"Execution: applied={(Executed.HasValue ? Executed.Value.ToString() : "?")}; executor={Executor}; durationMs={ExecutionMilliseconds}");

        if (Evidence.Count > 0)
        {
            builder.AppendLine(
                $"Evidence: {string.Join(" | ", Evidence.Select(item => Limit(item, 180)))}");
        }

        builder.AppendLine(
            $"Verification: {Limit(Verification, 260)}; confidence={VerificationConfidence:0.000}");
        builder.AppendLine(
            $"Recovery: code={RecoveryCode}; detail={Limit(RecoveryDetail, 220)}");
        builder.AppendLine(
            $"Result: {Result}");
        builder.Append(
            $"Next: {Next}");

        return builder.ToString();
    }

    public static string DescribeWindow(
        ComputerWindowInfo? window) =>
        window is null
            ? "none"
            : $"{window.ProcessName ?? "?"}/{window.Title} id={window.WindowId} rect={window.Left},{window.Top},{window.Width},{window.Height}";

    public static string DescribeWindowDelta(
        IReadOnlyList<ComputerWindowInfo> before,
        IReadOnlyList<ComputerWindowInfo> after)
    {
        var beforeMap =
            before.ToDictionary(
                item => item.WindowId,
                StringComparer.OrdinalIgnoreCase);

        var afterMap =
            after.ToDictionary(
                item => item.WindowId,
                StringComparer.OrdinalIgnoreCase);

        var appeared =
            afterMap.Keys
                .Where(key =>
                    !beforeMap.ContainsKey(key))
                .Select(key =>
                    afterMap[key].Title)
                .Take(5)
                .ToArray();

        var disappeared =
            beforeMap.Keys
                .Where(key =>
                    !afterMap.ContainsKey(key))
                .Select(key =>
                    beforeMap[key].Title)
                .Take(5)
                .ToArray();

        var moved =
            afterMap.Keys
                .Where(beforeMap.ContainsKey)
                .Where(key =>
                {
                    var oldWindow =
                        beforeMap[key];
                    var newWindow =
                        afterMap[key];

                    return
                        oldWindow.Left != newWindow.Left ||
                        oldWindow.Top != newWindow.Top ||
                        oldWindow.Width != newWindow.Width ||
                        oldWindow.Height != newWindow.Height;
                })
                .Select(key =>
                    afterMap[key].Title)
                .Take(5)
                .ToArray();

        return
            $"appeared=[{string.Join(",", appeared)}]; disappeared=[{string.Join(",", disappeared)}]; moved=[{string.Join(",", moved)}]; count={before.Count}->{after.Count}";
    }

    private static string Limit(
        string? value,
        int maximum)
    {
        if (string.IsNullOrWhiteSpace(
                value))
        {
            return "-";
        }

        var compact =
            value
                .Replace(
                    "\r",
                    " ",
                    StringComparison.Ordinal)
                .Replace(
                    "\n",
                    " ",
                    StringComparison.Ordinal)
                .Trim();

        return compact.Length <=
               maximum
            ? compact
            : compact[..maximum] +
              "…";
    }
}
