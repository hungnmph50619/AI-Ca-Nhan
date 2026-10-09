using System.Diagnostics;
using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public sealed record ComputerOperatorMinimalIntentResult(
    bool Compiled,
    DesktopOperatorIntent? Intent,
    DesktopOperatorDecision? Decision,
    string RejectionReason,
    long LatencyMilliseconds);

public interface IComputerOperatorMinimalIntentCoordinator
{
    Task<ComputerOperatorMinimalIntentResult> TryPlanAsync(
        DesktopScreenshotFrame frame,
        string goal,
        string windowsContext,
        string historyContext,
        CancellationToken cancellationToken);
}

/// <summary>
/// Điều phối Gemini Minimal Intent -> Intent Compiler và chỉ trả action candidate.
/// Không có quyền EXECUTE/REPLAN.
/// </summary>
public sealed class ComputerOperatorMinimalIntentCoordinator(
    IComputerOperatorVisionRouter vision,
    IComputerOperatorIntentCompiler intentCompiler)
    : IComputerOperatorMinimalIntentCoordinator
{
    public async Task<ComputerOperatorMinimalIntentResult> TryPlanAsync(
        DesktopScreenshotFrame frame,
        string goal,
        string windowsContext,
        string historyContext,
        CancellationToken cancellationToken)
    {
        var stopwatch =
            Stopwatch.StartNew();

        var intent =
            await vision.DecideIntentAsync(
                frame,
                goal,
                windowsContext,
                historyContext,
                cancellationToken);

        stopwatch.Stop();

        if (intent is null)
        {
            return new(
                Compiled: false,
                Intent: null,
                Decision: null,
                RejectionReason: string.Empty,
                LatencyMilliseconds: stopwatch.ElapsedMilliseconds);
        }

        var compiled =
            intentCompiler.TryCompile(
                intent,
                frame,
                out var decision,
                out var rejectionReason);

        return new(
            Compiled: compiled,
            Intent: intent,
            Decision: compiled ? decision : null,
            RejectionReason: rejectionReason,
            LatencyMilliseconds: stopwatch.ElapsedMilliseconds);
    }
}
