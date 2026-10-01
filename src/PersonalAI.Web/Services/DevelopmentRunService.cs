using System.Text.Json;
using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public interface IDevelopmentRunService
{
    DevelopmentRunStatus GetStatus();
    IReadOnlyList<DevelopmentRun> GetAll();
    DevelopmentRun? Get(Guid id);
    DevelopmentRun Create(CreateDevelopmentRunRequest request);
    DevelopmentRun Advance(Guid id, AdvanceDevelopmentRunRequest request);
    DevelopmentRun Fail(Guid id, FailDevelopmentRunRequest request);
    DevelopmentRun Cancel(Guid id, CancelDevelopmentRunRequest request);
    DevelopmentRun Resume(Guid id);
}

public sealed class DevelopmentRunService(
    IWorkspaceContextAccessor workspace,
    IConfiguration configuration,
    IAuditRecorder audit) : IDevelopmentRunService
{
    private readonly object _gate = new();
    private readonly string _root = ResolveRoot(configuration);
    private static readonly JsonSerializerOptions Options =
        new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public DevelopmentRunStatus GetStatus()
    {
        var all = GetAll();
        return new(
            PersonalAiRelease.Version,
            workspace.CurrentWorkspaceId,
            all.Count,
            all.Count(x => x.Status == "active"),
            all.Count(x => x.Status == "completed"),
            all.Count(x => x.Status == "failed"),
            all.Count(x => x.Status == "cancelled"),
            Persisted: true,
            Resumable: true,
            SideEffectsAutomatic: false,
            DevelopmentRunStages.Ordered);
    }

    public IReadOnlyList<DevelopmentRun> GetAll()
    {
        lock (_gate)
            return Load().OrderByDescending(x => x.UpdatedAt).ToArray();
    }

    public DevelopmentRun? Get(Guid id)
    {
        lock (_gate)
            return Load().FirstOrDefault(x => x.Id == id);
    }

    public DevelopmentRun Create(CreateDevelopmentRunRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!request.ConfirmCreate)
            throw new DevelopmentRunValidationException(
                "Cần ConfirmCreate=true để tạo DevelopmentRun.");

        var goal = NormalizeText(request.Goal, 1, 1000, "Goal");
        var repository = NormalizePath(request.RepositoryPath, "RepositoryPath");
        var branch = NormalizeBranch(request.Branch);
        var roadmapVersion = string.IsNullOrWhiteSpace(request.RoadmapVersion)
            ? null
            : NormalizeText(request.RoadmapVersion, 1, 40, "RoadmapVersion");

        lock (_gate)
        {
            var all = Load();
            if (all.Any(x =>
                x.Status == "active" &&
                x.RepositoryPath.Equals(repository, StringComparison.OrdinalIgnoreCase) &&
                x.Branch.Equals(branch, StringComparison.OrdinalIgnoreCase)))
            {
                throw new DevelopmentRunConflictException(
                    "Đã có DevelopmentRun active trên repository/branch này.");
            }

            var now = DateTimeOffset.UtcNow;
            var run = new DevelopmentRun(
                Guid.NewGuid(),
                workspace.CurrentWorkspaceId,
                goal,
                repository,
                branch,
                roadmapVersion,
                DevelopmentRunStages.Analysis,
                "active",
                null,
                [],
                now,
                now,
                null);

            all.Add(run);
            Save(all);

            audit.Record(
                AuditAgents.System,
                "development.run.create",
                $"development-run:{run.Id:D}",
                $"branch:{branch};stage:{run.Stage}",
                AuditResults.Prepared);

            return run;
        }
    }

    public DevelopmentRun Advance(Guid id, AdvanceDevelopmentRunRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        lock (_gate)
        {
            var all = Load();
            var index = all.FindIndex(x => x.Id == id);
            if (index < 0)
                throw new KeyNotFoundException("Không tìm thấy DevelopmentRun.");

            var current = all[index];
            EnsureActive(current);

            var expected = NormalizeStage(request.ExpectedCurrentStage);
            if (!current.Stage.Equals(expected, StringComparison.Ordinal))
            {
                throw new DevelopmentRunConflictException(
                    $"Stage hiện tại là '{current.Stage}', không phải '{expected}'.");
            }

            var result = NormalizeResult(request.Result);
            var evidence = NormalizeOptional(request.Evidence, 4000);

            var next = NextStage(current.Stage);
            var now = DateTimeOffset.UtcNow;

            var status = next == DevelopmentRunStages.Completed
                ? "completed"
                : "active";

            var history = current.History
                .Concat([
                    new DevelopmentRunTransition(
                        current.Stage,
                        next,
                        result,
                        evidence,
                        now)
                ])
                .ToArray();

            all[index] = current with
            {
                Stage = next,
                Status = status,
                CiRunId = request.CiRunId ?? current.CiRunId,
                History = history,
                UpdatedAt = now,
                CompletedAt = status == "completed" ? now : null
            };

            Save(all);

            audit.Record(
                AuditAgents.System,
                "development.run.advance",
                $"development-run:{current.Id:D}",
                $"from:{current.Stage};to:{next};result:{result}",
                status == "completed"
                    ? AuditResults.Succeeded
                    : AuditResults.Prepared);

            return all[index];
        }
    }

    public DevelopmentRun Fail(Guid id, FailDevelopmentRunRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return SetTerminal(
            id,
            DevelopmentRunStages.Failed,
            "failed",
            NormalizeText(request.Reason, 1, 1000, "Reason"),
            NormalizeOptional(request.Evidence, 4000),
            "development.run.fail");
    }

    public DevelopmentRun Cancel(Guid id, CancelDevelopmentRunRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return SetTerminal(
            id,
            DevelopmentRunStages.Cancelled,
            "cancelled",
            NormalizeText(request.Reason, 1, 1000, "Reason"),
            null,
            "development.run.cancel");
    }

    public DevelopmentRun Resume(Guid id)
    {
        lock (_gate)
        {
            var all = Load();
            var run = all.FirstOrDefault(x => x.Id == id)
                ?? throw new KeyNotFoundException("Không tìm thấy DevelopmentRun.");

            if (run.Status != "active")
                throw new DevelopmentRunValidationException(
                    "Chỉ DevelopmentRun active mới có thể resume.");

            audit.Record(
                AuditAgents.System,
                "development.run.resume",
                $"development-run:{run.Id:D}",
                $"stage:{run.Stage};history:{run.History.Count}",
                AuditResults.Prepared);

            return run;
        }
    }

    private DevelopmentRun SetTerminal(
        Guid id,
        string stage,
        string status,
        string reason,
        string? evidence,
        string auditAction)
    {
        lock (_gate)
        {
            var all = Load();
            var index = all.FindIndex(x => x.Id == id);
            if (index < 0)
                throw new KeyNotFoundException("Không tìm thấy DevelopmentRun.");

            var current = all[index];
            EnsureActive(current);

            var now = DateTimeOffset.UtcNow;
            var history = current.History
                .Concat([
                    new DevelopmentRunTransition(
                        current.Stage,
                        stage,
                        reason,
                        evidence,
                        now)
                ])
                .ToArray();

            all[index] = current with
            {
                Stage = stage,
                Status = status,
                History = history,
                UpdatedAt = now,
                CompletedAt = now
            };

            Save(all);

            audit.Record(
                AuditAgents.System,
                auditAction,
                $"development-run:{current.Id:D}",
                $"from:{current.Stage};to:{stage};reason:{SafeInline(reason, 300)}",
                status == "failed" ? AuditResults.Failed : AuditResults.Succeeded);

            return all[index];
        }
    }

    private static string NextStage(string current)
    {
        var index = DevelopmentRunStages.Ordered
            .ToList()
            .FindIndex(x => x.Equals(current, StringComparison.Ordinal));

        if (index < 0 || index >= DevelopmentRunStages.Ordered.Count - 1)
            throw new DevelopmentRunValidationException(
                $"Stage '{current}' không thể advance.");

        return DevelopmentRunStages.Ordered[index + 1];
    }

    private static void EnsureActive(DevelopmentRun run)
    {
        if (run.Status != "active")
            throw new DevelopmentRunValidationException(
                "DevelopmentRun đã ở trạng thái terminal.");
    }

    private static string NormalizeStage(string? value)
    {
        var stage = (value ?? string.Empty).Trim().ToLowerInvariant();
        if (!DevelopmentRunStages.Ordered.Contains(stage, StringComparer.Ordinal))
            throw new DevelopmentRunValidationException("ExpectedCurrentStage không hợp lệ.");
        return stage;
    }

    private static string NormalizeResult(string? value)
    {
        var result = (value ?? string.Empty).Trim().ToLowerInvariant();
        if (result is not ("passed" or "prepared" or "approved" or "succeeded"))
            throw new DevelopmentRunValidationException(
                "Result phải là passed, prepared, approved hoặc succeeded.");
        return result;
    }

    private static string NormalizePath(string? value, string name)
    {
        var path = (value ?? string.Empty).Trim().Replace('\\', '/');
        if (path.Length > 300 ||
            Path.IsPathRooted(path) ||
            path.StartsWith("../", StringComparison.Ordinal) ||
            path.Contains("/../", StringComparison.Ordinal))
        {
            throw new DevelopmentRunValidationException($"{name} không hợp lệ.");
        }
        return path;
    }

    private static string NormalizeBranch(string? value)
    {
        var branch = NormalizeText(value, 1, 160, "Branch");
        if (branch.StartsWith('-') ||
            branch.Contains("..", StringComparison.Ordinal) ||
            branch.Contains("@{", StringComparison.Ordinal) ||
            branch.Any(c =>
                char.IsControl(c) ||
                char.IsWhiteSpace(c) ||
                c is '~' or '^' or ':' or '?' or '*' or '[' or '\\'))
        {
            throw new DevelopmentRunValidationException("Branch không hợp lệ.");
        }
        return branch;
    }

    private static string NormalizeText(
        string? value,
        int minimum,
        int maximum,
        string name)
    {
        var text = (value ?? string.Empty).Trim();
        if (text.Length < minimum || text.Length > maximum || text.Any(char.IsControl))
            throw new DevelopmentRunValidationException($"{name} không hợp lệ.");
        return text;
    }

    private static string? NormalizeOptional(string? value, int maximum)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var text = value.Trim();
        return text.Length <= maximum ? text : text[..maximum];
    }

    private List<DevelopmentRun> Load()
    {
        var path = PathForWorkspace();
        if (!File.Exists(path)) return [];

        try
        {
            return JsonSerializer.Deserialize<List<DevelopmentRun>>(
                File.ReadAllText(path),
                Options) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private void Save(List<DevelopmentRun> runs)
    {
        Directory.CreateDirectory(_root);
        var path = PathForWorkspace();
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(runs, Options));
        File.Move(temp, path, true);
    }

    private string PathForWorkspace()
    {
        var safe = string.Concat(workspace.CurrentWorkspaceId.Select(c =>
            char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_'));
        return Path.Combine(_root, $"development-runs-{safe}.json");
    }

    private static string ResolveRoot(IConfiguration configuration)
    {
        var root = configuration["Development:RunRoot"];
        if (string.IsNullOrWhiteSpace(root))
        {
            root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PersonalAI",
                "Development",
                "Runs");
        }

        root = Path.GetFullPath(Environment.ExpandEnvironmentVariables(root));
        Directory.CreateDirectory(root);
        return root;
    }

    private static string SafeInline(string value, int maximum)
    {
        var normalized = value.Replace('\r', ' ').Replace('\n', ' ');
        return normalized.Length <= maximum ? normalized : normalized[..maximum];
    }
}
