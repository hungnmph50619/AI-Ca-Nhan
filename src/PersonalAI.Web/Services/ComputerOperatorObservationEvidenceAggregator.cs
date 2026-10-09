using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public sealed record ComputerOperatorObservationEvidence(
    ComputerOperatorDesktopState DesktopState,
    string SceneFingerprint,
    string PromptSummary,
    ComputerWindowInfo? ForegroundWindow,
    DesktopPerceptualHash? VisualHash,
    string? VisualHashFallbackReason,
    string DesktopStateSummary);

public interface IComputerOperatorObservationEvidenceAggregator
{
    ComputerOperatorObservationEvidence Build(
        DesktopScreenshotFrame frame);
}

public sealed class ComputerOperatorObservationEvidenceAggregator(
    IComputerOperatorDesktopStateBuilder desktopStateBuilder,
    IDesktopLocalVisualSensor localVisualSensor,
    IComputerOperatorSceneIdentityService sceneIdentity)
    : IComputerOperatorObservationEvidenceAggregator
{
    public ComputerOperatorObservationEvidence Build(
        DesktopScreenshotFrame frame)
    {
        var desktopState =
            desktopStateBuilder.Build(
                frame);

        DesktopPerceptualHash? visualHash =
            null;
        string? visualHashFallbackReason =
            null;

        try
        {
            visualHash =
                localVisualSensor.ComputeHash(
                    frame);
        }
        catch (Exception exception) when (
            exception is
                PlatformNotSupportedException or
                ArgumentException or
                InvalidOperationException or
                ToolExecutionInputException)
        {
            visualHashFallbackReason =
                $"Visual scene hash không khả dụng ({exception.GetType().Name}); fallback về structural scene fingerprint.";
        }

        var sceneFingerprint =
            sceneIdentity.BuildFingerprint(
                desktopState,
                visualHash);

        var promptSummary =
            desktopState.ToPromptSummary();

        var foreground =
            desktopState.ForegroundWindow;

        var desktopStateSummary =
            $"Unified Desktop State: foreground={foreground?.Title ?? "không xác định"}; windows={desktopState.Windows.Count}; structuredNodes={desktopState.StructuredScene?.NodeCount ?? 0}; graphNodes={desktopState.StructuredGraph?.NodeCount ?? 0}; interactive={desktopState.StructuredGraph?.InteractiveNodeCount ?? 0}; frame={desktopState.CaptureScope}/{desktopState.FrameWidth}x{desktopState.FrameHeight}.";

        return new(
            desktopState,
            sceneFingerprint,
            promptSummary,
            foreground,
            visualHash,
            visualHashFallbackReason,
            desktopStateSummary);
    }
}
