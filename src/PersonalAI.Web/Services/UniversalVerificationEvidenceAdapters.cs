using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public interface IUniversalVerificationEvidenceAdapters
{
    UniversalOutcomeEvidence? FromAgent(
        ExecutionAgentResult result);

    UniversalOutcomeEvidence? FromBrowser(
        BrowserExecutionBackendResult result);

    UniversalOutcomeEvidence FromCoding(
        CodingVerificationReport report);

    UniversalOutcomeEvidence? FromDesktop(
        DesktopVerificationRoutingResult result);
}

public sealed class UniversalVerificationEvidenceAdapters
    : IUniversalVerificationEvidenceAdapters
{
    public UniversalOutcomeEvidence? FromAgent(
        ExecutionAgentResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (!result.Status.Equals(
                AgentExecutionStatuses.Succeeded,
                StringComparison.OrdinalIgnoreCase))
        {
            return new(
                result.AgentId,
                Passed: false,
                Confidence: 0.99,
                $"Agent execution failed: {result.Summary}");
        }

        if (!result.Verified)
            return null;

        return new(
            result.AgentId,
            Passed: true,
            Confidence: 0.95,
            result.Summary);
    }

    public UniversalOutcomeEvidence? FromBrowser(
        BrowserExecutionBackendResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (!result.Success)
        {
            return new(
                $"browser:{result.Engine}",
                Passed: false,
                Confidence: 0.98,
                result.Summary);
        }

        if (!result.Verified)
            return null;

        return new(
            $"browser:{result.Engine}",
            Passed: true,
            Confidence: 0.96,
            result.Summary);
    }

    public UniversalOutcomeEvidence FromCoding(
        CodingVerificationReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        var passedSteps =
            report.Steps.Count(step =>
                step.Passed);

        return new(
            "coding-verification-gate",
            report.Passed,
            report.Passed
                ? 0.99
                : 0.98,
            $"{report.Summary} steps={passedSteps}/{report.Steps.Count}");
    }

    public UniversalOutcomeEvidence? FromDesktop(
        DesktopVerificationRoutingResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return result.Route switch
        {
            DesktopVerificationRoute.LocalVerified =>
                new(
                    "desktop-local-verifier",
                    Passed: true,
                    Confidence: Math.Clamp(
                        result.Confidence,
                        0,
                        1),
                    result.Reason),

            DesktopVerificationRoute.LocalFailed =>
                new(
                    "desktop-local-verifier",
                    Passed: false,
                    Confidence: Math.Clamp(
                        result.Confidence,
                        0,
                        1),
                    result.Reason),

            DesktopVerificationRoute.GeminiRequired =>
                null,

            _ =>
                null
        };
    }
}
