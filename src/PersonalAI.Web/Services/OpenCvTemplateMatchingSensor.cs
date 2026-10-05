using OpenCvSharp;
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

public sealed class OpenCvTemplateMatchingSensor
    : IDesktopTemplateMatchingSensor
{
    private const double MinimumUniquenessMargin = 0.04;

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

        if (!OperatingSystem.IsWindows())
        {
            return Unavailable(
                "OpenCV native runtime hiện chỉ bật trên Windows.");
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

        try
        {
            using var source =
                Cv2.ImDecode(
                    frame.Jpeg,
                    ImreadModes.Grayscale);

            using var template =
                Cv2.ImDecode(
                    templateImage.ToArray(),
                    ImreadModes.Grayscale);

            if (source.Empty() ||
                template.Empty())
            {
                return Unavailable(
                    "OpenCV không decode được frame/template.");
            }

            var candidates =
                new List<MatchCandidate>();

            foreach (var scale in
                     ScaleCandidates)
            {
                var width =
                    Math.Max(
                        2,
                        (int)Math.Round(
                            template.Width *
                            scale));
                var height =
                    Math.Max(
                        2,
                        (int)Math.Round(
                            template.Height *
                            scale));

                if (width >
                        source.Width ||
                    height >
                        source.Height)
                {
                    continue;
                }

                using var scaled =
                    new Mat();

                Cv2.Resize(
                    template,
                    scaled,
                    new Size(
                        width,
                        height),
                    interpolation:
                        scale < 1.0
                            ? InterpolationFlags.Area
                            : InterpolationFlags.Linear);

                using var result =
                    new Mat();

                Cv2.MatchTemplate(
                    source,
                    scaled,
                    result,
                    TemplateMatchModes.CCoeffNormed);

                Cv2.MinMaxLoc(
                    result,
                    out _,
                    out var maxValue,
                    out _,
                    out var maxLocation);

                var candidate =
                    new MatchCandidate(
                        maxValue,
                        maxLocation.X,
                        maxLocation.Y,
                        width,
                        height,
                        scale);

                candidates.Add(
                    candidate);

                using var suppressed =
                    result.Clone();

                var suppressLeft =
                    Math.Max(
                        0,
                        maxLocation.X -
                        width / 2);
                var suppressTop =
                    Math.Max(
                        0,
                        maxLocation.Y -
                        height / 2);
                var suppressWidth =
                    Math.Min(
                        suppressed.Width -
                        suppressLeft,
                        Math.Max(
                            1,
                            width * 2));
                var suppressHeight =
                    Math.Min(
                        suppressed.Height -
                        suppressTop,
                        Math.Max(
                            1,
                            height * 2));

                Cv2.Rectangle(
                    suppressed,
                    new Rect(
                        suppressLeft,
                        suppressTop,
                        suppressWidth,
                        suppressHeight),
                    Scalar.All(-1),
                    thickness: -1);

                Cv2.MinMaxLoc(
                    suppressed,
                    out _,
                    out var secondValue,
                    out _,
                    out var secondLocation);

                candidates.Add(
                    new MatchCandidate(
                        secondValue,
                        secondLocation.X,
                        secondLocation.Y,
                        width,
                        height,
                        scale));
            }

            if (candidates.Count == 0)
            {
                return Unavailable(
                    "Không có scale template hợp lệ trong frame.");
            }

            var ordered =
                candidates
                    .OrderByDescending(candidate =>
                        candidate.Score)
                    .ToArray();

            var best =
                ordered[0];

            var second =
                ordered
                    .Skip(1)
                    .FirstOrDefault(candidate =>
                        IsSpatiallyDistinct(
                            best,
                            candidate));

            var secondScore =
                second?.Score ??
                -1.0;

            var ambiguous =
                best.Score >=
                    minimumScore &&
                second is not null &&
                second.Score >=
                    minimumScore &&
                best.Score -
                    second.Score <
                    MinimumUniquenessMargin;

            var matched =
                best.Score >=
                    minimumScore &&
                !ambiguous;

            return new(
                Available: true,
                Matched:
                    matched,
                Ambiguous:
                    ambiguous,
                Score:
                    Math.Clamp(
                        best.Score,
                        -1,
                        1),
                SecondBestScore:
                    Math.Clamp(
                        secondScore,
                        -1,
                        1),
                Left:
                    best.Left,
                Top:
                    best.Top,
                Width:
                    best.Width,
                Height:
                    best.Height,
                Scale:
                    best.Scale,
                Provider:
                    "opencv-template",
                Reason:
                    ambiguous
                        ? $"OpenCV có hai match gần ngang nhau: best={best.Score:0.000}; second={secondScore:0.000}; từ chối đoán."
                        : matched
                            ? $"OpenCV template match đạt {best.Score:0.000}, second={secondScore:0.000}, scale={best.Scale:0.00}."
                            : $"OpenCV template match tốt nhất {best.Score:0.000} thấp hơn ngưỡng {minimumScore:0.000}.");
        }
        catch (Exception exception) when (
            exception is
                OpenCVException or
                DllNotFoundException or
                TypeInitializationException or
                BadImageFormatException)
        {
            return Unavailable(
                $"OpenCV không khả dụng: {exception.GetType().Name}: {exception.Message}");
        }
    }

    private static bool IsSpatiallyDistinct(
        MatchCandidate left,
        MatchCandidate right)
    {
        var leftCenterX =
            left.Left +
            left.Width / 2.0;
        var leftCenterY =
            left.Top +
            left.Height / 2.0;
        var rightCenterX =
            right.Left +
            right.Width / 2.0;
        var rightCenterY =
            right.Top +
            right.Height / 2.0;

        var threshold =
            Math.Max(
                8.0,
                Math.Min(
                    left.Width,
                    left.Height) *
                0.5);

        return Math.Abs(
                   leftCenterX -
                   rightCenterX) >
                   threshold ||
               Math.Abs(
                   leftCenterY -
                   rightCenterY) >
                   threshold;
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

    private sealed record MatchCandidate(
        double Score,
        int Left,
        int Top,
        int Width,
        int Height,
        double Scale);
}
