using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public sealed class GeminiComputerOperatorVisionProvider(
    DesktopVisionService vision)
    : IComputerOperatorVisionProvider
{
    public string Name => "Gemini";
    public string Model => vision.Model;
    public bool Ready => vision.Ready;

    public Task<DesktopVisionTarget> LocateAsync(
        DesktopScreenshotFrame frame,
        string targetDescription,
        CancellationToken cancellationToken) =>
        vision.LocateAsync(
            frame,
            targetDescription,
            cancellationToken);

    public Task<DesktopVisionVerification> VerifyAsync(
        DesktopScreenshotFrame frame,
        string expectedState,
        DesktopFrameDifference? frameDifference,
        CancellationToken cancellationToken) =>
        vision.VerifyAsync(
            frame,
            expectedState,
            frameDifference,
            cancellationToken);

    public Task<DesktopOperatorIntent?> DecideIntentAsync(
        DesktopScreenshotFrame frame,
        string goal,
        string windowsContext,
        string taskHistory,
        CancellationToken cancellationToken) =>
        vision.DecideComputerOperatorIntentAsync(
            frame,
            goal,
            windowsContext,
            taskHistory,
            cancellationToken);

    public Task<DesktopOperatorDecision> DecideActionAsync(
        DesktopScreenshotFrame frame,
        string goal,
        string windowsContext,
        string taskHistory,
        string temporalSceneContext,
        CancellationToken cancellationToken) =>
        vision.DecideComputerOperatorActionAsync(
            frame,
            goal,
            windowsContext,
            taskHistory,
            temporalSceneContext,
            cancellationToken);
}
