namespace PersonalAI.Web.Services;

public enum LocalVisualVerificationStatus
{
    Changed,
    Stable,
    Inconclusive
}

public sealed record LocalVisualVerificationResult(
    LocalVisualVerificationStatus Status,
    double Confidence,
    string Reason);

public interface ILocalVisualVerificationService
{
    LocalVisualVerificationResult Evaluate(
        DesktopLocalVisualObservation? observation);
}

public sealed class LocalVisualVerificationService
    : ILocalVisualVerificationService
{
    public LocalVisualVerificationResult Evaluate(
        DesktopLocalVisualObservation? observation)
    {
        if (observation is null)
        {
            return Inconclusive(
                "Không có local visual observation.");
        }

        var region =
            observation.RegionDelta;

        if (observation.Comparable)
        {
            if (observation.HashDistance >= 8 &&
                region?.Comparable == true &&
                region.ChangedRatio >= 0.01)
            {
                return new(
                    LocalVisualVerificationStatus.Changed,
                    0.96,
                    $"dHash + region delta cùng xác nhận UI thay đổi: hash={observation.HashDistance}/64; region={region.ChangedRatio * 100:0.00}%.");
            }

            if (observation.HashDistance >= 6 ||
                region?.HasMeaningfulChange == true)
            {
                return new(
                    LocalVisualVerificationStatus.Changed,
                    0.88,
                    $"Ít nhất một local visual sensor phát hiện thay đổi: hash={observation.HashDistance}/64; region={(region?.ChangedRatio ?? 0) * 100:0.00}%.");
            }

            if (observation.HashDistance <= 2 &&
                region?.Comparable == true &&
                region.ChangedRatio <= 0.003)
            {
                return new(
                    LocalVisualVerificationStatus.Stable,
                    0.94,
                    $"dHash + region delta cùng xác nhận frame gần như ổn định: hash={observation.HashDistance}/64; region={region.ChangedRatio * 100:0.00}%.");
            }

            return Inconclusive(
                $"Local visual signals chưa đồng thuận đủ mạnh: hash={observation.HashDistance}/64; region={(region?.ChangedRatio ?? -1) * 100:0.00}%.");
        }

        if (region?.Comparable == true)
        {
            if (region.HasMeaningfulChange)
            {
                return new(
                    LocalVisualVerificationStatus.Changed,
                    0.80,
                    $"Perceptual hash unavailable nhưng region delta phát hiện thay đổi {region.ChangedRatio * 100:0.00}%.");
            }

            if (region.ChangedRatio <= 0.003)
            {
                return new(
                    LocalVisualVerificationStatus.Stable,
                    0.82,
                    $"Perceptual hash unavailable; region delta cho thấy frame ổn định {region.ChangedRatio * 100:0.00}%.");
            }
        }

        return Inconclusive(
            observation.Reason);
    }

    private static LocalVisualVerificationResult Inconclusive(
        string reason) =>
        new(
            LocalVisualVerificationStatus.Inconclusive,
            0,
            reason);
}
