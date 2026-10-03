using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public sealed record DesktopTemporalSceneMatch(
    string PreviousId,
    string CurrentId,
    string Role,
    string Label,
    double IntersectionOverUnion,
    int DeltaX,
    int DeltaY,
    bool Moved);

public sealed record DesktopTemporalSceneAnalysis(
    IReadOnlyList<DesktopTemporalSceneMatch> Matches,
    IReadOnlyList<string> AppearedIds,
    IReadOnlyList<string> DisappearedIds,
    int StableCount,
    int MovedCount)
{
    public string ToPromptSummary()
    {
        var matches = Matches
            .Take(12)
            .Select(item =>
                $"{item.PreviousId}->{item.CurrentId}:{item.Role}:{item.Label}:iou={item.IntersectionOverUnion:0.00}:dx={item.DeltaX}:dy={item.DeltaY}:moved={item.Moved}");
        var appeared = AppearedIds.Take(10);
        var disappeared = DisappearedIds.Take(10);

        return
            $"stable={StableCount}; moved={MovedCount}; " +
            $"matches=[{string.Join("; ", matches)}]; " +
            $"appeared=[{string.Join(",", appeared)}]; " +
            $"disappeared=[{string.Join(",", disappeared)}]";
    }
}

public interface IDesktopTemporalSceneService
{
    DesktopTemporalSceneAnalysis Analyze(
        IReadOnlyList<DesktopSceneElement> previous,
        IReadOnlyList<DesktopSceneElement> current);
}

public sealed class DesktopTemporalSceneService
    : IDesktopTemporalSceneService
{
    private const double MinimumIoU = 0.20;
    private const int MovementThresholdPixels = 12;

    public DesktopTemporalSceneAnalysis Analyze(
        IReadOnlyList<DesktopSceneElement> previous,
        IReadOnlyList<DesktopSceneElement> current)
    {
        previous ??= Array.Empty<DesktopSceneElement>();
        current ??= Array.Empty<DesktopSceneElement>();

        var remaining = new HashSet<string>(
            current.Select(item => item.Id),
            StringComparer.OrdinalIgnoreCase);
        var matches = new List<DesktopTemporalSceneMatch>();

        foreach (var oldItem in previous)
        {
            var candidate = current
                .Where(item => remaining.Contains(item.Id))
                .Where(item =>
                    item.Role.Equals(oldItem.Role, StringComparison.OrdinalIgnoreCase) &&
                    LabelsCompatible(oldItem.Label, item.Label))
                .Select(item => new
                {
                    Item = item,
                    Iou = ComputeIoU(oldItem, item)
                })
                .Where(item => item.Iou >= MinimumIoU)
                .OrderByDescending(item => item.Iou)
                .FirstOrDefault();

            if (candidate is null)
                continue;

            remaining.Remove(candidate.Item.Id);

            var oldCenterX = oldItem.BoxLeft + oldItem.BoxWidth / 2;
            var oldCenterY = oldItem.BoxTop + oldItem.BoxHeight / 2;
            var newCenterX = candidate.Item.BoxLeft + candidate.Item.BoxWidth / 2;
            var newCenterY = candidate.Item.BoxTop + candidate.Item.BoxHeight / 2;
            var deltaX = newCenterX - oldCenterX;
            var deltaY = newCenterY - oldCenterY;
            var moved =
                Math.Abs(deltaX) > MovementThresholdPixels ||
                Math.Abs(deltaY) > MovementThresholdPixels;

            matches.Add(new(
                oldItem.Id,
                candidate.Item.Id,
                candidate.Item.Role,
                candidate.Item.Label,
                candidate.Iou,
                deltaX,
                deltaY,
                moved));
        }

        var matchedPrevious = new HashSet<string>(
            matches.Select(item => item.PreviousId),
            StringComparer.OrdinalIgnoreCase);

        return new(
            matches,
            remaining.ToArray(),
            previous
                .Where(item => !matchedPrevious.Contains(item.Id))
                .Select(item => item.Id)
                .ToArray(),
            matches.Count(item => !item.Moved),
            matches.Count(item => item.Moved));
    }

    private static bool LabelsCompatible(string left, string right)
    {
        left = Normalize(left);
        right = Normalize(right);

        if (left.Length == 0 || right.Length == 0)
            return true;

        return left.Equals(right, StringComparison.OrdinalIgnoreCase) ||
               left.Contains(right, StringComparison.OrdinalIgnoreCase) ||
               right.Contains(left, StringComparison.OrdinalIgnoreCase);
    }

    private static string Normalize(string value) =>
        (value ?? string.Empty).Trim();

    private static double ComputeIoU(
        DesktopSceneElement left,
        DesktopSceneElement right)
    {
        var intersectionLeft = Math.Max(left.BoxLeft, right.BoxLeft);
        var intersectionTop = Math.Max(left.BoxTop, right.BoxTop);
        var intersectionRight = Math.Min(
            left.BoxLeft + left.BoxWidth,
            right.BoxLeft + right.BoxWidth);
        var intersectionBottom = Math.Min(
            left.BoxTop + left.BoxHeight,
            right.BoxTop + right.BoxHeight);

        var intersectionWidth = Math.Max(0, intersectionRight - intersectionLeft);
        var intersectionHeight = Math.Max(0, intersectionBottom - intersectionTop);
        var intersectionArea = intersectionWidth * intersectionHeight;
        if (intersectionArea <= 0)
            return 0;

        var leftArea = Math.Max(0, left.BoxWidth) * Math.Max(0, left.BoxHeight);
        var rightArea = Math.Max(0, right.BoxWidth) * Math.Max(0, right.BoxHeight);
        var union = leftArea + rightArea - intersectionArea;

        return union <= 0 ? 0 : (double)intersectionArea / union;
    }
}
