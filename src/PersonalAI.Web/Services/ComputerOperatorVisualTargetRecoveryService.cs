using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public sealed record ComputerOperatorVisualTargetRecoveryResult(
    bool Attempted,
    bool Recovered,
    string Status,
    double Confidence,
    string Reason,
    DesktopTargetTrackingResult? Tracking,
    DesktopScreenshotFrame? ReplacementFrame);

public interface IComputerOperatorVisualTargetRecoveryService
{
    Task<ComputerOperatorVisualTargetRecoveryResult> TryRecoverAsync(
        DesktopOperatorDecision decision,
        ComputerWindowInfo? plannedWindow,
        ComputerWindowInfo? currentWindow,
        DesktopVisualTargetTemplate? template,
        CancellationToken cancellationToken);
}

/// <summary>
/// OpenCV visual reacquire fallback cho click target khi geometry tracking không còn đủ.
/// Chỉ trả recovered candidate + geometry frame mới; không EXECUTE và không tự REPLAN.
/// </summary>
public sealed class ComputerOperatorVisualTargetRecoveryService(
    IDesktopScreenshotService screenshots,
    IDesktopVisualTargetPersistenceService visualTargetPersistence)
    : IComputerOperatorVisualTargetRecoveryService
{
    private const double MinimumRelocationConfidence = 0.90;

    public async Task<ComputerOperatorVisualTargetRecoveryResult> TryRecoverAsync(
        DesktopOperatorDecision decision,
        ComputerWindowInfo? plannedWindow,
        ComputerWindowInfo? currentWindow,
        DesktopVisualTargetTemplate? template,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(
            decision);

        var sameWindow =
            plannedWindow is not null &&
            currentWindow is not null &&
            plannedWindow.WindowId.Equals(
                currentWindow.WindowId,
                StringComparison.OrdinalIgnoreCase);

        if (!sameWindow ||
            template?.Available != true)
        {
            return new(
                Attempted: false,
                Recovered: false,
                Status: "not-attempted",
                Confidence: 0,
                Reason: "Visual reacquire không đủ điều kiện: cần cùng WindowId và visual target template khả dụng.",
                Tracking: null,
                ReplacementFrame: null);
        }

        DesktopScreenshotFrame? reacquireFrame =
            null;

        try
        {
            reacquireFrame =
                await screenshots.CaptureStableVirtualScreenAsync(
                    maximumWaitMs: 3000,
                    cancellationToken);

            var relocation =
                visualTargetPersistence.Relocate(
                    reacquireFrame,
                    template,
                    minimumScore:
                        MinimumRelocationConfidence);

            if (!relocation.Relocated ||
                relocation.Ambiguous ||
                relocation.Confidence <
                    MinimumRelocationConfidence)
            {
                return new(
                    Attempted: true,
                    Recovered: false,
                    Status:
                        relocation.Ambiguous
                            ? "ambiguous"
                            : "not-found",
                    Confidence:
                        relocation.Confidence,
                    Reason:
                        $"Visual reacquire chưa đủ an toàn: {relocation.Reason}",
                    Tracking: null,
                    ReplacementFrame: null);
            }

            var centerDesktopX =
                reacquireFrame.Left +
                relocation.Left +
                relocation.Width / 2;

            var centerDesktopY =
                reacquireFrame.Top +
                relocation.Top +
                relocation.Height / 2;

            var insideSameWindow =
                centerDesktopX >= currentWindow!.Left &&
                centerDesktopX <
                    currentWindow.Left +
                    currentWindow.Width &&
                centerDesktopY >= currentWindow.Top &&
                centerDesktopY <
                    currentWindow.Top +
                    currentWindow.Height;

            if (!insideSameWindow)
            {
                return new(
                    Attempted: true,
                    Recovered: false,
                    Status: "outside-window",
                    Confidence:
                        relocation.Confidence,
                    Reason:
                        "OpenCV tìm thấy target nhưng tâm target không còn nằm trong cùng WindowId đã lập kế hoạch.",
                    Tracking: null,
                    ReplacementFrame: null);
            }

            var recoveredDecision =
                decision with
                {
                    BoxLeft =
                        relocation.Left,
                    BoxTop =
                        relocation.Top,
                    BoxWidth =
                        relocation.Width,
                    BoxHeight =
                        relocation.Height,
                    ImageX =
                        relocation.Left +
                        relocation.Width / 2,
                    ImageY =
                        relocation.Top +
                        relocation.Height / 2,
                    Confidence =
                        Math.Min(
                            decision.Confidence,
                            relocation.Confidence)
                };

            var tracking =
                new DesktopTargetTrackingResult(
                    recoveredDecision,
                    Adjusted: true,
                    SafeToExecute: true,
                    Confidence:
                        relocation.Confidence,
                    Reason:
                        $"Geometry tracker không đủ nhưng OpenCV reacquire tìm lại target duy nhất trong cùng WindowId. {relocation.Reason}");

            var replacementFrame =
                reacquireFrame;

            // Caller chỉ cần geometry của capture mới.
            replacementFrame.Clear();
            reacquireFrame =
                null;

            return new(
                Attempted: true,
                Recovered: true,
                Status: "reacquired",
                Confidence:
                    relocation.Confidence,
                Reason:
                    tracking.Reason,
                Tracking:
                    tracking,
                ReplacementFrame:
                    replacementFrame);
        }
        finally
        {
            reacquireFrame?.Clear();
        }
    }
}
