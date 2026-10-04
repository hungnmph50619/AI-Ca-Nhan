using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public enum DesktopVerificationRoute
{
    LocalVerified,
    LocalFailed,
    GeminiRequired
}

public sealed record DesktopVerificationRoutingResult(
    DesktopVerificationRoute Route,
    double Confidence,
    string Reason);

public interface IDesktopVerificationRouter
{
    DesktopVerificationRoutingResult Route(
        DesktopOperatorDecision decision,
        DesktopFastObservation observation,
        DesktopFrameDifference? frameDifference);
}

public sealed class DesktopVerificationRouter
    : IDesktopVerificationRouter
{
    private const double StrongVisualChangeRatio = 0.015;
    private const double NoVisualChangeRatio = 0.001;

    public DesktopVerificationRoutingResult Route(
        DesktopOperatorDecision decision,
        DesktopFastObservation observation,
        DesktopFrameDifference? frameDifference)
    {
        ArgumentNullException.ThrowIfNull(decision);
        ArgumentNullException.ThrowIfNull(observation);

        var action = (decision.Action ?? string.Empty)
            .Trim()
            .ToLowerInvariant();

        if (observation.TargetLikelyOccluded ||
            observation.TargetMissing)
        {
            return new(
                DesktopVerificationRoute.GeminiRequired,
                0.0,
                "Target có dấu hiệu biến mất hoặc bị che; cần Vision hiểu lại ngữ cảnh.");
        }

        if (action is "maximize" or "restore" or "minimize")
        {
            if (observation.WindowBoundsChanged ||
                observation.ForegroundWindowChanged)
            {
                return new(
                    DesktopVerificationRoute.LocalVerified,
                    0.98,
                    "Local verifier xác nhận trạng thái cửa sổ đã thay đổi sau thao tác.");
            }

            if (frameDifference?.Comparable == true &&
                frameDifference.ChangedRatio <= NoVisualChangeRatio)
            {
                return new(
                    DesktopVerificationRoute.LocalFailed,
                    0.96,
                    "Không thấy thay đổi hình học cửa sổ hoặc hình ảnh sau thao tác cửa sổ.");
            }
        }

        if (action == "focus-window")
        {
            if (observation.ForegroundWindowChanged)
            {
                return new(
                    DesktopVerificationRoute.LocalVerified,
                    0.97,
                    "Foreground window đã thay đổi sau thao tác focus.");
            }

            return new(
                DesktopVerificationRoute.GeminiRequired,
                0.0,
                "Foreground không đổi; cần Vision xác minh cửa sổ đích có thể đã được focus sẵn.");
        }

        if (action == "scroll")
        {
            if (frameDifference?.Comparable == true &&
                frameDifference.ChangedRatio >= StrongVisualChangeRatio)
            {
                return new(
                    DesktopVerificationRoute.LocalVerified,
                    0.94,
                    $"Frame thay đổi {frameDifference.ChangedRatio * 100:0.00}% sau thao tác cuộn.");
            }

            if (frameDifference?.Comparable == true &&
                frameDifference.ChangedRatio <= NoVisualChangeRatio)
            {
                return new(
                    DesktopVerificationRoute.LocalFailed,
                    0.93,
                    "Không phát hiện thay đổi hình ảnh đáng kể sau thao tác cuộn.");
            }
        }

        return new(
            DesktopVerificationRoute.GeminiRequired,
            0.0,
            "Kết quả cần hiểu semantic; chuyển sang Gemini Vision.");
    }
}
