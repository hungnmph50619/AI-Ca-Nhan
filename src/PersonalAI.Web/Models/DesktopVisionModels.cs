namespace PersonalAI.Web.Models;

public sealed record DesktopScreenshotFrame(
    byte[] Jpeg,
    int Left,
    int Top,
    int Width,
    int Height,
    DateTimeOffset CapturedAtUtc,
    string CaptureScope = "virtual-desktop",
    string? WindowId = null,
    string? WindowTitle = null,
    bool WindowWasForeground = false,
    string CaptureBackend = "copy-from-screen",
    string? CaptureFallbackReason = null,
    string? MonitorDevice = null,
    bool MonitorWasPrimary = false,
    uint MonitorDpiX = 96,
    uint MonitorDpiY = 96,
    double WindowVisibleRatio = 1.0,
    bool WindowLikelyOccluded = false)
{
    public void Clear()
    {
        if (Jpeg.Length > 0)
            Array.Clear(Jpeg, 0, Jpeg.Length);
    }
}

public sealed record DesktopVisionTarget(
    bool Found,
    string Label,
    int ImageX,
    int ImageY,
    double Confidence,
    string Reason);

public sealed record LeagueVisualDecision(
    string State,
    string Action,
    string Label,
    int ImageX,
    int ImageY,
    double Confidence,
    string Reason);

public sealed record LeaguePracticeAutomationResult(
    string Status,
    string Phase,
    int StepsCompleted,
    string Detail,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc);


public sealed record DesktopSceneElement(
    string Id,
    string Role,
    string Label,
    string ParentId,
    int BoxLeft,
    int BoxTop,
    int BoxWidth,
    int BoxHeight,
    double Confidence,
    IReadOnlyList<string> Relations);

public sealed record DesktopOperatorDecision(
    string State,
    string Plan,
    string CurrentSubgoal,
    double GoalProgress,
    IReadOnlyList<string> VerifiedMilestones,
    string Action,
    string Query,
    string Text,
    string Key,
    IReadOnlyList<string> Keys,
    string Url,
    string TargetLabel,
    string CoordinateSpace,
    string CoordinateWindowId,
    int ImageX,
    int ImageY,
    int EndImageX,
    int EndImageY,
    double NormalizedX,
    double NormalizedY,
    double EndNormalizedX,
    double EndNormalizedY,
    int BoxLeft,
    int BoxTop,
    int BoxWidth,
    int BoxHeight,
    double BoxNormalizedLeft,
    double BoxNormalizedTop,
    double BoxNormalizedWidth,
    double BoxNormalizedHeight,
    int ScrollDelta,
    string ExpectedEffect,
    double Confidence,
    string Reason,
    IReadOnlyList<DesktopSceneElement>? SceneElements = null,
    string TargetElementId = "");


public sealed record DesktopOperatorIntent(
    string Intent,
    string Target,
    string Query,
    string Text,
    string Key,
    IReadOnlyList<string> Keys,
    int ImageX,
    int ImageY,
    int BoxLeft,
    int BoxTop,
    int BoxWidth,
    int BoxHeight,
    int ScrollDelta,
    string ExpectedEffect,
    double Confidence,
    string Reason);

public sealed record DesktopVisionVerification(
    bool Satisfied,
    double Confidence,
    string Reason);

public sealed record DesktopFrameDifference(
    bool Comparable,
    double ChangedRatio,
    int ChangedPixelSamples,
    int TotalPixelSamples,
    int BoxLeft,
    int BoxTop,
    int BoxWidth,
    int BoxHeight,
    double MeanChannelDelta,
    string Reason)
{
    public bool HasMeaningfulChange =>
        Comparable &&
        ChangedPixelSamples > 0 &&
        BoxWidth > 0 &&
        BoxHeight > 0;
}
