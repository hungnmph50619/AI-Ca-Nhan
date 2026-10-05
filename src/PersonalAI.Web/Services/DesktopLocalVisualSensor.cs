using System.Drawing;
using System.Drawing.Drawing2D;
using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public sealed record DesktopPerceptualHash(
    ulong Value,
    string Algorithm,
    int Bits = 64)
{
    public string Hex =>
        Value.ToString("X16");
}

public sealed record DesktopLocalVisualObservation(
    bool Comparable,
    DesktopPerceptualHash? BeforeHash,
    DesktopPerceptualHash? AfterHash,
    int HashDistance,
    double HashSimilarity,
    DesktopFrameDifference? RegionDelta,
    bool MeaningfulVisualChange,
    string Reason);

public interface IDesktopLocalVisualSensor
{
    DesktopPerceptualHash ComputeHash(
        DesktopScreenshotFrame frame);

    DesktopLocalVisualObservation Analyze(
        DesktopScreenshotFrame before,
        DesktopScreenshotFrame after);
}

public sealed class DesktopLocalVisualSensor(
    IDesktopFrameDifferenceService frameDifferences)
    : IDesktopLocalVisualSensor
{
    private const int HashWidth = 9;
    private const int HashHeight = 8;
    private const int SignificantHashDistance = 6;

    public DesktopPerceptualHash ComputeHash(
        DesktopScreenshotFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        if (frame.Jpeg is null ||
            frame.Jpeg.Length == 0)
        {
            throw new ToolExecutionInputException(
                "Frame không có dữ liệu JPEG để tính perceptual hash.");
        }

        using var stream =
            new MemoryStream(
                frame.Jpeg,
                writable: false);
        using var source =
            new Bitmap(stream);
        using var reduced =
            new Bitmap(
                HashWidth,
                HashHeight);

        using (var graphics =
               Graphics.FromImage(
                   reduced))
        {
            graphics.CompositingMode =
                CompositingMode.SourceCopy;
            graphics.InterpolationMode =
                InterpolationMode.HighQualityBilinear;
            graphics.PixelOffsetMode =
                PixelOffsetMode.HighQuality;

            graphics.DrawImage(
                source,
                0,
                0,
                HashWidth,
                HashHeight);
        }

        Span<byte> grayscale =
            stackalloc byte[
                HashWidth * HashHeight];

        var offset = 0;
        for (var y = 0;
             y < HashHeight;
             y++)
        {
            for (var x = 0;
                 x < HashWidth;
                 x++)
            {
                var pixel =
                    reduced.GetPixel(
                        x,
                        y);

                grayscale[offset++] =
                    ToLuma(
                        pixel.R,
                        pixel.G,
                        pixel.B);
            }
        }

        return new(
            ComputeDifferenceHash(
                grayscale,
                HashWidth,
                HashHeight),
            Algorithm: "dhash-9x8");
    }

    public DesktopLocalVisualObservation Analyze(
        DesktopScreenshotFrame before,
        DesktopScreenshotFrame after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);

        var regionDelta =
            frameDifferences.Compare(
                before,
                after);

        if (!regionDelta.Comparable)
        {
            return new(
                Comparable: false,
                BeforeHash: null,
                AfterHash: null,
                HashDistance: 64,
                HashSimilarity: 0,
                RegionDelta: regionDelta,
                MeaningfulVisualChange: false,
                Reason:
                    $"Không so được local visual sensor: {regionDelta.Reason}");
        }

        var beforeHash =
            ComputeHash(
                before);
        var afterHash =
            ComputeHash(
                after);

        var distance =
            HammingDistance(
                beforeHash.Value,
                afterHash.Value);

        var similarity =
            1.0 -
            distance /
            64.0;

        var meaningful =
            distance >=
                SignificantHashDistance ||
            regionDelta.HasMeaningfulChange;

        return new(
            Comparable: true,
            BeforeHash: beforeHash,
            AfterHash: afterHash,
            HashDistance: distance,
            HashSimilarity:
                Math.Clamp(
                    similarity,
                    0,
                    1),
            RegionDelta: regionDelta,
            MeaningfulVisualChange:
                meaningful,
            Reason:
                meaningful
                    ? $"Local visual sensor phát hiện thay đổi: dHash distance={distance}/64; region={regionDelta.ChangedRatio * 100:0.00}%."
                    : $"Local visual sensor coi frame ổn định: dHash distance={distance}/64; region={regionDelta.ChangedRatio * 100:0.00}%.");
    }

    internal static ulong ComputeDifferenceHash(
        ReadOnlySpan<byte> grayscale,
        int width,
        int height)
    {
        if (width < 2 ||
            height < 1 ||
            grayscale.Length <
                width * height)
        {
            throw new ArgumentOutOfRangeException(
                nameof(grayscale),
                "Ma trận grayscale không đủ để tính dHash.");
        }

        var comparisons =
            (width - 1) *
            height;

        if (comparisons > 64)
        {
            throw new ArgumentOutOfRangeException(
                nameof(width),
                "dHash hiện hỗ trợ tối đa 64 phép so sánh.");
        }

        ulong hash = 0;
        var bit = 0;

        for (var y = 0;
             y < height;
             y++)
        {
            var row =
                y * width;

            for (var x = 0;
                 x < width - 1;
                 x++)
            {
                if (grayscale[row + x] >
                    grayscale[row + x + 1])
                {
                    hash |=
                        1UL << bit;
                }

                bit++;
            }
        }

        return hash;
    }

    internal static int HammingDistance(
        ulong left,
        ulong right) =>
        System.Numerics.BitOperations.PopCount(
            left ^ right);

    private static byte ToLuma(
        byte red,
        byte green,
        byte blue) =>
        checked((byte)Math.Clamp(
            (red * 299 +
             green * 587 +
             blue * 114) /
            1000,
            0,
            255));
}
