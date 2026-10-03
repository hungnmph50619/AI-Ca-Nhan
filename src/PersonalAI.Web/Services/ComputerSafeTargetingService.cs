using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public interface IComputerSafeTargetingService
{
    ComputerSafeTargetPoint Resolve(
        DesktopOperatorDecision decision,
        DesktopScreenshotFrame frame);
}

public sealed class ComputerSafeTargetingService(
    IComputerCoordinateTransformService coordinates)
    : IComputerSafeTargetingService
{
    public ComputerSafeTargetPoint Resolve(
        DesktopOperatorDecision decision,
        DesktopScreenshotFrame frame)
    {
        var space = string.IsNullOrWhiteSpace(decision.CoordinateSpace)
            ? ComputerCoordinateSpaces.ImagePixel
            : decision.CoordinateSpace.Trim().ToLowerInvariant();

        var hasBox = HasValidBox(
            decision,
            frame,
            space);

        if (!hasBox)
            throw new ToolExecutionInputException(
                "Hành động click cần bounding box hợp lệ; hệ thống từ chối click theo một điểm đơn lẻ.");

        var safeRequest = BuildSafeCenterRequest(
            decision,
            space);

        var safePoint = coordinates.ToDesktopPoint(
            safeRequest,
            frame);

        return new(
            safePoint,
            true,
            BuildBoxDetail(decision, space));
    }

    private static bool HasValidBox(
        DesktopOperatorDecision decision,
        DesktopScreenshotFrame frame,
        string space)
    {
        if (space == ComputerCoordinateSpaces.ImagePixel)
        {
            return decision.BoxLeft >= 0 &&
                   decision.BoxTop >= 0 &&
                   decision.BoxWidth > 1 &&
                   decision.BoxHeight > 1 &&
                   decision.BoxLeft + decision.BoxWidth <= frame.Width &&
                   decision.BoxTop + decision.BoxHeight <= frame.Height;
        }

        return double.IsFinite(decision.BoxNormalizedLeft) &&
               double.IsFinite(decision.BoxNormalizedTop) &&
               double.IsFinite(decision.BoxNormalizedWidth) &&
               double.IsFinite(decision.BoxNormalizedHeight) &&
               decision.BoxNormalizedLeft >= 0 &&
               decision.BoxNormalizedTop >= 0 &&
               decision.BoxNormalizedWidth > 0 &&
               decision.BoxNormalizedHeight > 0 &&
               decision.BoxNormalizedLeft + decision.BoxNormalizedWidth <= 1.000001 &&
               decision.BoxNormalizedTop + decision.BoxNormalizedHeight <= 1.000001;
    }

    private static ComputerCoordinateRequest BuildSafeCenterRequest(
        DesktopOperatorDecision decision,
        string space)
    {
        if (space == ComputerCoordinateSpaces.ImagePixel)
        {
            var safeInsetX = Math.Min(
                14,
                Math.Max(
                    1,
                    (int)Math.Round(
                        decision.BoxWidth * 0.18,
                        MidpointRounding.AwayFromZero)));
            var safeInsetY = Math.Min(
                14,
                Math.Max(
                    1,
                    (int)Math.Round(
                        decision.BoxHeight * 0.18,
                        MidpointRounding.AwayFromZero)));

            var safeLeft = checked(decision.BoxLeft + safeInsetX);
            var safeTop = checked(decision.BoxTop + safeInsetY);
            var safeWidth = Math.Max(
                1,
                decision.BoxWidth - safeInsetX * 2);
            var safeHeight = Math.Max(
                1,
                decision.BoxHeight - safeInsetY * 2);

            var x = checked(safeLeft + safeWidth / 2);
            var y = checked(safeTop + safeHeight / 2);

            return new(
                space,
                x,
                y,
                0,
                0,
                decision.CoordinateWindowId);
        }

        var insetX = decision.BoxNormalizedWidth * 0.18;
        var insetY = decision.BoxNormalizedHeight * 0.18;

        var safeLeftNormalized =
            decision.BoxNormalizedLeft + insetX;
        var safeTopNormalized =
            decision.BoxNormalizedTop + insetY;
        var safeWidthNormalized =
            Math.Max(
                0.000001,
                decision.BoxNormalizedWidth - insetX * 2);
        var safeHeightNormalized =
            Math.Max(
                0.000001,
                decision.BoxNormalizedHeight - insetY * 2);

        var normalizedX =
            safeLeftNormalized + safeWidthNormalized / 2;
        var normalizedY =
            safeTopNormalized + safeHeightNormalized / 2;

        return new(
            space,
            0,
            0,
            normalizedX,
            normalizedY,
            decision.CoordinateWindowId);
    }

    private static string BuildBoxDetail(
        DesktopOperatorDecision decision,
        string space)
    {
        if (space == ComputerCoordinateSpaces.ImagePixel)
        {
            return
                $"Đã dùng vùng mục tiêu [{decision.BoxLeft},{decision.BoxTop},{decision.BoxWidth},{decision.BoxHeight}] và chọn điểm an toàn gần tâm.";
        }

        return
            $"Đã dùng vùng mục tiêu chuẩn hóa [{decision.BoxNormalizedLeft:0.0000},{decision.BoxNormalizedTop:0.0000},{decision.BoxNormalizedWidth:0.0000},{decision.BoxNormalizedHeight:0.0000}] và chọn điểm an toàn gần tâm.";
    }
}
