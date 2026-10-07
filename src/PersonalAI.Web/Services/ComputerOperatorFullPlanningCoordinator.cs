using System.Diagnostics;
using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public sealed record ComputerOperatorFullPlanningResult(
    DesktopOperatorDecision Decision,
    bool Degraded,
    long LatencyMilliseconds);

public interface IComputerOperatorFullPlanningCoordinator
{
    Task<ComputerOperatorFullPlanningResult> PlanAsync(
        DesktopScreenshotFrame frame,
        string goal,
        string windowsContext,
        string historyContext,
        string temporalSceneContext,
        CancellationToken cancellationToken);
}

/// <summary>
/// Điều phối full Gemini planner và chỉ trả action candidate + provider health evidence.
/// Không có quyền EXECUTE/REPLAN và không tự thay đổi backoff/circuit state.
/// </summary>
public sealed class ComputerOperatorFullPlanningCoordinator(
    IComputerOperatorVisionRouter vision,
    IComputerOperatorProviderResiliencePolicy providerResilience)
    : IComputerOperatorFullPlanningCoordinator
{
    public async Task<ComputerOperatorFullPlanningResult> PlanAsync(
        DesktopScreenshotFrame frame,
        string goal,
        string windowsContext,
        string historyContext,
        string temporalSceneContext,
        CancellationToken cancellationToken)
    {
        var stopwatch =
            Stopwatch.StartNew();

        var decision =
            await vision.DecideActionAsync(
                frame,
                goal,
                windowsContext,
                historyContext,
                temporalSceneContext,
                cancellationToken);

        stopwatch.Stop();

        return new(
            decision,
            providerResilience.IsDegradedPlannerDecision(
                decision),
            stopwatch.ElapsedMilliseconds);
    }
}
