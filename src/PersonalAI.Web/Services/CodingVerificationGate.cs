using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public sealed record CodingVerificationRequest(
    string TargetPath,
    string RepositoryPath,
    string Configuration = "Release");

public sealed record CodingVerificationStep(
    string Name,
    bool Passed,
    string Detail);

public sealed record CodingVerificationReport(
    bool Passed,
    IReadOnlyList<CodingVerificationStep> Steps,
    string Summary);

public interface ICodingVerificationGate
{
    Task<CodingVerificationReport> VerifyAsync(
        CodingVerificationRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class CodingVerificationGate(
    IDevelopmentAgentService development)
    : ICodingVerificationGate
{
    public async Task<CodingVerificationReport> VerifyAsync(
        CodingVerificationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.TargetPath))
            throw new AgentValidationException(
                "Coding verification cần TargetPath.");

        if (string.IsNullOrWhiteSpace(request.RepositoryPath))
            throw new AgentValidationException(
                "Coding verification cần RepositoryPath.");

        var steps = new List<CodingVerificationStep>();

        var restore = await development.DotnetRestoreAsync(
            request.TargetPath.Trim(),
            cancellationToken);

        steps.Add(
            new(
                "restore",
                restore.Succeeded && !restore.TimedOut,
                DescribeProcess(restore)));

        if (!steps[^1].Passed)
            return Finish(false, steps, "Restore thất bại.");

        var build = await development.DotnetBuildAsync(
            request.TargetPath.Trim(),
            NormalizeConfiguration(request.Configuration),
            cancellationToken);

        steps.Add(
            new(
                "build",
                build.Succeeded && !build.TimedOut,
                DescribeProcess(build)));

        if (!steps[^1].Passed)
            return Finish(false, steps, "Build thất bại.");

        var test = await development.DotnetTestAsync(
            request.TargetPath.Trim(),
            NormalizeConfiguration(request.Configuration),
            cancellationToken);

        steps.Add(
            new(
                "test",
                test.Succeeded && !test.TimedOut,
                DescribeProcess(test)));

        if (!steps[^1].Passed)
            return Finish(false, steps, "Test thất bại.");

        var diff = await development.GitDiffAsync(
            request.RepositoryPath.Trim(),
            staged: false,
            cancellationToken);

        steps.Add(
            new(
                "diff-review",
                diff.Succeeded,
                $"exitCode={diff.ExitCode}; outputTruncated={diff.OutputTruncated}; diffChars={diff.Output.Length}"));

        return Finish(
            steps.All(step => step.Passed),
            steps,
            steps.All(step => step.Passed)
                ? "Coding verification gate đã PASS."
                : "Coding verification gate chưa đạt.");
    }

    private static CodingVerificationReport Finish(
        bool passed,
        IReadOnlyList<CodingVerificationStep> steps,
        string summary) =>
        new(
            passed,
            steps,
            summary);

    private static string DescribeProcess(
        DevelopmentProcessResult result) =>
        $"tool={result.Tool}; exitCode={result.ExitCode}; timedOut={result.TimedOut}; durationMs={result.DurationMs}; outputTruncated={result.OutputTruncated}";

    private static string NormalizeConfiguration(
        string configuration) =>
        string.IsNullOrWhiteSpace(configuration)
            ? "Release"
            : configuration.Trim();
}
