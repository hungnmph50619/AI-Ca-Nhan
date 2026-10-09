using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public sealed record ComputerOperatorClickGroundingResult(
    ComputerOperatorGroundingResult Grounding,
    DesktopTargetTrackingResult? Tracking,
    ComputerWindowInfo? PlannedWindow,
    ComputerWindowInfo? CurrentWindow);

public interface IComputerOperatorClickGroundingCoordinator
{
    ComputerOperatorClickGroundingResult Ground(
        DesktopOperatorDecision decision,
        ComputerOperatorDesktopState desktopState,
        DesktopScreenshotFrame planningFrame,
        ComputerWindowInfo? activeAtPlanning,
        bool deterministicTextSurface);
}

/// <summary>
/// Hợp nhất semantic grounding + window resolution + geometry tracking cho click candidate.
/// Chỉ trả grounded/tracked candidate; không EXECUTE, không visual-reacquire và không REPLAN.
/// </summary>
public sealed class ComputerOperatorClickGroundingCoordinator(
    IComputerUseService computer,
    IComputerOperatorGroundingService groundingService,
    IDesktopDynamicTargetTracker targetTracker)
    : IComputerOperatorClickGroundingCoordinator
{
    public ComputerOperatorClickGroundingResult Ground(
        DesktopOperatorDecision decision,
        ComputerOperatorDesktopState desktopState,
        DesktopScreenshotFrame planningFrame,
        ComputerWindowInfo? activeAtPlanning,
        bool deterministicTextSurface)
    {
        var grounding =
            groundingService.GroundClickTarget(
                decision,
                desktopState,
                deterministicTextSurface);

        if (!grounding.Resolved)
        {
            return new(
                grounding,
                null,
                null,
                null);
        }

        var groundedDecision =
            grounding.Decision;

        var plannedWindow =
            ResolveTrackingWindow(
                groundedDecision,
                activeAtPlanning,
                planningFrame);

        var currentWindow =
            plannedWindow is null
                ? null
                : computer
                    .GetWindows(50)
                    .Windows
                    .FirstOrDefault(
                        window =>
                            window.WindowId.Equals(
                                plannedWindow.WindowId,
                                StringComparison.OrdinalIgnoreCase));

        // Never track a visual target against a window whose geometry changed
        // after planning. The old frame's box can now point at another control.
        if (plannedWindow is not null &&
            currentWindow is not null &&
            !SameWindowGeometry(plannedWindow, currentWindow))
        {
            return new(
                new(
                    ComputerOperatorGroundingStatuses.Stale,
                    groundedDecision,
                    0,
                    grounding.Evidence,
                    "Cửa sổ đã thay đổi vị trí hoặc kích thước sau planning; phải quan sát lại trước click."),
                null,
                plannedWindow,
                currentWindow);
        }

        var tracking =
            targetTracker.Track(
                groundedDecision,
                planningFrame,
                plannedWindow,
                currentWindow);

        return new(
            grounding,
            tracking,
            plannedWindow,
            currentWindow);
    }

    internal static bool SameWindowGeometry(
        ComputerWindowInfo before,
        ComputerWindowInfo after) =>
        before.WindowId.Equals(after.WindowId, StringComparison.OrdinalIgnoreCase) &&
        before.Left == after.Left &&
        before.Top == after.Top &&
        before.Width == after.Width &&
        before.Height == after.Height;

    private ComputerWindowInfo? ResolveTrackingWindow(
        DesktopOperatorDecision decision,
        ComputerWindowInfo? activeAtPlanning,
        DesktopScreenshotFrame planningFrame)
    {
        if (!string.IsNullOrWhiteSpace(
                decision.CoordinateWindowId))
        {
            return computer
                .GetWindows(50)
                .Windows
                .FirstOrDefault(
                    window =>
                        window.WindowId.Equals(
                            decision.CoordinateWindowId.Trim(),
                            StringComparison.OrdinalIgnoreCase));
        }

        if (activeAtPlanning is null)
            return null;

        if (decision.BoxWidth <= 1 ||
            decision.BoxHeight <= 1)
        {
            return activeAtPlanning;
        }

        var centerX =
            planningFrame.Left +
            decision.BoxLeft +
            decision.BoxWidth / 2;

        var centerY =
            planningFrame.Top +
            decision.BoxTop +
            decision.BoxHeight / 2;

        var insideActive =
            centerX >= activeAtPlanning.Left &&
            centerX <
                activeAtPlanning.Left +
                activeAtPlanning.Width &&
            centerY >= activeAtPlanning.Top &&
            centerY <
                activeAtPlanning.Top +
                activeAtPlanning.Height;

        return insideActive
            ? activeAtPlanning
            : null;
    }
}
