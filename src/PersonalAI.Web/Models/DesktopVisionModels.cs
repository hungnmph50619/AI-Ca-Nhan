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
