using System.Text.Json;
using System.Text.RegularExpressions;
using PersonalAI.Web.Evaluation.Contracts;
using PersonalAI.Web.Evaluation.Core;
using PersonalAI.Web.Evaluation.Regression;
using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public interface IDevelopmentAutoTestService
{
    DevelopmentTestStatus GetStatus();
    IReadOnlyList<DevelopmentTestReport> GetAll();
    DevelopmentTestReport? Get(Guid id);
    Task<DevelopmentTestReport> RunAsync(
        RunDevelopmentTestsRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class DevelopmentAutoTestService(
    IDevelopmentRunService runs,
    IDevelopmentWorktreeService worktrees,
    IDevelopmentAgentService development,
    IRegressionDatasetStore regressionStore,
    IEvaluationEngine evaluation,
    IWorkspaceContextAccessor workspace,
    IConfiguration configuration,
    IAuditRecorder audit) : IDevelopmentAutoTestService
{
    public const int MaximumFailureLogCharacters = 32_000;
    public const int MaximumRegressionCases = 100;

    private readonly object _gate = new();
    private readonly string _root = ResolveRoot(configuration);
    private static readonly JsonSerializerOptions Options =
        new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private static readonly Regex SecretPattern = new(
        @"(?ix)
        (?:
          gh[pousr]_[A-Za-z0-9]{20,} |
          github_pat_[A-Za-z0-9_]{20,} |
          sk-[A-Za-z0-9_-]{16,} |
          AKIA[A-Z0-9]{16} |
          authorization\s*:\s*[^\r\n]+ |
          bearer\s+[A-Za-z0-9._~+\-/]+=*
        )",
        RegexOptions.Compiled);

    public DevelopmentTestStatus GetStatus()
    {
        var all = GetAll();
        return new(
            PersonalAiRelease.Version,
            workspace.CurrentWorkspaceId,
            all.Count,
            all.Count(x => x.Succeeded),
            all.Count(x => !x.Succeeded),
            ReportsPersisted: true,
            FailureLogsPersisted: true,
            FailureLogsRedacted: true,
            WorktreeOnly: true,
            [
                DevelopmentTestPhaseNames.Restore,
                DevelopmentTestPhaseNames.Build,
                DevelopmentTestPhaseNames.Unit,
                DevelopmentTestPhaseNames.Integration,
                DevelopmentTestPhaseNames.Regression
            ]);
    }

    public IReadOnlyList<DevelopmentTestReport> GetAll()
    {
        lock (_gate)
            return Load().OrderByDescending(x => x.CompletedAt).ToArray();
    }

    public DevelopmentTestReport? Get(Guid id)
    {
        lock (_gate)
            return Load().FirstOrDefault(x => x.Id == id);
    }

    public async Task<DevelopmentTestReport> RunAsync(
        RunDevelopmentTestsRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!request.ConfirmExecution)
            throw new DevelopmentTestValidationException(
                "Cần ConfirmExecution=true để chạy restore/build/test.");

        if (request.MaximumRegressionCases is < 1 or > MaximumRegressionCases)
            throw new DevelopmentTestValidationException(
                $"MaximumRegressionCases phải từ 1 đến {MaximumRegressionCases}.");

        var run = runs.Get(request.DevelopmentRunId)
            ?? throw new KeyNotFoundException("Không tìm thấy DevelopmentRun.");

        if (run.WorkspaceId != workspace.CurrentWorkspaceId)
            throw new DevelopmentTestValidationException(
                "DevelopmentRun không thuộc workspace hiện tại.");

        if (run.Status != "active" || run.Stage != DevelopmentRunStages.Testing)
            throw new DevelopmentTestValidationException(
                "Development auto test chỉ chạy khi DevelopmentRun đang ở stage testing.");

        if (!run.Branch.StartsWith("experiment/", StringComparison.Ordinal))
            throw new DevelopmentTestValidationException(
                "Development auto test chỉ chạy trên experiment branch.");

        var allWorktrees = await worktrees.GetAllAsync(
            run.RepositoryPath,
            cancellationToken);

        var worktree = allWorktrees.FirstOrDefault(x =>
            !x.IsMainWorktree &&
            x.Branch.Equals(run.Branch, StringComparison.Ordinal));

        if (worktree is null)
            throw new DevelopmentTestValidationException(
                "Không tìm thấy isolated worktree cho DevelopmentRun.");

        var startedAt = DateTimeOffset.UtcNow;
        var phases = new List<DevelopmentTestPhaseResult>();

        var solutionTarget = PrefixWorktreePath(
            worktree.WorktreePath,
            NormalizeRelativePath(request.DotnetTargetPath));

        var restore = await development.DotnetRestoreAsync(
            solutionTarget,
            cancellationToken);
        phases.Add(ToPhase(
            DevelopmentTestPhaseNames.Restore,
            restore));

        if (!restore.Succeeded)
            return Persist(BuildReport(false));

        var build = await development.DotnetBuildAsync(
            solutionTarget,
            "Release",
            cancellationToken);
        phases.Add(ToPhase(
            DevelopmentTestPhaseNames.Build,
            build));

        if (!build.Succeeded)
            return Persist(BuildReport(false));

        var unitTarget = PrefixWorktreePath(
            worktree.WorktreePath,
            NormalizeRelativePath(
                request.UnitTestTargetPath ?? request.DotnetTargetPath));

        var unit = await development.DotnetTestAsync(
            unitTarget,
            "Release",
            cancellationToken);
        phases.Add(ToPhase(
            DevelopmentTestPhaseNames.Unit,
            unit));

        if (!unit.Succeeded)
            return Persist(BuildReport(false));

        if (!string.IsNullOrWhiteSpace(request.IntegrationTestTargetPath))
        {
            var integrationTarget = PrefixWorktreePath(
                worktree.WorktreePath,
                NormalizeRelativePath(request.IntegrationTestTargetPath));

            var integration = await development.DotnetTestAsync(
                integrationTarget,
                "Release",
                cancellationToken);

            phases.Add(ToPhase(
                DevelopmentTestPhaseNames.Integration,
                integration));

            if (!integration.Succeeded)
                return Persist(BuildReport(false));
        }
        else
        {
            phases.Add(new DevelopmentTestPhaseResult(
                DevelopmentTestPhaseNames.Integration,
                "skipped",
                null,
                false,
                0,
                null,
                false));
        }

        phases.Add(RunRegression(request.MaximumRegressionCases));

        var succeeded = phases.All(x =>
            x.Status is "passed" or "skipped");

        return Persist(BuildReport(succeeded));

        DevelopmentTestReport BuildReport(bool success) =>
            new(
                Guid.NewGuid(),
                workspace.CurrentWorkspaceId,
                run.Id,
                run.RepositoryPath,
                run.Branch,
                worktree.WorktreePath,
                request.DotnetTargetPath,
                phases.ToArray(),
                success,
                startedAt,
                DateTimeOffset.UtcNow);

        DevelopmentTestReport Persist(DevelopmentTestReport report)
        {
            lock (_gate)
            {
                var all = Load();
                all.Add(report);
                Save(all);
            }

            audit.Record(
                AuditAgents.System,
                "development.auto-test.complete",
                $"development-run:{run.Id:D}",
                $"report:{report.Id:D};success:{report.Succeeded};phases:{report.Phases.Count};worktree:{report.WorktreePath}",
                report.Succeeded ? AuditResults.Succeeded : AuditResults.Failed);

            return report;
        }
    }

    private DevelopmentTestPhaseResult RunRegression(int maximumCases)
    {
        var dataset = regressionStore.GetAll()
            .Where(x => x.Enabled)
            .Take(maximumCases)
            .ToArray();

        var supported = evaluation.Categories
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var executed = 0;
        var passed = 0;
        var failed = 0;
        var skipped = 0;
        var failures = new List<string>();

        foreach (var item in dataset)
        {
            if (!supported.Contains(item.Category))
            {
                skipped++;
                continue;
            }

            executed++;
            try
            {
                var result = evaluation.Evaluate(new EvaluationCase
                {
                    Id = item.Id,
                    Category = item.Category,
                    Input = item.Input,
                    Expected = item.Expected,
                    Metadata = item.Metadata.ToDictionary(
                        x => x.Key,
                        x => x.Value,
                        StringComparer.OrdinalIgnoreCase)
                });

                if (result.Errors.Count == 0)
                {
                    passed++;
                }
                else
                {
                    failed++;
                    failures.Add(
                        $"{item.Id}: {string.Join(", ", result.Errors)}");
                }
            }
            catch (Exception exception) when (
                exception is EvaluationEngineException or InvalidOperationException)
            {
                failed++;
                failures.Add($"{item.Id}: {exception.Message}");
            }
        }

        var failureLog = failures.Count == 0
            ? null
            : Sanitize(string.Join(Environment.NewLine, failures), out _);

        var truncated = false;
        if (failureLog is not null)
            failureLog = Sanitize(failureLog, out truncated);

        return new DevelopmentTestPhaseResult(
            DevelopmentTestPhaseNames.Regression,
            failed == 0 ? "passed" : "failed",
            null,
            false,
            0,
            failureLog,
            truncated,
            executed,
            passed,
            failed,
            skipped);
    }

    private static DevelopmentTestPhaseResult ToPhase(
        string phase,
        DevelopmentProcessResult result)
    {
        var failure = result.Succeeded
            ? null
            : Sanitize(result.Output, out _);

        var truncated = false;
        if (failure is not null)
            failure = Sanitize(failure, out truncated);

        return new(
            phase,
            result.Succeeded ? "passed" : "failed",
            result.ExitCode,
            result.TimedOut,
            result.DurationMs,
            failure,
            truncated);
    }

    private static string Sanitize(string? value, out bool truncated)
    {
        var text = value ?? string.Empty;
        var redacted = SecretPattern.Replace(text, "[REDACTED]");
        truncated = redacted.Length > MaximumFailureLogCharacters;
        return truncated
            ? redacted[..MaximumFailureLogCharacters]
            : redacted;
    }

    private static string NormalizeRelativePath(string? value)
    {
        var path = (value ?? string.Empty).Trim().Replace('\\', '/');
        if (path.Length is < 1 or > 500 ||
            Path.IsPathRooted(path) ||
            path.StartsWith("../", StringComparison.Ordinal) ||
            path.Contains("/../", StringComparison.Ordinal) ||
            path.Equals("..", StringComparison.Ordinal))
        {
            throw new DevelopmentTestValidationException(
                "Test target path không hợp lệ.");
        }
        return path.Trim('/');
    }

    private static string PrefixWorktreePath(
        string worktreePath,
        string relativePath)
    {
        var prefix = worktreePath.Trim().Replace('\\', '/').Trim('/');
        if (prefix.Length == 0)
            throw new DevelopmentTestValidationException(
                "Worktree path không hợp lệ.");
        return $"{prefix}/{relativePath}";
    }

    private List<DevelopmentTestReport> Load()
    {
        var path = PathForWorkspace();
        if (!File.Exists(path)) return [];

        try
        {
            return JsonSerializer.Deserialize<List<DevelopmentTestReport>>(
                File.ReadAllText(path),
                Options) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private void Save(List<DevelopmentTestReport> reports)
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
        return Path.Combine(_root, $"development-test-reports-{safe}.json");
    }

    private static string ResolveRoot(IConfiguration configuration)
    {
        var root = configuration["Development:TestReportRoot"];
        if (string.IsNullOrWhiteSpace(root))
        {
            root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PersonalAI",
                "Development",
                "TestReports");
        }

        root = Path.GetFullPath(Environment.ExpandEnvironmentVariables(root));
        Directory.CreateDirectory(root);
        return root;
    }
}
