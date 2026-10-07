using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public sealed record ComputerOperatorStructuredPreExecutionRevalidation(
    StructuredTargetRevalidationResult Result,
    string WindowId,
    double SceneAgeMilliseconds);

public interface IComputerOperatorStructuredPreExecutionRevalidator
{
    ComputerOperatorStructuredPreExecutionRevalidation Revalidate(
        DesktopOperatorDecision decision,
        ComputerOperatorDesktopState desktopState);
}

/// <summary>
/// Fresh structured evidence ngay trước EXECUTE:
/// foreground -> UIA snapshot -> scene graph -> target revalidation.
/// Chỉ trả valid/remapped/rejected evidence; không EXECUTE và không tự REPLAN.
/// </summary>
public sealed class ComputerOperatorStructuredPreExecutionRevalidator(
    IComputerUseService computer,
    IStructuredDesktopSnapshotService structuredDesktop,
    IStructuredTargetRevalidator structuredTargetRevalidator)
    : IComputerOperatorStructuredPreExecutionRevalidator
{
    public ComputerOperatorStructuredPreExecutionRevalidation Revalidate(
        DesktopOperatorDecision decision,
        ComputerOperatorDesktopState desktopState)
    {
        ArgumentNullException.ThrowIfNull(decision);
        ArgumentNullException.ThrowIfNull(desktopState);

        var active =
            computer.GetActiveWindow();

        if (active is null)
        {
            var rejected =
                structuredTargetRevalidator.Revalidate(
                    decision,
                    graph: null,
                    DateTimeOffset.UtcNow);

            return new(
                rejected,
                string.Empty,
                -1);
        }

        var snapshot =
            structuredDesktop.CaptureWindow(
                active.WindowId,
                maximumNodes: 240,
                maximumDepth: 7);

        var graph =
            UnifiedStructuredSceneGraphBuilder.Build(
                snapshot,
                active,
                desktopState.FrameLeft,
                desktopState.FrameTop,
                desktopState.FrameWidth,
                desktopState.FrameHeight);

        var now =
            DateTimeOffset.UtcNow;

        var result =
            structuredTargetRevalidator.Revalidate(
                decision,
                graph,
                now);

        var sceneAgeMilliseconds =
            graph is null
                ? -1
                : Math.Max(
                    0,
                    (now - graph.CapturedAtUtc)
                        .TotalMilliseconds);

        return new(
            result,
            active.WindowId,
            sceneAgeMilliseconds);
    }
}
