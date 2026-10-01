using System.Text.Json;
using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public interface INightlyImprovementService
{
    NightlyImprovementStatus GetStatus();
    IReadOnlyList<NightlyImprovementRunReport> GetReports();
    Task<NightlyImprovementRunReport> RunAsync(
        RunNightlyImprovementRequest request,
        CancellationToken cancellationToken = default);
    Task<NightlyImprovementRunReport?> RunScheduledAsync(
        CancellationToken cancellationToken = default);
}

public sealed class NightlyImprovementService(
    IImprovementBacklogService backlog,
    IDevelopmentRunService runs,
    IWorkspaceContextAccessor workspace,
    IConfiguration configuration,
    IAuditRecorder audit) : INightlyImprovementService
{
    public const int HardMaximumItemsPerNight = 3;
    public const int HardMaximumRuntimeMinutes = 240;

    private readonly object _gate = new();
    private readonly string _root = ResolveRoot(configuration);
    private static readonly JsonSerializerOptions Options =
        new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public NightlyImprovementStatus GetStatus()
    {
        var reports = GetReports();
        var last = reports.OrderByDescending(x => x.CompletedAt).FirstOrDefault();

        return new(
            PersonalAiRelease.Version,
            workspace.CurrentWorkspaceId,
            SchedulerEnabled: GetBool("Development:Nightly:Enabled", false),
            ScheduleHourUtc: Math.Clamp(GetInt("Development:Nightly:HourUtc", 2), 0, 23),
            ScheduleMinuteUtc: Math.Clamp(GetInt("Development:Nightly:MinuteUtc", 0), 0, 59),
            MaximumItemsPerNight: Math.Clamp(
                GetInt("Development:Nightly:MaximumItems", 1),
                1,
                HardMaximumItemsPerNight),
            MaximumRuntimeMinutes: Math.Clamp(
                GetInt("Development:Nightly:MaximumRuntimeMinutes", 60),
                1,
                HardMaximumRuntimeMinutes),
            LowRiskOnly: true,
            OneActiveNightlyRunAtATime: true,
            ReportsPersisted: true,
            DownstreamPullRequestUsesPolicy: true,
            DownstreamAutoMergeUsesMergePolicy: true,
            LastRunAt: last?.CompletedAt,
            LastStopReason: last?.StopReason);
    }

    public IReadOnlyList<NightlyImprovementRunReport> GetReports()
    {
        lock (_gate)
            return Load()
                .OrderByDescending(x => x.CompletedAt)
                .ToArray();
    }

    public Task<NightlyImprovementRunReport> RunAsync(
        RunNightlyImprovementRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!request.ConfirmRun)
            throw new NightlyImprovementValidationException(
                "Cần ConfirmRun=true để chạy nightly improvement thủ công.");

        return RunCoreAsync(
            request,
            scheduled: false,
            cancellationToken);
    }

    public async Task<NightlyImprovementRunReport?> RunScheduledAsync(
        CancellationToken cancellationToken = default)
    {
        if (!GetBool("Development:Nightly:Enabled", false))
            return null;

        var repositoryPath =
            configuration["Development:Nightly:RepositoryPath"]?.Trim() ?? string.Empty;
        var baseBranch =
            configuration["Development:Nightly:BaseBranch"]?.Trim() ?? "main";
        var branchPrefix =
            configuration["Development:Nightly:BranchPrefix"]?.Trim() ??
            "experiment/nightly";

        if (string.IsNullOrWhiteSpace(repositoryPath) &&
            string.IsNullOrWhiteSpace(
                configuration["Development:Nightly:RepositoryPath"]))
        {
            return null;
        }

        var request = new RunNightlyImprovementRequest(
            repositoryPath,
            baseBranch,
            Math.Clamp(
                GetInt("Development:Nightly:MaximumItems", 1),
                1,
                HardMaximumItemsPerNight),
            Math.Clamp(
                GetInt("Development:Nightly:MaximumRuntimeMinutes", 60),
                1,
                HardMaximumRuntimeMinutes),
            branchPrefix,
            ConfirmRun: true);

        return await RunCoreAsync(
            request,
            scheduled: true,
            cancellationToken);
    }

    private async Task<NightlyImprovementRunReport> RunCoreAsync(
        RunNightlyImprovementRequest request,
        bool scheduled,
        CancellationToken cancellationToken)
    {
        var repository = NormalizeRepositoryPath(request.RepositoryPath);
        var baseBranch = NormalizeBaseBranch(request.BaseBranch);
        var branchPrefix = NormalizeBranchPrefix(request.BranchPrefix);

        if (request.MaximumItems is < 1 or > HardMaximumItemsPerNight)
            throw new NightlyImprovementValidationException(
                $"MaximumItems phải từ 1 đến {HardMaximumItemsPerNight}.");

        if (request.MaximumRuntimeMinutes is < 1 or > HardMaximumRuntimeMinutes)
            throw new NightlyImprovementValidationException(
                $"MaximumRuntimeMinutes phải từ 1 đến {HardMaximumRuntimeMinutes}.");

        var startedAt = DateTimeOffset.UtcNow;
        var deadline = startedAt.AddMinutes(request.MaximumRuntimeMinutes);

        var activeNightly = runs.GetAll()
            .Any(x =>
                x.Status == "active" &&
                string.Equals(
                    x.RoadmapVersion,
                    "nightly-v2.7.14",
                    StringComparison.Ordinal));

        if (activeNightly)
        {
            return Persist(new NightlyImprovementRunReport(
                Guid.NewGuid(),
                workspace.CurrentWorkspaceId,
                startedAt,
                DateTimeOffset.UtcNow,
                request.MaximumItems,
                request.MaximumRuntimeMinutes,
                EligibleItems: 0,
                SelectedItems: 0,
                DevelopmentRunsCreated: 0,
                NightlyImprovementStopReasons.ActiveNightlyRunExists,
                []));
        }

        var activeImprovementIds = runs.GetAll()
            .Where(x => x.Status == "active" && x.ImprovementItemId is not null)
            .Select(x => x.ImprovementItemId!.Value)
            .ToHashSet();

        var eligible = backlog.GetAll()
            .Where(x =>
                x.Priority == ImprovementPriorities.Low &&
                x.State is ImprovementBacklogStates.Open or
                    ImprovementBacklogStates.Planned &&
                !activeImprovementIds.Contains(x.Id))
            .OrderByDescending(x => x.Occurrences)
            .ThenBy(x => x.FirstSeenAt)
            .ToArray();

        if (eligible.Length == 0)
        {
            return Persist(new NightlyImprovementRunReport(
                Guid.NewGuid(),
                workspace.CurrentWorkspaceId,
                startedAt,
                DateTimeOffset.UtcNow,
                request.MaximumItems,
                request.MaximumRuntimeMinutes,
                EligibleItems: 0,
                SelectedItems: 0,
                DevelopmentRunsCreated: 0,
                NightlyImprovementStopReasons.NoEligibleBacklog,
                []));
        }

        var itemResults = new List<NightlyImprovementItemResult>();
        var created = 0;
        var stopReason = NightlyImprovementStopReasons.CompletedBudget;

        foreach (var item in eligible.Take(request.MaximumItems))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (DateTimeOffset.UtcNow >= deadline)
            {
                stopReason = NightlyImprovementStopReasons.RuntimeBudgetReached;
                break;
            }

            var branch =
                $"{branchPrefix}/{startedAt:yyyyMMdd}-{item.Id:N}"[..Math.Min(
                    150,
                    $"{branchPrefix}/{startedAt:yyyyMMdd}-{item.Id:N}".Length)];

            try
            {
                var run = runs.Create(new CreateDevelopmentRunRequest(
                    Goal:
                        $"Nightly low-risk improvement: {item.Title}",
                    RepositoryPath: repository,
                    Branch: branch,
                    RoadmapVersion: "nightly-v2.7.14",
                    ConfirmCreate: true,
                    ImprovementItemId: item.Id,
                    DiagnosisId: null));

                backlog.UpdateState(
                    item.Id,
                    new UpdateImprovementBacklogStateRequest(
                        ImprovementBacklogStates.InProgress,
                        scheduled
                            ? "selected-by-nightly-scheduler-v2.7.14"
                            : "selected-by-nightly-manual-run-v2.7.14"));

                created++;
                itemResults.Add(new(
                    item.Id,
                    item.Title,
                    item.Priority,
                    run.Id,
                    branch,
                    "development-run-created",
                    "Run created at analysis stage; downstream diagnosis/coding/test/review/security/benchmark/PR/merge remain policy-gated."));
            }
            catch (Exception exception) when (
                exception is DevelopmentRunValidationException or
                DevelopmentRunConflictException or
                ImprovementBacklogValidationException or
                KeyNotFoundException)
            {
                itemResults.Add(new(
                    item.Id,
                    item.Title,
                    item.Priority,
                    null,
                    branch,
                    "failed",
                    SafeInline(exception.Message, 500)));
                stopReason = NightlyImprovementStopReasons.RunCreationFailed;
                break;
            }

            await Task.Yield();
        }

        if (created < request.MaximumItems &&
            stopReason == NightlyImprovementStopReasons.CompletedBudget &&
            DateTimeOffset.UtcNow >= deadline)
        {
            stopReason = NightlyImprovementStopReasons.RuntimeBudgetReached;
        }

        var report = new NightlyImprovementRunReport(
            Guid.NewGuid(),
            workspace.CurrentWorkspaceId,
            startedAt,
            DateTimeOffset.UtcNow,
            request.MaximumItems,
            request.MaximumRuntimeMinutes,
            eligible.Length,
            itemResults.Count,
            created,
            stopReason,
            itemResults);

        audit.Record(
            AuditAgents.System,
            "development.nightly-improvement.run",
            $"nightly-report:{report.Id:D}",
            $"scheduled:{scheduled};eligible:{eligible.Length};selected:{itemResults.Count};created:{created};max-items:{request.MaximumItems};max-minutes:{request.MaximumRuntimeMinutes};stop:{stopReason}",
            created > 0
                ? AuditResults.Prepared
                : AuditResults.Succeeded);

        return Persist(report);
    }

    private NightlyImprovementRunReport Persist(
        NightlyImprovementRunReport report)
    {
        lock (_gate)
        {
            var all = Load();
            all.Add(report);
            Save(all);
        }

        return report;
    }

    private List<NightlyImprovementRunReport> Load()
    {
        var path = PathForWorkspace();
        if (!File.Exists(path)) return [];

        try
        {
            return JsonSerializer.Deserialize<List<NightlyImprovementRunReport>>(
                File.ReadAllText(path),
                Options) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private void Save(List<NightlyImprovementRunReport> reports)
    {
        Directory.CreateDirectory(_root);
        var path = PathForWorkspace();
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(reports, Options));
        File.Move(temp, path, true);
    }

    private string PathForWorkspace()
    {
        var safe = string.Concat(workspace.CurrentWorkspaceId.Select(c =>
            char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_'));

        return Path.Combine(
            _root,
            $"nightly-improvement-{safe}.json");
    }

    private int GetInt(string key, int fallback) =>
        int.TryParse(configuration[key], out var value)
            ? value
            : fallback;

    private bool GetBool(string key, bool fallback) =>
        bool.TryParse(configuration[key], out var value)
            ? value
            : fallback;

    private static string NormalizeRepositoryPath(string? value)
    {
        var path = (value ?? string.Empty).Trim().Replace('\\', '/');
        if (path.Length > 300 ||
            Path.IsPathRooted(path) ||
            path.StartsWith("../", StringComparison.Ordinal) ||
            path.Contains("/../", StringComparison.Ordinal))
        {
            throw new NightlyImprovementValidationException(
                "RepositoryPath không hợp lệ.");
        }

        return path;
    }

    private static string NormalizeBaseBranch(string? value)
    {
        var branch = (value ?? string.Empty).Trim();
        if (branch is not ("main" or "master"))
            throw new NightlyImprovementValidationException(
                "BaseBranch phải là main hoặc master.");
        return branch;
    }

    private static string NormalizeBranchPrefix(string? value)
    {
        var prefix = (value ?? string.Empty).Trim().TrimEnd('/');
        if (!prefix.StartsWith("experiment/", StringComparison.Ordinal) ||
            prefix.Length is < 12 or > 100 ||
            prefix.Contains("..", StringComparison.Ordinal) ||
            prefix.Any(c =>
                char.IsControl(c) ||
                char.IsWhiteSpace(c) ||
                c is '~' or '^' or ':' or '?' or '*' or '[' or '\\'))
        {
            throw new NightlyImprovementValidationException(
                "BranchPrefix phải là experiment/* hợp lệ.");
        }

        return prefix;
    }

    private static string SafeInline(string value, int maximum)
    {
        var normalized = value
            .Replace('\r', ' ')
            .Replace('\n', ' ');
        return normalized.Length <= maximum
            ? normalized
            : normalized[..maximum];
    }

    private static string ResolveRoot(IConfiguration configuration)
    {
        var root = configuration["Development:Nightly:ReportRoot"];
        if (string.IsNullOrWhiteSpace(root))
        {
            root = Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData),
                "PersonalAI",
                "Development",
                "Nightly");
        }

        root = Path.GetFullPath(
            Environment.ExpandEnvironmentVariables(root));
        Directory.CreateDirectory(root);
        return root;
    }
}

