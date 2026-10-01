using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public interface IDevelopmentMergePolicyService
{
    DevelopmentMergePolicyStatus GetStatus();
    IReadOnlyList<DevelopmentMergePolicyReport> GetAll();
    DevelopmentMergePolicyReport? Get(Guid id);
    Task<DevelopmentMergePolicyReport> EvaluateAndMergeAsync(
        RunDevelopmentMergePolicyRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class DevelopmentMergePolicyService(
    IDevelopmentRunService runs,
    IDevelopmentReviewService reviews,
    IDevelopmentSecurityReportStore security,
    IDevelopmentBenchmarkReportStore benchmarks,
    IDevelopmentGitHubReportStore github,
    ICiMonitorService ci,
    IGitCredentialService credentials,
    IDevelopmentMergePolicyReportStore store,
    IHttpClientFactory httpClientFactory,
    IWorkspaceContextAccessor workspace,
    IConfiguration configuration,
    IAuditRecorder audit) : IDevelopmentMergePolicyService
{
    private readonly string _apiBase =
        (configuration["Development:GitHubApiBaseUrl"] ?? "https://api.github.com")
        .TrimEnd('/');

    public DevelopmentMergePolicyStatus GetStatus()
    {
        var all = store.GetAll();
        return new(
            PersonalAiRelease.Version,
            workspace.CurrentWorkspaceId,
            all.Count,
            all.Count(x => x.Merged),
            LowRiskAutoMergeEnabled: true,
            HighRiskAutoMergeAllowed: false,
            HighRiskRequiresUserConfirmation: true,
            ["ci", "review", "security", "benchmark", "pull-request"]);
    }

    public IReadOnlyList<DevelopmentMergePolicyReport> GetAll() =>
        store.GetAll();

    public DevelopmentMergePolicyReport? Get(Guid id) =>
        store.Get(id);

    public async Task<DevelopmentMergePolicyReport> EvaluateAndMergeAsync(
        RunDevelopmentMergePolicyRequest request,
        CancellationToken cancellationToken = default)
    {
        var run = runs.Get(request.DevelopmentRunId)
            ?? throw new KeyNotFoundException("Không tìm thấy DevelopmentRun.");

        if (run.WorkspaceId != workspace.CurrentWorkspaceId ||
            run.Status != "active" ||
            run.Stage != DevelopmentRunStages.MergePolicy)
            throw new DevelopmentMergePolicyValidationException(
                "Merge policy chỉ chạy khi DevelopmentRun active ở stage merge-policy.");

        var risk = NormalizeRisk(request.RiskLevel);
        var riskEvidence = NormalizeText(request.RiskEvidence, 3, 2000, "RiskEvidence");

        var review = reviews.Get(request.ReviewReportId)
            ?? throw new KeyNotFoundException("Không tìm thấy review report.");
        var securityReport = security.Get(request.SecurityReportId)
            ?? throw new KeyNotFoundException("Không tìm thấy security report.");
        var benchmark = benchmarks.Get(request.BenchmarkReportId)
            ?? throw new KeyNotFoundException("Không tìm thấy benchmark report.");
        var githubReport = github.Get(request.GitHubReportId)
            ?? throw new KeyNotFoundException("Không tìm thấy GitHub report.");
        var ciRun = ci.Get(request.CiRunId)
            ?? throw new KeyNotFoundException("Không tìm thấy CI run.");

        var checks = new[]
        {
            new DevelopmentMergePolicyCheck(
                "review",
                review.DevelopmentRunId == run.Id && review.Approved,
                "Independent review phải approved."),
            new DevelopmentMergePolicyCheck(
                "security",
                securityReport.DevelopmentRunId == run.Id &&
                securityReport.PromotionAllowed &&
                !securityReport.HighRiskFound,
                "Security report phải sạch high-risk."),
            new DevelopmentMergePolicyCheck(
                "benchmark",
                benchmark.DevelopmentRunId == run.Id && benchmark.Passed,
                "Benchmark report phải pass."),
            new DevelopmentMergePolicyCheck(
                "ci",
                ciRun.State == CiMonitorStates.Completed &&
                string.Equals(ciRun.Conclusion, "success", StringComparison.OrdinalIgnoreCase),
                "CI phải completed success."),
            new DevelopmentMergePolicyCheck(
                "pull-request",
                githubReport.DevelopmentRunId == run.Id &&
                githubReport.Pushed &&
                githubReport.PullRequestCreated &&
                githubReport.Branch.Equals(run.Branch, StringComparison.Ordinal),
                "PR phải thuộc đúng DevelopmentRun và experiment branch.")
        };

        var allPassed = checks.All(x => x.Passed);
        var autoEligible = risk == DevelopmentMergeRiskLevels.Low && allPassed;
        var requiresConfirmation =
            risk != DevelopmentMergeRiskLevels.Low;

        var shouldMerge =
            allPassed &&
            ((autoEligible && request.AllowAutomaticMerge) ||
             (requiresConfirmation && request.ConfirmNonLowRiskMerge));

        string? mergeSha = null;
        var merged = false;

        if (shouldMerge)
        {
            var credential = credentials.Resolve(request.CredentialRef);
            if (credential.Kind != GitCredentialKinds.HttpsToken)
                throw new DevelopmentMergePolicyValidationException(
                    "Merge GitHub yêu cầu https-token credential.");

            mergeSha = await MergePullRequestAsync(
                githubReport.Repository,
                githubReport.PullRequestNumber,
                run.Branch,
                credential.Secret,
                cancellationToken);
            merged = true;
        }

        var decision =
            !allPassed ? "blocked" :
            merged ? "merged" :
            requiresConfirmation ? "requires-user-confirmation" :
            "ready-for-auto-merge";

        var report = new DevelopmentMergePolicyReport(
            Guid.NewGuid(),
            workspace.CurrentWorkspaceId,
            run.Id,
            githubReport.Id,
            risk,
            riskEvidence,
            checks,
            allPassed,
            autoEligible,
            requiresConfirmation,
            MergeAttempted: shouldMerge,
            Merged: merged,
            decision,
            mergeSha,
            DateTimeOffset.UtcNow);

        store.Save(report);

        audit.Record(
            AuditAgents.System,
            "development.merge-policy.evaluate",
            $"development-run:{run.Id:D}",
            $"report:{report.Id:D};risk:{risk};all-pass:{allPassed};auto-eligible:{autoEligible};requires-user:{requiresConfirmation};merged:{merged}",
            merged ? AuditResults.Succeeded :
            allPassed ? AuditResults.Prepared : AuditResults.Failed);

        return report;
    }

    private async Task<string?> MergePullRequestAsync(
        string repository,
        long pullRequestNumber,
        string branch,
        string token,
        CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient("development-github");
        client.DefaultRequestHeaders.UserAgent.ParseAdd("PersonalAI/2.7.9");
        client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

        using var message = new HttpRequestMessage(
            HttpMethod.Put,
            $"{_apiBase}/repos/{repository}/pulls/{pullRequestNumber}/merge");
        message.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", token);
        message.Content = new StringContent(
            JsonSerializer.Serialize(new
            {
                commit_title = $"Merge {branch}",
                merge_method = "squash"
            }),
            Encoding.UTF8,
            "application/json");

        using var response = await client.SendAsync(
            message,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw new DevelopmentMergePolicyValidationException(
                $"GitHub merge API trả HTTP {(int)response.StatusCode}.");

        await using var stream =
            await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document =
            await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        var root = document.RootElement;
        if (!root.TryGetProperty("merged", out var mergedElement) ||
            mergedElement.ValueKind != JsonValueKind.True)
            throw new DevelopmentMergePolicyValidationException(
                "GitHub báo Pull Request chưa được merge.");

        return root.TryGetProperty("sha", out var shaElement) &&
               shaElement.ValueKind == JsonValueKind.String
            ? shaElement.GetString()
            : null;
    }

    private static string NormalizeRisk(string? value)
    {
        var risk = (value ?? string.Empty).Trim().ToLowerInvariant();
        if (risk is not (
            DevelopmentMergeRiskLevels.Low or
            DevelopmentMergeRiskLevels.Medium or
            DevelopmentMergeRiskLevels.High))
            throw new DevelopmentMergePolicyValidationException(
                "RiskLevel phải là low, medium hoặc high.");
        return risk;
    }

    private static string NormalizeText(
        string? value,
        int minimum,
        int maximum,
        string name)
    {
        var text = (value ?? string.Empty).Trim();
        if (text.Length < minimum || text.Length > maximum)
            throw new DevelopmentMergePolicyValidationException(
                $"{name} không hợp lệ.");
        return text;
    }
}
