using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public interface IComputerOperatorVisionProvider
{
    string Name { get; }
    string Model { get; }
    bool Ready { get; }

    Task<DesktopVisionTarget> LocateAsync(
        DesktopScreenshotFrame frame,
        string targetDescription,
        CancellationToken cancellationToken);

    Task<DesktopVisionVerification> VerifyAsync(
        DesktopScreenshotFrame frame,
        string expectedState,
        DesktopFrameDifference? frameDifference,
        CancellationToken cancellationToken);

    Task<DesktopOperatorIntent?> DecideIntentAsync(
        DesktopScreenshotFrame frame,
        string goal,
        string windowsContext,
        string taskHistory,
        CancellationToken cancellationToken);

    Task<DesktopOperatorDecision> DecideActionAsync(
        DesktopScreenshotFrame frame,
        string goal,
        string windowsContext,
        string taskHistory,
        string temporalSceneContext,
        CancellationToken cancellationToken);
}

public interface IComputerOperatorVisionRouter
    : IComputerOperatorVisionProvider
{
    IReadOnlyList<ComputerOperatorVisionProviderInfo> GetProviders();
}

public sealed record ComputerOperatorVisionProviderInfo(
    string Name,
    string Model,
    bool Ready,
    int Priority);
