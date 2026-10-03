namespace PersonalAI.Web.Services;

public sealed record ComputerOperatorLoopAssessment(
    bool Detected,
    string Kind,
    string Detail,
    int Occurrences);

public sealed class ComputerOperatorLoopGuardSession
{
    private const int MaximumStates = 16;
    private readonly List<string> states = [];
    private int warningCount;

    public ComputerOperatorLoopAssessment Observe(
        string state)
    {
        var normalized = NormalizeState(state);
        if (normalized.Length == 0)
            return new(false, string.Empty, string.Empty, 0);

        states.Add(normalized);
        if (states.Count > MaximumStates)
            states.RemoveRange(
                0,
                states.Count - MaximumStates);

        var stagnation = DetectStagnation();
        if (stagnation.Detected)
        {
            warningCount++;
            return stagnation with
            {
                Occurrences = warningCount
            };
        }

        var oscillation = DetectOscillation();
        if (oscillation.Detected)
        {
            warningCount++;
            return oscillation with
            {
                Occurrences = warningCount
            };
        }

        warningCount = Math.Max(
            0,
            warningCount - 1);

        return new(
            false,
            string.Empty,
            string.Empty,
            0);
    }

    public void MarkProgress()
    {
        warningCount = 0;

        // Giữ lại trạng thái gần nhất để vẫn có ngữ cảnh nhưng xóa chuỗi cũ,
        // tránh coi tiến triển mới là continuation của vòng lặp cũ.
        if (states.Count > 1)
        {
            var last = states[^1];
            states.Clear();
            states.Add(last);
        }
    }

    private ComputerOperatorLoopAssessment DetectStagnation()
    {
        if (states.Count < 4)
            return new(false, string.Empty, string.Empty, 0);

        var recent = states.TakeLast(4).ToArray();
        var same = recent.Count(item =>
            item.Equals(
                recent[0],
                StringComparison.OrdinalIgnoreCase));

        if (same < 4)
            return new(false, string.Empty, string.Empty, 0);

        return new(
            true,
            "stagnation",
            "Trạng thái desktop gần như không thay đổi qua 4 vòng suy luận liên tiếp.",
            same);
    }

    private ComputerOperatorLoopAssessment DetectOscillation()
    {
        if (states.Count < 4)
            return new(false, string.Empty, string.Empty, 0);

        var recent = states.TakeLast(4).ToArray();
        var a = recent[0];
        var b = recent[1];

        if (a.Equals(
                b,
                StringComparison.OrdinalIgnoreCase))
            return new(false, string.Empty, string.Empty, 0);

        var oscillates =
            recent[2].Equals(
                a,
                StringComparison.OrdinalIgnoreCase) &&
            recent[3].Equals(
                b,
                StringComparison.OrdinalIgnoreCase);

        if (!oscillates)
            return new(false, string.Empty, string.Empty, 0);

        return new(
            true,
            "oscillation",
            "Trạng thái đang dao động qua lại giữa hai tình huống A ↔ B.",
            2);
    }

    private static string NormalizeState(
        string value)
    {
        var normalized = string.Join(
            " ",
            (value ?? string.Empty)
                .Trim()
                .ToLowerInvariant()
                .Split(
                    [' ', '\t', '\r', '\n', '.', ',', ';', ':'],
                    StringSplitOptions.RemoveEmptyEntries));

        return normalized.Length <= 320
            ? normalized
            : normalized[..320];
    }
}
