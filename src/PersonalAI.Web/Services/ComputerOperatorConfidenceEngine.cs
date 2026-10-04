using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public enum ComputerOperatorConfidenceDecision
{
    Execute,
    Reobserve,
    GeminiFallback
}

public sealed record ComputerOperatorConfidenceAssessment(
    double SceneConfidence,
    double TargetConfidence,
    double ActionConfidence,
    double VerificationConfidence,
    double OverallConfidence,
    ComputerOperatorConfidenceDecision Decision,
    string Reason);

public interface IComputerOperatorConfidenceEngine
{
    ComputerOperatorConfidenceAssessment AssessBeforeExecution(
        DesktopOperatorDecision decision,
        IReadOnlyList<DesktopSceneElement> scene,
        DesktopTargetTrackingResult? tracking);

    ComputerOperatorConfidenceAssessment AssessVerification(
        DesktopOperatorDecision decision,
        DesktopFastObservation? observation,
        double verificationConfidence,
        bool semanticVerificationUsed);
}

public sealed class ComputerOperatorConfidenceEngine
    : IComputerOperatorConfidenceEngine
{
    private const double ExecuteThreshold = 0.78;
    private const double ReobserveThreshold = 0.62;

    public ComputerOperatorConfidenceAssessment AssessBeforeExecution(
        DesktopOperatorDecision decision,
        IReadOnlyList<DesktopSceneElement> scene,
        DesktopTargetTrackingResult? tracking)
    {
        ArgumentNullException.ThrowIfNull(decision);
        scene ??= Array.Empty<DesktopSceneElement>();

        var sceneConfidence = ComputeSceneConfidence(
            decision,
            scene);

        var targetConfidence = ComputeTargetConfidence(
            decision,
            scene,
            tracking);

        var actionConfidence = ComputeActionConfidence(
            decision);

        var overall = Weighted(
            sceneConfidence,
            targetConfidence,
            actionConfidence,
            verification: 1.0);

        var route = overall >= ExecuteThreshold
            ? ComputerOperatorConfidenceDecision.Execute
            : overall >= ReobserveThreshold
                ? ComputerOperatorConfidenceDecision.GeminiFallback
                : ComputerOperatorConfidenceDecision.Reobserve;

        var reason =
            $"scene={sceneConfidence:0.00}; target={targetConfidence:0.00}; " +
            $"action={actionConfidence:0.00}; overall={overall:0.00}; decision={route}";

        return new(
            sceneConfidence,
            targetConfidence,
            actionConfidence,
            1.0,
            overall,
            route,
            reason);
    }

    public ComputerOperatorConfidenceAssessment AssessVerification(
        DesktopOperatorDecision decision,
        DesktopFastObservation? observation,
        double verificationConfidence,
        bool semanticVerificationUsed)
    {
        ArgumentNullException.ThrowIfNull(decision);

        var sceneConfidence = observation is null
            ? 0.72
            : observation.TargetLikelyOccluded ||
              observation.TargetMissing
                ? 0.35
                : observation.ForegroundWindowChanged ||
                  observation.MonitorChanged ||
                  observation.DpiChanged
                    ? 0.62
                    : 0.90;

        var targetConfidence = observation is null
            ? 0.72
            : observation.TargetMissing
                ? 0.20
                : observation.TargetMoved
                    ? 0.70
                    : 0.90;

        var actionConfidence = Clamp(
            decision.Confidence);

        var verification = Clamp(
            verificationConfidence);

        var overall = Weighted(
            sceneConfidence,
            targetConfidence,
            actionConfidence,
            verification);

        var route = overall >= ExecuteThreshold
            ? ComputerOperatorConfidenceDecision.Execute
            : semanticVerificationUsed ||
              overall >= ReobserveThreshold
                ? ComputerOperatorConfidenceDecision.GeminiFallback
                : ComputerOperatorConfidenceDecision.Reobserve;

        return new(
            sceneConfidence,
            targetConfidence,
            actionConfidence,
            verification,
            overall,
            route,
            $"scene={sceneConfidence:0.00}; target={targetConfidence:0.00}; " +
            $"action={actionConfidence:0.00}; verification={verification:0.00}; " +
            $"overall={overall:0.00}; semantic={semanticVerificationUsed}; decision={route}");
    }

    private static double ComputeSceneConfidence(
        DesktopOperatorDecision decision,
        IReadOnlyList<DesktopSceneElement> scene)
    {
        if (scene.Count == 0)
            return Clamp(
                decision.Confidence * 0.82);

        var confident = scene
            .Where(item =>
                double.IsFinite(item.Confidence))
            .Select(item =>
                Clamp(item.Confidence))
            .OrderByDescending(item => item)
            .Take(8)
            .ToArray();

        if (confident.Length == 0)
            return Clamp(
                decision.Confidence * 0.82);

        var average = confident.Average();

        return Clamp(
            average * 0.70 +
            Clamp(decision.Confidence) * 0.30);
    }

    private static double ComputeTargetConfidence(
        DesktopOperatorDecision decision,
        IReadOnlyList<DesktopSceneElement> scene,
        DesktopTargetTrackingResult? tracking)
    {
        var trackingConfidence = tracking is null
            ? 0.78
            : tracking.SafeToExecute
                ? Clamp(tracking.Confidence)
                : 0.0;

        DesktopSceneElement? target = null;

        if (!string.IsNullOrWhiteSpace(
                decision.TargetElementId))
        {
            target = scene.FirstOrDefault(item =>
                item.Id.Equals(
                    decision.TargetElementId,
                    StringComparison.OrdinalIgnoreCase));
        }

        if (target is null &&
            !string.IsNullOrWhiteSpace(
                decision.TargetLabel))
        {
            target = scene
                .Where(item =>
                    !string.IsNullOrWhiteSpace(item.Label))
                .OrderByDescending(item =>
                    LabelSimilarity(
                        decision.TargetLabel,
                        item.Label))
                .FirstOrDefault();
        }

        var sceneTargetConfidence = target is null
            ? HasValidTargetGeometry(decision)
                ? 0.76
                : 0.50
            : Clamp(target.Confidence);

        return Clamp(
            sceneTargetConfidence * 0.65 +
            trackingConfidence * 0.35);
    }

    private static double ComputeActionConfidence(
        DesktopOperatorDecision decision)
    {
        var confidence = Clamp(
            decision.Confidence);

        if (RequiresTarget(decision.Action) &&
            !HasValidTargetGeometry(decision))
        {
            confidence *= 0.70;
        }

        if (RequiresExpectedEffect(decision.Action) &&
            string.IsNullOrWhiteSpace(
                decision.ExpectedEffect))
        {
            confidence *= 0.65;
        }

        return Clamp(confidence);
    }

    private static bool HasValidTargetGeometry(
        DesktopOperatorDecision decision) =>
        decision.BoxWidth > 1 &&
        decision.BoxHeight > 1 ||
        decision.BoxNormalizedWidth > 0 &&
        decision.BoxNormalizedHeight > 0;

    private static bool RequiresTarget(
        string action) =>
        (action ?? string.Empty)
            .Trim()
            .ToLowerInvariant() is
            "click-left" or
            "double-click-left" or
            "click-right" or
            "drag-left";

    private static bool RequiresExpectedEffect(
        string action) =>
        (action ?? string.Empty)
            .Trim()
            .ToLowerInvariant() is not
            "complete" and not
            "blocked" and not
            "wait" and not
            "move-pointer";

    private static double LabelSimilarity(
        string left,
        string right)
    {
        left = Normalize(left);
        right = Normalize(right);

        if (left.Length == 0 ||
            right.Length == 0)
            return 0;

        if (left.Equals(
                right,
                StringComparison.OrdinalIgnoreCase))
            return 1.0;

        if (left.Contains(
                right,
                StringComparison.OrdinalIgnoreCase) ||
            right.Contains(
                left,
                StringComparison.OrdinalIgnoreCase))
            return 0.90;

        return 0.0;
    }

    private static string Normalize(
        string value) =>
        (value ?? string.Empty).Trim();

    private static double Weighted(
        double scene,
        double target,
        double action,
        double verification) =>
        Clamp(
            scene * 0.20 +
            target * 0.30 +
            action * 0.25 +
            verification * 0.25);

    private static double Clamp(
        double value) =>
        !double.IsFinite(value)
            ? 0
            : Math.Clamp(value, 0.0, 1.0);
}
