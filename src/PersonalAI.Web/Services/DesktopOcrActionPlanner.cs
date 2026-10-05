using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public interface IDesktopOcrActionPlanner
{
    bool TryPlan(
        string goal,
        ComputerOperatorDesktopState state,
        out DesktopOperatorDecision decision);
}

public sealed class DesktopOcrActionPlanner(
    IDesktopOcrSensor ocrSensor)
    : IDesktopOcrActionPlanner
{
    private static readonly DesktopOcrTargetResolver Resolver =
        new();

    private static readonly string[] Prefixes =
    [
        "bấm ",
        "bam ",
        "nhấn ",
        "nhan ",
        "click ",
        "press "
    ];

    private static readonly string[] NextStepSeparators =
    [
        ",",
        ";",
        " sau đó ",
        " sau do ",
        " rồi ",
        " roi ",
        " then ",
        " and then "
    ];

    public bool TryPlan(
        string goal,
        ComputerOperatorDesktopState state,
        out DesktopOperatorDecision decision)
    {
        ArgumentNullException.ThrowIfNull(state);

        decision = Empty();

        if (state.ForegroundWindow is null ||
            !TryExtractTarget(
                goal,
                out var requestedText))
        {
            return false;
        }

        var window =
            state.ForegroundWindow;

        var observation =
            ocrSensor.ReadWindow(
                window.WindowId);

        if (!observation.Available ||
            observation.CaptureWidth <= 0 ||
            observation.CaptureHeight <= 0)
        {
            return false;
        }

        var resolution =
            Resolver.Resolve(
                observation,
                requestedText);

        if (!resolution.Resolved ||
            resolution.Target is null)
        {
            return false;
        }

        var target =
            resolution.Target;

        var scaleX =
            window.Width /
            (double)observation.CaptureWidth;
        var scaleY =
            window.Height /
            (double)observation.CaptureHeight;

        var desktopLeft =
            window.Left +
            (int)Math.Round(
                target.Left * scaleX);
        var desktopTop =
            window.Top +
            (int)Math.Round(
                target.Top * scaleY);
        var width =
            Math.Max(
                4,
                (int)Math.Round(
                    target.Width * scaleX));
        var height =
            Math.Max(
                4,
                (int)Math.Round(
                    target.Height * scaleY));

        var frameLeft =
            desktopLeft -
            state.FrameLeft;
        var frameTop =
            desktopTop -
            state.FrameTop;

        if (frameLeft < 0 ||
            frameTop < 0 ||
            frameLeft + width >
                state.FrameWidth ||
            frameTop + height >
                state.FrameHeight)
        {
            return false;
        }

        var confidence =
            target.Score >= 115
                ? 0.93
                : target.Score >= 110
                    ? 0.90
                    : 0.82;

        decision =
            new DesktopOperatorDecision(
                State:
                    $"Windows OCR thấy duy nhất text '{target.Text}' trong foreground window.",
                Plan:
                    $"Click một lần vào bounding box OCR của '{target.Text}', sau đó quan sát lại trước hành động tiếp theo.",
                CurrentSubgoal:
                    $"Tương tác với text '{target.Text}' bằng local OCR fallback.",
                GoalProgress: 0,
                VerifiedMilestones:
                    Array.Empty<string>(),
                Action: "click-left",
                Query: string.Empty,
                Text: string.Empty,
                Key: string.Empty,
                Keys:
                    Array.Empty<string>(),
                Url: string.Empty,
                TargetLabel:
                    target.Text,
                CoordinateSpace:
                    ComputerCoordinateSpaces.ImagePixel,
                CoordinateWindowId:
                    window.WindowId,
                ImageX:
                    frameLeft +
                    width / 2,
                ImageY:
                    frameTop +
                    height / 2,
                EndImageX: 0,
                EndImageY: 0,
                NormalizedX: 0,
                NormalizedY: 0,
                EndNormalizedX: 0,
                EndNormalizedY: 0,
                BoxLeft:
                    frameLeft,
                BoxTop:
                    frameTop,
                BoxWidth:
                    width,
                BoxHeight:
                    height,
                BoxNormalizedLeft: 0,
                BoxNormalizedTop: 0,
                BoxNormalizedWidth: 0,
                BoxNormalizedHeight: 0,
                ScrollDelta: 0,
                ExpectedEffect:
                    $"Phần tử văn bản '{target.Text}' phản hồi sau click OCR-local.",
                Confidence:
                    confidence,
                Reason:
                    $"Structured/local planner không tìm được target; Windows OCR resolver chọn duy nhất '{target.Text}' score={target.Score}, source={target.Source}.",
                SceneElements:
                    Array.Empty<DesktopSceneElement>(),
                TargetElementId:
                    string.Empty);

        return true;
    }

    private static bool TryExtractTarget(
        string goal,
        out string target)
    {
        var value =
            (goal ?? string.Empty)
                .Trim();

        foreach (var prefix in Prefixes)
        {
            if (!value.StartsWith(
                    prefix,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var remainder =
                value[prefix.Length..]
                    .Trim();

            foreach (var qualifier in new[]
            {
                "chữ ",
                "chu ",
                "text ",
                "nút ",
                "nut ",
                "button "
            })
            {
                if (remainder.StartsWith(
                        qualifier,
                        StringComparison.OrdinalIgnoreCase))
                {
                    remainder =
                        remainder[qualifier.Length..]
                            .Trim();
                    break;
                }
            }

            var cut =
                remainder.Length;

            foreach (var separator in NextStepSeparators)
            {
                var index =
                    remainder.IndexOf(
                        separator,
                        StringComparison.OrdinalIgnoreCase);

                if (index >= 0 &&
                    index < cut)
                {
                    cut = index;
                }
            }

            target =
                remainder[..cut]
                    .Trim()
                    .Trim(
                        '"',
                        '\'',
                        '“',
                        '”',
                        '.',
                        ':');

            return target.Length is
                >= 1 and <= 120;
        }

        target =
            string.Empty;
        return false;
    }

    private static DesktopOperatorDecision Empty() =>
        new(
            State: string.Empty,
            Plan: string.Empty,
            CurrentSubgoal: string.Empty,
            GoalProgress: 0,
            VerifiedMilestones:
                Array.Empty<string>(),
            Action: "wait",
            Query: string.Empty,
            Text: string.Empty,
            Key: string.Empty,
            Keys:
                Array.Empty<string>(),
            Url: string.Empty,
            TargetLabel: string.Empty,
            CoordinateSpace:
                ComputerCoordinateSpaces.ImagePixel,
            CoordinateWindowId:
                string.Empty,
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
            SceneElements:
                Array.Empty<DesktopSceneElement>(),
            TargetElementId:
                string.Empty);
}
