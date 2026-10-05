using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public sealed record DesktopTemplateMatchResult(
    bool Available,
    bool Matched,
    bool Ambiguous,
    double Score,
    double SecondBestScore,
    int Left,
    int Top,
    int Width,
    int Height,
    double Scale,
    string Provider,
    string Reason);

public interface IDesktopTemplateMatchingSensor
{
    DesktopTemplateMatchResult Match(
        DesktopScreenshotFrame frame,
        ReadOnlyMemory<byte> templateImage,
        double minimumScore = 0.88);
}

public sealed class OpenCvTemplateMatchingSensor(
    ILocalVisualProviderHealthRegistry health,
    ILocalVisualSensorBudgetPolicy budgetPolicy,
    ILocalVisionWorkerClient worker)
    : IDesktopTemplateMatchingSensor
{
    private static readonly double[] ScaleCandidates =
    [
        1.00,
        0.95,
        1.05,
        0.90,
        1.10,
        0.85,
        1.15
    ];

    public DesktopTemplateMatchResult Match(
        DesktopScreenshotFrame frame,
        ReadOnlyMemory<byte> templateImage,
        double minimumScore = 0.88)
    {
        ArgumentNullException.ThrowIfNull(frame);

        var templateBudget =
            new LocalVisualSensorBudget(
                MaximumTotalMilliseconds: 1200,
                MaximumOcrMilliseconds: 0,
                MaximumTemplateMilliseconds: 1000,
                AllowWindowsOcr: false,
                AllowPaddleOcr: false,
                AllowOpenCv: true,
                Reason: "OpenCV template/recovery budget.");

        var providerHealth =
            health.Get(
                "opencv-template");

        if (!budgetPolicy.CanUseProvider(
                "opencv-template",
                templateBudget,
                elapsedMilliseconds: 0,
                providerHealth,
                out var budgetReason))
        {
            return Unavailable(
                budgetReason);
        }

        if (health.ShouldSkip(
                "opencv-template",
                DateTimeOffset.UtcNow,
                out var skipReason))
        {
            return Unavailable(
                skipReason);
        }

        if (!OperatingSystem.IsWindows())
        {
            return Unavailable(
                "OpenCV worker hiện chỉ bật trên Windows.");
        }

        if (frame.Jpeg is null ||
            frame.Jpeg.Length == 0 ||
            templateImage.IsEmpty)
        {
            return Unavailable(
                "Thiếu frame hoặc template image.");
        }

        minimumScore =
            Math.Clamp(
                minimumScore,
                0.50,
                0.999);

        var stopwatch =
            System.Diagnostics.Stopwatch.StartNew();

        var invocation =
            worker.Invoke(
                new LocalVisionWorkerRequest(
                    Operation: "opencv-template",
                    JpegBase64:
                        Convert.ToBase64String(
                            frame.Jpeg),
                    TemplateBase64:
                        Convert.ToBase64String(
                            templateImage.Span),
                    MinimumScore:
                        minimumScore),
                timeoutMilliseconds: 1100);

        stopwatch.Stop();

        if (!invocation.Success ||
            invocation.Response?.Match is null)
        {
            health.RecordFailure(
                "opencv-template",
                stopwatch.ElapsedMilliseconds,
                invocation.Detail);

            return Unavailable(
                invocation.Detail);
        }

        var match =
            invocation.Response.Match;

        health.RecordSuccess(
            "opencv-template",
            stopwatch.ElapsedMilliseconds,
            invocation.Response.Detail ??
            "OpenCV worker hoàn tất.");

        return new(
            Available: true,
            Matched:
                match.Matched,
            Ambiguous:
                match.Ambiguous,
            Score:
                match.Score,
            SecondBestScore:
                match.SecondBestScore,
            Left:
                match.Left,
            Top:
                match.Top,
            Width:
                match.Width,
            Height:
                match.Height,
            Scale:
                match.Scale,
            Provider:
                "opencv-template",
            Reason:
                invocation.Response.Detail ??
                "OpenCV worker hoàn tất.");
    }

    internal static IReadOnlyList<double> CandidateScalesForAcceptance() =>
        ScaleCandidates;

    internal static bool AcceptScore(
        double score,
        double minimumScore) =>
        double.IsFinite(score) &&
        score >=
            Math.Clamp(
                minimumScore,
                0.50,
                0.999);

    private static DesktopTemplateMatchResult Unavailable(
        string reason) =>
        new(
            Available: false,
            Matched: false,
            Ambiguous: false,
            Score: 0,
            SecondBestScore: -1,
            Left: 0,
            Top: 0,
            Width: 0,
            Height: 0,
            Scale: 1,
            Provider:
                "opencv-template",
            Reason:
                reason);
}
