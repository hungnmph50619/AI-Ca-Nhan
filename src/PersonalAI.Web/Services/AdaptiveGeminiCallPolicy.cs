using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public enum AdaptiveGeminiDecision
{
    CallGemini,
    SkipAndFail,
    SkipAndPass
}

public sealed record AdaptiveGeminiCallDecision(
    AdaptiveGeminiDecision Decision,
    double Confidence,
    string Reason);

public interface IAdaptiveGeminiCallPolicy
{
    AdaptiveGeminiCallDecision EvaluateVerification(
        DesktopOperatorDecision decision,
        DesktopFastObservation observation,
        DesktopFrameDifference? frameDifference);
}

public sealed class AdaptiveGeminiCallPolicy
    : IAdaptiveGeminiCallPolicy
{
    private const double NoVisualChangeRatio = 0.001;
    private const double StrongVisualChangeRatio = 0.02;

    public AdaptiveGeminiCallDecision EvaluateVerification(
        DesktopOperatorDecision decision,
        DesktopFastObservation observation,
        DesktopFrameDifference? frameDifference)
    {
        ArgumentNullException.ThrowIfNull(decision);
        ArgumentNullException.ThrowIfNull(observation);

        var action = (decision.Action ?? string.Empty)
            .Trim()
            .ToLowerInvariant();

        if (action is "press-key" or "press-hotkey")
        {
            var hasIndependentTransitionEvidence =
                observation.ForegroundWindowChanged &&
                frameDifference?.Comparable == true &&
                frameDifference.ChangedRatio >= StrongVisualChangeRatio;

            if (hasIndependentTransitionEvidence)
            {
                return new(
                    AdaptiveGeminiDecision.SkipAndPass,
                    0.96,
                    $"Thao tác bàn phím tạo transition rõ: foreground đổi và frame thay đổi {frameDifference!.ChangedRatio * 100:0.00}%; không cần Gemini xác minh lại side effect.");
            }
        }

        if (observation.TargetLikelyOccluded ||
            observation.TargetMissing ||
            observation.ForegroundWindowChanged ||
            observation.WindowBoundsChanged ||
            observation.DpiChanged ||
            observation.MonitorChanged)
        {
            return new(
                AdaptiveGeminiDecision.CallGemini,
                0,
                "Ngữ cảnh desktop thay đổi đáng kể; cần Gemini đọc lại trạng thái.");
        }

        if (frameDifference?.Comparable != true)
        {
            return new(
                AdaptiveGeminiDecision.CallGemini,
                0,
                "Không có frame-difference tin cậy; cần Gemini xác minh.");
        }

        var ratio = frameDifference.ChangedRatio;

        if (ratio <= NoVisualChangeRatio &&
            ActionShouldNormallyChangeVisibleUi(action))
        {
            return new(
                AdaptiveGeminiDecision.SkipAndFail,
                0.97,
                $"Action {action} không tạo thay đổi hình ảnh đáng kể ({ratio * 100:0.000}%).");
        }

        if (ratio >= StrongVisualChangeRatio &&
            action == "scroll")
        {
            return new(
                AdaptiveGeminiDecision.SkipAndPass,
                0.94,
                $"Scroll tạo thay đổi hình ảnh rõ ({ratio * 100:0.00}%).");
        }

        return new(
            AdaptiveGeminiDecision.CallGemini,
            0,
            "Cần hiểu semantic của trạng thái sau action.");
    }

    private static bool ActionShouldNormallyChangeVisibleUi(
        string action) =>
        action is
            "click-left" or
            "double-click-left" or
            "click-right" or
            "scroll" or
            "drag-left" or
            "type-text" or
            "press-key" or
            "press-hotkey" or
            "open-browser" or
            "focus-window" or
            "minimize" or
            "maximize" or
            "restore";
}
