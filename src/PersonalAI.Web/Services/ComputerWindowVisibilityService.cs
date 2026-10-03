using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public sealed record ComputerWindowVisibilityAssessment(
    double VisibleRatio,
    int VisibleSamples,
    int TotalSamples,
    bool LikelyOccluded,
    string Reason);

public interface IComputerWindowVisibilityService
{
    ComputerWindowVisibilityAssessment Assess(
        ComputerWindowInfo window);
}

public sealed class ComputerWindowVisibilityService(
    IComputerUseService computer)
    : IComputerWindowVisibilityService
{
    private const int GridSize = 3;
    private const double OcclusionThreshold = 0.75;

    public ComputerWindowVisibilityAssessment Assess(
        ComputerWindowInfo window)
    {
        ArgumentNullException.ThrowIfNull(window);

        if (window.Width <= 0 || window.Height <= 0)
            return new(0, 0, 0, true, "Cửa sổ không có vùng hiển thị hợp lệ.");

        var visible = 0;
        var total = 0;

        for (var row = 0; row < GridSize; row++)
        {
            for (var column = 0; column < GridSize; column++)
            {
                var x = SampleCoordinate(window.Left, window.Width, column);
                var y = SampleCoordinate(window.Top, window.Height, row);
                var topmost = computer.GetWindowAtPoint(x, y);

                total++;
                if (topmost is not null &&
                    topmost.WindowId.Equals(
                        window.WindowId,
                        StringComparison.OrdinalIgnoreCase))
                {
                    visible++;
                }
            }
        }

        var ratio = total == 0 ? 0 : (double)visible / total;
        var occluded = ratio < OcclusionThreshold;

        return new(
            ratio,
            visible,
            total,
            occluded,
            occluded
                ? $"Chỉ {visible}/{total} điểm mẫu thuộc cửa sổ mục tiêu; có khả năng bị che."
                : $"{visible}/{total} điểm mẫu thuộc cửa sổ mục tiêu; vùng hiển thị đủ rõ.");
    }

    private static int SampleCoordinate(
        int origin,
        int length,
        int index)
    {
        var numerator = (index * 2) + 1;
        var denominator = GridSize * 2;
        return origin + Math.Clamp(
            (length * numerator) / denominator,
            0,
            Math.Max(0, length - 1));
    }
}
