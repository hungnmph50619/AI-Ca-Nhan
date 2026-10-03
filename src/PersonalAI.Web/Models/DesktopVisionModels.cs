namespace PersonalAI.Web.Models;

public sealed record DesktopScreenshotFrame(
    byte[] Jpeg,
    int Left,
    int Top,
    int Width,
    int Height,
    DateTimeOffset CapturedAtUtc)
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


public sealed record DesktopOperatorDecision(
    string State,
    string Plan,
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
    string Reason);


public sealed record DesktopVisionVerification(
    bool Satisfied,
    double Confidence,
    string Reason);
