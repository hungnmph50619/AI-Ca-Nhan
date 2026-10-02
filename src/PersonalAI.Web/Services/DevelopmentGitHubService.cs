using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public interface IDevelopmentGitHubService
{
    DevelopmentGitHubStatus GetStatus();
    IReadOnlyList<DevelopmentGitHubReport> GetAll();
    DevelopmentGitHubReport? Get(Guid id);
    Task<DevelopmentGitHubReport> PushAndCreatePullRequestAsync(
        RunDevelopmentGitHubRequest request,
        CancellationToken cancellationToken = default);
    DevelopmentGitHubCiRefreshResult RefreshCi(Guid reportId);
    CiRepairDecision RequestRepair(
        Guid reportId,
        DevelopmentGitHubCiRepairRequest request);
}

public sealed class DevelopmentGitHubService(
    IDevelopmentRunService runs,
    IDevelopmentWorktreeService worktrees,
    ILocalGitRepositoryService git,
    IGitCredentialService credentials,
    ICiMonitorService ci,
    IDevelopmentGitHubReportStore store,
    IHttpClientFactory httpClientFactory,
    IWorkspaceContextAccessor workspace,
    IConfiguration configuration,
    IAuditRecorder audit) : IDevelopmentGitHubService
{
    private readonly string _apiBase = ResolveApiBase(configuration);
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    public DevelopmentGitHubStatus GetStatus() =>
        new(
            PersonalAiRelease.Version,
            workspace.CurrentWorkspaceId,
            store.GetAll().Count,
            DirectMainPushAllowed: false,
            PullRequestRequired: true,
            CiMonitorService.MaximumRepairAttempts,
            CiFailureExcerptReadable: true,
            FullGitHubLogDownloadEnabled: false);

    public IReadOnlyList<DevelopmentGitHubReport> GetAll() =>
        store.GetAll();

    public DevelopmentGitHubReport? Get(Guid id) =>
        store.Get(id);

    public async Task<DevelopmentGitHubReport> PushAndCreatePullRequestAsync(
        RunDevelopmentGitHubRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!request.ConfirmPush || !request.ConfirmCreatePullRequest)
            throw new DevelopmentGitHubValidationException(
                "Cần xác nhận riêng cho push và tạo Pull Request.");

        var run = runs.Get(request.DevelopmentRunId)
            ?? throw new KeyNotFoundException("Không tìm thấy DevelopmentRun.");

        if (run.WorkspaceId != workspace.CurrentWorkspaceId)
            throw new DevelopmentGitHubValidationException(
                "DevelopmentRun không thuộc workspace hiện tại.");

        if (run.Status != "active" || run.Stage != DevelopmentRunStages.Push)
            throw new DevelopmentGitHubValidationException(
                "GitHub automation chỉ chạy khi DevelopmentRun đang ở stage push.");

        if (!run.Branch.StartsWith("experiment/", StringComparison.Ordinal) ||
            run.Branch.Equals("main", StringComparison.OrdinalIgnoreCase) ||
            run.Branch.Equals("master", StringComparison.OrdinalIgnoreCase))
        {
            throw new DevelopmentGitHubValidationException(
                "Chỉ experiment/* branch được push/tạo PR.");
        }

        var repository = NormalizeRepository(request.Repository);
        var baseBranch = NormalizeBaseBranch(request.BaseBranch);
        var remote = NormalizeRemote(request.Remote);
        var title = NormalizeText(request.Title, 3, 240, "Title");
        var body = NormalizeText(request.Body, 0, 8000, "Body");

        var credential = credentials.Resolve(request.CredentialRef);
        if (credential.Kind != GitCredentialKinds.HttpsToken)
            throw new DevelopmentGitHubValidationException(
                "GitHub API v2.7.8 yêu cầu https-token credential.");

        var allWorktrees = await worktrees.GetAllAsync(
            run.RepositoryPath,
            cancellationToken);

        var worktree = allWorktrees.FirstOrDefault(x =>
            !x.IsMainWorktree &&
            x.Branch.Equals(run.Branch, StringComparison.Ordinal));

        if (worktree is null)
            throw new DevelopmentGitHubValidationException(
                "Không tìm thấy isolated worktree cho experiment branch.");

        var push = await git.PushAsync(
            new LocalGitPushRequest(
                worktree.WorktreePath,
                remote,
                run.Branch,
                ConfirmGitWrite: true,
                request.CredentialRef),
            cancellationToken);

        if (!push.Succeeded)
            throw new DevelopmentGitHubValidationException(
                "Push experiment branch thất bại.");

        var pullRequest = await FindOrCreatePullRequestAsync(
            repository,
            run.Branch,
            baseBranch,
            title,
            body,
            credential.Secret,
            cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var report = new DevelopmentGitHubReport(
            Guid.NewGuid(),
            workspace.CurrentWorkspaceId,
            run.Id,
            repository,
            run.Branch,
            baseBranch,
            worktree.WorktreePath,
            remote,
            Pushed: true,
            PullRequestCreated: true,
            pullRequest.Number,
            pullRequest.Url,
            CiRunId: null,
            CiState: null,
            CiConclusion: null,
            CiRepairAttempts: 0,
            MaximumCiRepairAttempts: CiMonitorService.MaximumRepairAttempts,
            now,
            now);

        store.Save(report);

        audit.Record(
            AuditAgents.System,
            "development.github.push-pr",
            $"development-run:{run.Id:D}",
            $"report:{report.Id:D};repository:{repository};branch:{run.Branch};base:{baseBranch};pr:{pullRequest.Number};secret:redacted",
            AuditResults.Succeeded);

        return report;
    }

    public DevelopmentGitHubCiRefreshResult RefreshCi(Guid reportId)
    {
        var report = store.Get(reportId)
            ?? throw new KeyNotFoundException("Không tìm thấy DevelopmentGitHubReport.");

        var candidates = ci.RefreshFromEvents()
            .Where(x =>
                x.Repository.Equals(
                    report.Repository,
                    StringComparison.OrdinalIgnoreCase) &&
                string.Equals(
                    x.Ref,
                    report.Branch,
                    StringComparison.Ordinal))
            .OrderByDescending(x => x.UpdatedAt)
            .ToArray();

        var ciRun = candidates.FirstOrDefault();
        if (ciRun is null)
        {
            return new(
                report,
                FailureLogExcerpt: null,
                FailureLogTruncated: false);
        }

        var updated = report with
        {
            CiRunId = ciRun.Id,
            CiState = ciRun.State,
            CiConclusion = ciRun.Conclusion,
            CiRepairAttempts = ciRun.RepairAttempts,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        store.Save(updated);

        return new(
            updated,
            ciRun.FailureLogExcerpt,
            ciRun.FailureLogTruncated);
    }

    public CiRepairDecision RequestRepair(
        Guid reportId,
        DevelopmentGitHubCiRepairRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var refreshed = RefreshCi(reportId);
        var report = refreshed.Report;

        if (report.CiRunId is null)
            throw new DevelopmentGitHubValidationException(
                "Chưa tìm thấy CI run cho Pull Request.");

        var decision = ci.RequestRepair(
            new RequestCiRepairRequest(
                report.CiRunId.Value,
                request.Reason));

        var ciRun = ci.Get(report.CiRunId.Value)
            ?? throw new KeyNotFoundException("Không tìm thấy CI run sau repair.");

        store.Save(report with
        {
            CiRepairAttempts = ciRun.RepairAttempts,
            CiState = ciRun.State,
            CiConclusion = ciRun.Conclusion,
            UpdatedAt = DateTimeOffset.UtcNow
        });

        audit.Record(
            AuditAgents.System,
            "development.github.ci-repair",
            $"development-run:{report.DevelopmentRunId:D}",
            $"report:{report.Id:D};ci-run:{ciRun.Id:D};attempt:{decision.Attempt}/{decision.MaximumAttempts}",
            AuditResults.Prepared);

        return decision;
    }

    private async Task<PullRequestInfo> FindOrCreatePullRequestAsync(
        string repository,
        string head,
        string baseBranch,
        string title,
        string body,
        string token,
        CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient("development-github");
        client.DefaultRequestHeaders.UserAgent.ParseAdd("PersonalAI/2.7.8");
        client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue(
                "application/vnd.github+json"));

        var owner = repository.Split('/')[0];
        var listUri =
            $"{_apiBase}/repos/{repository}/pulls?state=open&head={Uri.EscapeDataString(owner + ":" + head)}&base={Uri.EscapeDataString(baseBranch)}";

        using (var list = new HttpRequestMessage(HttpMethod.Get, listUri))
        {
            list.Headers.Authorization =
                new AuthenticationHeaderValue("Bearer", token);
            using var response = await client.SendAsync(
                list,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                await using var stream = await response.Content.ReadAsStreamAsync(
                    cancellationToken);
                using var document = await JsonDocument.ParseAsync(
                    stream,
                    cancellationToken: cancellationToken);

                if (document.RootElement.ValueKind == JsonValueKind.Array &&
                    document.RootElement.GetArrayLength() > 0)
                {
                    return ParsePullRequest(document.RootElement[0]);
                }
            }
        }

        var payload = JsonSerializer.Serialize(
            new
            {
                title,
                head,
                @base = baseBranch,
                body
            },
            JsonOptions);

        using var create = new HttpRequestMessage(
            HttpMethod.Post,
            $"{_apiBase}/repos/{repository}/pulls");
        create.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", token);
        create.Content = new StringContent(
            payload,
            Encoding.UTF8,
            "application/json");

        using var created = await client.SendAsync(
            create,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        if (!created.IsSuccessStatusCode)
            throw new DevelopmentGitHubValidationException(
                $"GitHub Pull Request API trả HTTP {(int)created.StatusCode}.");

        await using var createdStream =
            await created.Content.ReadAsStreamAsync(cancellationToken);
        using var createdDocument = await JsonDocument.ParseAsync(
            createdStream,
            cancellationToken: cancellationToken);

        return ParsePullRequest(createdDocument.RootElement);
    }

    private static PullRequestInfo ParsePullRequest(JsonElement element)
    {
        if (!element.TryGetProperty("number", out var numberElement) ||
            !numberElement.TryGetInt64(out var number) ||
            !element.TryGetProperty("html_url", out var urlElement) ||
            urlElement.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(urlElement.GetString()))
        {
            throw new DevelopmentGitHubValidationException(
                "GitHub Pull Request response thiếu number/html_url.");
        }

        return new(number, urlElement.GetString()!);
    }

    private static string ResolveApiBase(IConfiguration configuration)
    {
        var value = configuration["Development:GitHubApiBaseUrl"];
        if (string.IsNullOrWhiteSpace(value))
            return "https://api.github.com";

        if (!Uri.TryCreate(value.TrimEnd('/'), UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https"))
        {
            throw new DevelopmentGitHubValidationException(
                "Development:GitHubApiBaseUrl không hợp lệ.");
        }

        return uri.ToString().TrimEnd('/');
    }

    private static string NormalizeRepository(string? value)
    {
        var repository = (value ?? string.Empty).Trim();
        var parts = repository.Split('/');
        if (parts.Length != 2 ||
            parts.Any(x =>
                x.Length is < 1 or > 100 ||
                x.Any(c =>
                    !(char.IsLetterOrDigit(c) ||
                      c is '-' or '_' or '.'))))
        {
            throw new DevelopmentGitHubValidationException(
                "Repository phải có dạng owner/name.");
        }
        return repository;
    }

    private static string NormalizeBaseBranch(string? value)
    {
        var branch = (value ?? string.Empty).Trim();
        if (branch is not ("main" or "master"))
            throw new DevelopmentGitHubValidationException(
                "BaseBranch của automated PR phải là main hoặc master.");
        return branch;
    }

    private static string NormalizeRemote(string? value)
    {
        var remote = (value ?? string.Empty).Trim();
        if (remote.Length is < 1 or > 80 ||
            remote.Any(c =>
                !(char.IsLetterOrDigit(c) ||
                  c is '-' or '_' or '.')))
        {
            throw new DevelopmentGitHubValidationException(
                "Remote không hợp lệ.");
        }
        return remote;
    }

    private static string NormalizeText(
        string? value,
        int minimum,
        int maximum,
        string name)
    {
        var text = (value ?? string.Empty).Trim();
        if (text.Length < minimum || text.Length > maximum)
            throw new DevelopmentGitHubValidationException(
                $"{name} không hợp lệ.");
        return text;
    }

    private sealed record PullRequestInfo(long Number, string Url);
}
