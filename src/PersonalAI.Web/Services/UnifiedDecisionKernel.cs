using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public interface IComputerOperatorIntentCompiler
{
    bool TryCompile(
        DesktopOperatorIntent intent,
        DesktopScreenshotFrame frame,
        out DesktopOperatorDecision decision,
        out string rejectionReason);
}

/// <summary>
/// Chỉ biên dịch Minimal Intent thành một action candidate có cấu trúc.
/// Không có quyền quyết định EXECUTE/REPLAN; quyền đó thuộc
/// IComputerOperatorDecisionAuthority sau khi loop/recovery signals được đánh giá.
/// </summary>
public sealed class UnifiedDecisionKernel
    : IComputerOperatorIntentCompiler
{
    private static readonly IReadOnlyDictionary<string, string> IntentToAction =
        new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase)
        {
            ["focus_window"] = "focus-window",
            ["click_target"] = "click-left",
            ["double_click_target"] = "double-click-left",
            ["right_click_target"] = "click-right",
            ["type_text"] = "type-text",
            ["press_key"] = "press-key",
            ["press_hotkey"] = "press-hotkey",
            ["scroll"] = "scroll",
            ["wait"] = "wait",
            ["complete"] = "complete",
            ["blocked"] = "blocked"
        };

    public bool TryCompile(
        DesktopOperatorIntent intent,
        DesktopScreenshotFrame frame,
        out DesktopOperatorDecision decision,
        out string rejectionReason)
    {
        ArgumentNullException.ThrowIfNull(intent);
        ArgumentNullException.ThrowIfNull(frame);

        decision = Empty();
        rejectionReason = string.Empty;

        var normalizedIntent =
            (intent.Intent ?? string.Empty)
                .Trim()
                .ToLowerInvariant();

        if (!IntentToAction.TryGetValue(
                normalizedIntent,
                out var action))
        {
            rejectionReason =
                $"Intent '{intent.Intent}' không nằm trong capability map của Unified Decision Kernel.";
            return false;
        }

        if (!double.IsFinite(intent.Confidence) ||
            intent.Confidence is < 0 or > 1)
        {
            rejectionReason =
                "Intent confidence không hợp lệ.";
            return false;
        }

        var target =
            (intent.Target ?? string.Empty)
                .Trim();
        var query =
            (intent.Query ?? string.Empty)
                .Trim();
        var text =
            intent.Text ?? string.Empty;
        var key =
            (intent.Key ?? string.Empty)
                .Trim();
        var keys =
            (intent.Keys ?? Array.Empty<string>())
                .Where(value =>
                    !string.IsNullOrWhiteSpace(value))
                .Select(value =>
                    value.Trim())
                .Take(8)
                .ToArray();
        var expectedEffect =
            (intent.ExpectedEffect ?? string.Empty)
                .Trim();
        var reason =
            (intent.Reason ?? string.Empty)
                .Trim();

        if (reason.Length > 600 ||
            expectedEffect.Length > 600 ||
            target.Length > 160 ||
            query.Length > 200 ||
            text.Length > 1200 ||
            key.Length > 40)
        {
            rejectionReason =
                "Intent chứa field vượt giới hạn an toàn.";
            return false;
        }

        var pointerAction =
            action is
                "click-left" or
                "double-click-left" or
                "click-right" or
                "scroll";

        if (pointerAction)
        {
            if (intent.ImageX < 0 ||
                intent.ImageX >= frame.Width ||
                intent.ImageY < 0 ||
                intent.ImageY >= frame.Height)
            {
                rejectionReason =
                    "Intent pointer trả tọa độ ngoài frame hiện tại.";
                return false;
            }

            if (action is
                    "click-left" or
                    "double-click-left" or
                    "click-right")
            {
                if (intent.BoxWidth <= 3 ||
                    intent.BoxHeight <= 3 ||
                    intent.BoxLeft < 0 ||
                    intent.BoxTop < 0 ||
                    intent.BoxLeft + intent.BoxWidth > frame.Width ||
                    intent.BoxTop + intent.BoxHeight > frame.Height)
                {
                    rejectionReason =
                        "Intent click chưa có bounding box hợp lệ trong frame hiện tại.";
                    return false;
                }

                if (intent.ImageX < intent.BoxLeft ||
                    intent.ImageX >= intent.BoxLeft + intent.BoxWidth ||
                    intent.ImageY < intent.BoxTop ||
                    intent.ImageY >= intent.BoxTop + intent.BoxHeight)
                {
                    rejectionReason =
                        "Điểm click của intent nằm ngoài bounding box target.";
                    return false;
                }
            }
        }

        if (action == "focus-window" &&
            string.IsNullOrWhiteSpace(query) &&
            string.IsNullOrWhiteSpace(target))
        {
            rejectionReason =
                "focus_window thiếu query/target.";
            return false;
        }

        if (action == "type-text" &&
            string.IsNullOrEmpty(text))
        {
            rejectionReason =
                "type_text thiếu nội dung.";
            return false;
        }

        if (action == "press-key" &&
            string.IsNullOrWhiteSpace(key))
        {
            rejectionReason =
                "press_key thiếu key.";
            return false;
        }

        if (action == "press-hotkey" &&
            keys.Length == 0)
        {
            rejectionReason =
                "press_hotkey thiếu keys.";
            return false;
        }

        if (action is not
                "wait" and not
                "complete" and not
                "blocked" &&
            string.IsNullOrWhiteSpace(expectedEffect))
        {
            rejectionReason =
                "Intent side-effect thiếu expectedEffect để verifier kiểm tra.";
            return false;
        }

        decision =
            new DesktopOperatorDecision(
                State:
                    "Unified Decision Kernel đã nhận minimal intent hợp lệ.",
                Plan:
                    "Biên dịch intent tối thiểu thành đúng một action đã kiểm tra.",
                CurrentSubgoal:
                    string.IsNullOrWhiteSpace(target)
                        ? expectedEffect
                        : $"Xử lý mục tiêu '{target}'.",
                GoalProgress:
                    action == "complete"
                        ? 1.0
                        : 0.0,
                VerifiedMilestones:
                    Array.Empty<string>(),
                Action:
                    action,
                Query:
                    action == "focus-window"
                        ? (string.IsNullOrWhiteSpace(query)
                            ? target
                            : query)
                        : query,
                Text:
                    text,
                Key:
                    key,
                Keys:
                    keys,
                Url:
                    string.Empty,
                TargetLabel:
                    target,
                CoordinateSpace:
                    "image-pixel",
                CoordinateWindowId:
                    string.Empty,
                ImageX:
                    pointerAction
                        ? intent.ImageX
                        : 0,
                ImageY:
                    pointerAction
                        ? intent.ImageY
                        : 0,
                EndImageX:
                    0,
                EndImageY:
                    0,
                NormalizedX:
                    0,
                NormalizedY:
                    0,
                EndNormalizedX:
                    0,
                EndNormalizedY:
                    0,
                BoxLeft:
                    action is
                        "click-left" or
                        "double-click-left" or
                        "click-right"
                        ? intent.BoxLeft
                        : 0,
                BoxTop:
                    action is
                        "click-left" or
                        "double-click-left" or
                        "click-right"
                        ? intent.BoxTop
                        : 0,
                BoxWidth:
                    action is
                        "click-left" or
                        "double-click-left" or
                        "click-right"
                        ? intent.BoxWidth
                        : 0,
                BoxHeight:
                    action is
                        "click-left" or
                        "double-click-left" or
                        "click-right"
                        ? intent.BoxHeight
                        : 0,
                BoxNormalizedLeft:
                    0,
                BoxNormalizedTop:
                    0,
                BoxNormalizedWidth:
                    0,
                BoxNormalizedHeight:
                    0,
                ScrollDelta:
                    action == "scroll"
                        ? intent.ScrollDelta
                        : 0,
                ExpectedEffect:
                    expectedEffect,
                Confidence:
                    intent.Confidence,
                Reason:
                    $"Unified Decision Kernel: {reason}",
                SceneElements:
                    Array.Empty<DesktopSceneElement>(),
                TargetElementId:
                    string.Empty);

        return true;
    }

    private static DesktopOperatorDecision Empty() =>
        new(
            State: string.Empty,
            Plan: string.Empty,
            CurrentSubgoal: string.Empty,
            GoalProgress: 0,
            VerifiedMilestones: Array.Empty<string>(),
            Action: "wait",
            Query: string.Empty,
            Text: string.Empty,
            Key: string.Empty,
            Keys: Array.Empty<string>(),
            Url: string.Empty,
            TargetLabel: string.Empty,
            CoordinateSpace: "image-pixel",
            CoordinateWindowId: string.Empty,
            ImageX: 0,
            ImageY: 0,
            EndImageX: 0,
            EndImageY: 0,
            NormalizedX: 0,
            NormalizedY: 0,
            EndNormalizedX: 0,
            EndNormalizedY: 0,
            BoxLeft: 0,
            BoxTop: 0,
            BoxWidth: 0,
            BoxHeight: 0,
            BoxNormalizedLeft: 0,
            BoxNormalizedTop: 0,
            BoxNormalizedWidth: 0,
            BoxNormalizedHeight: 0,
            ScrollDelta: 0,
            ExpectedEffect: string.Empty,
            Confidence: 0,
            Reason: string.Empty,
            SceneElements: Array.Empty<DesktopSceneElement>(),
            TargetElementId: string.Empty);
}