public sealed class NightlyImprovementBackgroundService(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<NightlyImprovementBackgroundService> logger)
    : BackgroundService
{
    private static readonly TimeSpan PollInterval =
        TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (IsDue(DateTimeOffset.UtcNow))
                {
                    using var scope = scopeFactory.CreateScope();
                    var service = scope.ServiceProvider
                        .GetRequiredService<INightlyImprovementService>();

                    var reports = service.GetReports();
                    var today = DateTimeOffset.UtcNow.UtcDateTime.Date;

                    var alreadyRanToday = reports.Any(x =>
                        x.StartedAt.UtcDateTime.Date == today);

                    if (!alreadyRanToday)
                        await service.RunScheduledAsync(stoppingToken);
                }
            }
            catch (OperationCanceledException)
                when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "Nightly improvement scheduler iteration failed.");
            }

            await Task.Delay(PollInterval, stoppingToken);
        }
    }

    private bool IsDue(DateTimeOffset now)
    {
        if (!bool.TryParse(
                configuration["Development:Nightly:Enabled"],
                out var enabled) ||
            !enabled)
        {
            return false;
        }

        var hour = int.TryParse(
            configuration["Development:Nightly:HourUtc"],
            out var parsedHour)
            ? Math.Clamp(parsedHour, 0, 23)
            : 2;

        var minute = int.TryParse(
            configuration["Development:Nightly:MinuteUtc"],
            out var parsedMinute)
            ? Math.Clamp(parsedMinute, 0, 59)
            : 0;

        return now.Hour == hour &&
               now.Minute >= minute &&
               now.Minute < minute + 1;
    }
}
