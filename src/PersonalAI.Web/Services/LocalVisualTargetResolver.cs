namespace PersonalAI.Web.Services;

public enum LocalVisualTargetStatus
{
    Resolved,
    Ambiguous,
    NotFound,
    Unavailable
}

public sealed record LocalVisualTargetEvidence(
    string Source,
    double Confidence,
    int Left,
    int Top,
    int Width,
    int Height,
    string Detail);

public sealed record LocalVisualTargetResolution(
    LocalVisualTargetStatus Status,
    double Confidence,
    int Left,
    int Top,
    int Width,
    int Height,
    IReadOnlyList<LocalVisualTargetEvidence> Evidence,
    string Reason)
{
    public bool Resolved =>
        Status == LocalVisualTargetStatus.Resolved &&
        Width > 1 &&
        Height > 1;
}

public interface ILocalVisualTargetResolver
{
    LocalVisualTargetResolution Resolve(
        DesktopOcrResolution? ocr,
        DesktopVisualTargetRelocation? template,
        DesktopLocalVisualObservation? visualObservation = null);
}

public sealed class LocalVisualTargetResolver
    : ILocalVisualTargetResolver
{
    private const double MinimumResolvedConfidence = 0.82;
    private const double StrongEvidenceConfidence = 0.88;
    private const double MinimumAgreementIou = 0.35;

    public LocalVisualTargetResolution Resolve(
        DesktopOcrResolution? ocr,
        DesktopVisualTargetRelocation? template,
        DesktopLocalVisualObservation? visualObservation = null)
    {
        var evidence =
            new List<LocalVisualTargetEvidence>();

        LocalVisualTargetEvidence? ocrEvidence =
            null;

        if (ocr?.Resolved == true &&
            ocr.Target is not null)
        {
            var confidence =
                ScoreToConfidence(
                    ocr.Target.Score);

            ocrEvidence =
                new(
                    Source:
                        $"ocr:{ocr.Target.Source}",
                    Confidence:
                        confidence,
                    Left:
                        (int)Math.Round(ocr.Target.Left),
                    Top:
                        (int)Math.Round(ocr.Target.Top),
                    Width:
                        Math.Max(
                            2,
                            (int)Math.Round(ocr.Target.Width)),
                    Height:
                        Math.Max(
                            2,
                            (int)Math.Round(ocr.Target.Height)),
                    Detail:
                        ocr.Reason);

            evidence.Add(
                ocrEvidence);
        }

        LocalVisualTargetEvidence? templateEvidence =
            null;

        if (template?.Available == true &&
            template.Relocated &&
            !template.Ambiguous &&
            template.Width > 1 &&
            template.Height > 1)
        {
            templateEvidence =
                new(
                    Source:
                        template.Provider,
                    Confidence:
                        Math.Clamp(
                            template.Confidence,
                            0,
                            1),
                    Left:
                        template.Left,
                    Top:
                        template.Top,
                    Width:
                        template.Width,
                    Height:
                        template.Height,
                    Detail:
                        template.Reason);

            evidence.Add(
                templateEvidence);
        }

        if (ocr?.Status ==
                DesktopOcrResolutionStatus.Ambiguous ||
            template?.Ambiguous == true)
        {
            if (ocrEvidence is null &&
                templateEvidence is null)
            {
                return new(
                    LocalVisualTargetStatus.Ambiguous,
                    0,
                    0,
                    0,
                    0,
                    0,
                    evidence,
                    "Local visual evidence đang mơ hồ; từ chối đoán target.");
            }
        }

        if (ocrEvidence is not null &&
            templateEvidence is not null)
        {
            var iou =
                IntersectionOverUnion(
                    ocrEvidence,
                    templateEvidence);

            if (iou < MinimumAgreementIou &&
                ocrEvidence.Confidence >=
                    StrongEvidenceConfidence &&
                templateEvidence.Confidence >=
                    StrongEvidenceConfidence)
            {
                return new(
                    LocalVisualTargetStatus.Ambiguous,
                    0,
                    0,
                    0,
                    0,
                    0,
                    evidence,
                    $"OCR và OpenCV đều mạnh nhưng không cùng vùng (IoU={iou:0.000}); từ chối click.");
            }

            if (iou >=
                MinimumAgreementIou)
            {
                var fused =
                    FuseBoxes(
                        ocrEvidence,
                        templateEvidence);

                var confidence =
                    Math.Clamp(
                        0.58 *
                            Math.Max(
                                ocrEvidence.Confidence,
                                templateEvidence.Confidence) +
                        0.32 *
                            Math.Min(
                                ocrEvidence.Confidence,
                                templateEvidence.Confidence) +
                        0.10 *
                            Math.Clamp(
                                iou,
                                0,
                                1),
                        0,
                        0.99);

                return new(
                    confidence >=
                        MinimumResolvedConfidence
                        ? LocalVisualTargetStatus.Resolved
                        : LocalVisualTargetStatus.NotFound,
                    confidence,
                    fused.Left,
                    fused.Top,
                    fused.Width,
                    fused.Height,
                    evidence,
                    $"OCR + OpenCV đồng thuận cùng vùng (IoU={iou:0.000}); confidence fusion={confidence:0.000}.");
            }
        }

        var single =
            evidence
                .OrderByDescending(item =>
                    item.Confidence)
                .FirstOrDefault();

        if (single is not null &&
            single.Confidence >=
                MinimumResolvedConfidence)
        {
            var visualPenalty =
                visualObservation is not null &&
                visualObservation.Comparable &&
                !visualObservation.MeaningfulVisualChange
                    ? 0.02
                    : 0.0;

            var confidence =
                Math.Max(
                    0,
                    single.Confidence -
                    visualPenalty);

            if (confidence >=
                MinimumResolvedConfidence)
            {
                return new(
                    LocalVisualTargetStatus.Resolved,
                    confidence,
                    single.Left,
                    single.Top,
                    single.Width,
                    single.Height,
                    evidence,
                    $"Local visual resolver dùng bằng chứng mạnh nhất từ {single.Source}; confidence={confidence:0.000}.");
            }
        }

        if (evidence.Count > 0)
        {
            return new(
                LocalVisualTargetStatus.NotFound,
                evidence.Max(item =>
                    item.Confidence),
                0,
                0,
                0,
                0,
                evidence,
                "Có local visual evidence nhưng chưa đủ confidence để xác định target an toàn.");
        }

        var unavailable =
            ocr is null &&
            template is null;

        return new(
            unavailable
                ? LocalVisualTargetStatus.Unavailable
                : LocalVisualTargetStatus.NotFound,
            0,
            0,
            0,
            0,
            0,
            evidence,
            unavailable
                ? "Không có local visual evidence provider."
                : "Không tìm thấy local visual target phù hợp.");
    }

    internal static double IntersectionOverUnion(
        LocalVisualTargetEvidence left,
        LocalVisualTargetEvidence right)
    {
        var x1 =
            Math.Max(
                left.Left,
                right.Left);
        var y1 =
            Math.Max(
                left.Top,
                right.Top);
        var x2 =
            Math.Min(
                left.Left + left.Width,
                right.Left + right.Width);
        var y2 =
            Math.Min(
                left.Top + left.Height,
                right.Top + right.Height);

        var intersectionWidth =
            Math.Max(
                0,
                x2 - x1);
        var intersectionHeight =
            Math.Max(
                0,
                y2 - y1);

        var intersection =
            (double)intersectionWidth *
            intersectionHeight;

        var union =
            (double)left.Width *
            left.Height +
            (double)right.Width *
            right.Height -
            intersection;

        if (union <= 0)
            return 0;

        return Math.Clamp(
            intersection /
            union,
            0,
            1);
    }

    private static LocalVisualTargetEvidence FuseBoxes(
        LocalVisualTargetEvidence left,
        LocalVisualTargetEvidence right)
    {
        var leftWeight =
            Math.Max(
                0.01,
                left.Confidence);
        var rightWeight =
            Math.Max(
                0.01,
                right.Confidence);
        var total =
            leftWeight +
            rightWeight;

        var fusedLeft =
            (int)Math.Round(
                (left.Left * leftWeight +
                 right.Left * rightWeight) /
                total);
        var fusedTop =
            (int)Math.Round(
                (left.Top * leftWeight +
                 right.Top * rightWeight) /
                total);
        var fusedWidth =
            Math.Max(
                2,
                (int)Math.Round(
                    (left.Width * leftWeight +
                     right.Width * rightWeight) /
                    total));
        var fusedHeight =
            Math.Max(
                2,
                (int)Math.Round(
                    (left.Height * leftWeight +
                     right.Height * rightWeight) /
                    total));

        return new(
            "fusion",
            Math.Max(
                left.Confidence,
                right.Confidence),
            fusedLeft,
            fusedTop,
            fusedWidth,
            fusedHeight,
            "Weighted OCR/OpenCV box fusion.");
    }

    private static double ScoreToConfidence(
        int score) =>
        score >= 120
            ? 0.95
            : score >= 115
                ? 0.93
                : score >= 110
                    ? 0.90
                    : score >= 95
                        ? 0.84
                        : 0.78;
}
