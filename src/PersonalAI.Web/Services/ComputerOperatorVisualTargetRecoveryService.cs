using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public sealed record ComputerOperatorVisualTargetRecoveryResult(
    DesktopTargetTrackingResult? Recovery,
    bool Ambiguous,
    double Confidence,
    string Reason)
{
    public bool Recovered =>
        Recovery is not null &&
        Recovery.SafeToExecute;
}

public interface IComputerOperatorVisualTargetRecoveryService
{
    ComputerOperatorVisualTargetRecoveryResult TryRecover(
        DesktopOperatorDecision decision,
        DesktopScreenshotFrame reacquireFrame,
        DesktopVisualTargetTemplate template,
        ComputerWindowInfo currentWindow,
        double minimumScore = 0.90);
}

/// <summary>
/// Thử tìm lại target bằng visual persistence trong cùng cửa sổ đã biết.
/// Chỉ tạo recovery candidate; không capture, không execute và không tự replan.
/// </summary>
public sealed class ComputerOperatorVisualTargetRecoveryService(
    IDesktopVisualTargetPersistenceService visualTargetPersistence)
    : IComputerOperatorVisualTargetRecoveryService
{
    public ComputerOperatorVisualTargetRecoveryResult TryRecover(
        DesktopOperatorDecision decision,
        DesktopScreenshotFrame reacquireFrame,
        DesktopVisualTargetTemplate template,
        ComputerWindowInfo currentWindow,
        double minimumScore = 0.90)
    {
        ArgumentNullException.ThrowIfNull(decision);
        ArgumentNullException.ThrowIfNull(reacquireFrame);
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(currentWindow);

        var relocation =
            visualTargetPersistence.Relocate(
                reacquireFrame,
                template,
                minimumScore);

        if (!relocation.Relocated ||
            relocation.Ambiguous ||
            relocation.Confidence < minimumScore)
        {
            return new(
                Recovery: null,
                Ambiguous: relocation.Ambiguous,
                Confidence: relocation.Confidence,
                Reason: relocation.Reason);
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
            centerDesktopX >= currentWindow.Left &&
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
                Recovery: null,
                Ambiguous: false,
                Confidence: relocation.Confidence,
                Reason:
                    $"Visual target được tìm thấy nhưng nằm ngoài WindowId hiện tại; từ chối remap. {relocation.Reason}");
        }

        var recoveredDecision =
            decision with
            {
                BoxLeft = relocation.Left,
                BoxTop = relocation.Top,
                BoxWidth = relocation.Width,
                BoxHeight = relocation.Height,
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
                Confidence: relocation.Confidence,
                Reason:
                    $"Geometry tracker không đủ nhưng visual persistence tìm lại target duy nhất trong cùng WindowId. {relocation.Reason}");

        return new(
            tracking,
            Ambiguous: false,
            Confidence: relocation.Confidence,
            Reason: tracking.Reason);
    }
}
