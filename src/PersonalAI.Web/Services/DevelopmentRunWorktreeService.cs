using System.Text.Json;
using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public interface IDevelopmentRunWorktreeService
{
    DevelopmentRunWorktreeStatus GetStatus();
    IReadOnlyList<DevelopmentRunWorktreeBinding> GetAll();
    DevelopmentRunWorktreeBinding? GetByRun(Guid developmentRunId);
    Task<DevelopmentRunWorktreeBinding> EnsureAsync(
        EnsureDevelopmentRunWorktreeRequest request,
        CancellationToken cancellationToken = default);
    Task<DevelopmentRunWorktreeBinding> CleanupAsync(
        CleanupDevelopmentRunWorktreeRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class DevelopmentRunWorktreeService(
    IDevelopmentRunService runs,
    IDevelopmentWorktreeService worktrees,
    IWorkspaceContextAccessor workspace,
    IConfiguration configuration,
    IAuditRecorder audit) : IDevelopmentRunWorktreeService
{
    private readonly object _gate = new();
    private readonly string _root = ResolveRoot(configuration);
    private static readonly JsonSerializerOptions Options =
        new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public DevelopmentRunWorktreeStatus GetStatus()
    {
        var all = GetAll();
        return new(
            PersonalAiRelease.Version,
            workspace.CurrentWorkspaceId,
            all.Count,
            all.Count(x => x.State == DevelopmentRunWorktreeStates.Active),
            all.Count(x => x.State == DevelopmentRunWorktreeStates.CleanupDeferredDirty),
            all.Count(x => x.State == DevelopmentRunWorktreeStates.Removed),
            OneWorktreePerRun: true,
            CleanupRequiresTerminalRun: true,
            DirtyCleanupBlocked: true,
            ForceRemoveEnabled: false);
    }

    public IReadOnlyList<DevelopmentRunWorktreeBinding> GetAll()
    {
        lock (_gate)
            return Load()
                .OrderByDescending(x => x.UpdatedAt)
                .ToArray();
    }

    public DevelopmentRunWorktreeBinding? GetByRun(Guid developmentRunId)
    {
        lock (_gate)
            return Load().FirstOrDefault(x =>
                x.DevelopmentRunId == developmentRunId);
    }

    public async Task<DevelopmentRunWorktreeBinding> EnsureAsync(
        EnsureDevelopmentRunWorktreeRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!request.ConfirmCreateWorktree)
            throw new DevelopmentRunWorktreeValidationException(
                "Cần ConfirmCreateWorktree=true để tạo worktree riêng cho DevelopmentRun.");

        var run = runs.Get(request.DevelopmentRunId)
            ?? throw new KeyNotFoundException("Không tìm thấy DevelopmentRun.");

        if (run.WorkspaceId != workspace.CurrentWorkspaceId)
            throw new DevelopmentRunWorktreeValidationException(
                "DevelopmentRun không thuộc workspace hiện tại.");

        if (run.Status != "active")
            throw new DevelopmentRunWorktreeValidationException(
                "Chỉ DevelopmentRun active mới được tạo worktree.");

        if (!run.Branch.StartsWith("experiment/", StringComparison.Ordinal))
            throw new DevelopmentRunWorktreeValidationException(
                "DevelopmentRun worktree chỉ dùng experiment/* branch.");

        DevelopmentRunWorktreeBinding? existing;
        lock (_gate)
            existing = Load().FirstOrDefault(x =>
                x.DevelopmentRunId == run.Id);

        if (existing is not null)
        {
            if (existing.State == DevelopmentRunWorktreeStates.Removed)
                throw new DevelopmentRunWorktreeValidationException(
                    "Worktree của DevelopmentRun đã cleanup; không được tạo worktree thứ hai cho cùng run.");

            var current = await worktrees.GetAllAsync(
                run.RepositoryPath,
                cancellationToken);

            var actual = current.FirstOrDefault(x =>
                x.WorktreePath.Equals(
                    existing.WorktreePath,
                    StringComparison.OrdinalIgnoreCase) &&
                x.Branch.Equals(
                    run.Branch,
                    StringComparison.Ordinal));

            if (actual is null)
            {
                var missing = existing with
                {
                    State = DevelopmentRunWorktreeStates.Missing,
                    Dirty = false,
                    UpdatedAt = DateTimeOffset.UtcNow
                };
                SaveReplacement(missing);
                throw new DevelopmentRunWorktreeValidationException(
                    "Binding worktree đã persist nhưng worktree thực tế không còn tồn tại; từ chối tạo bản thay thế tự động.");
            }

            var refreshed = existing with
            {
                State = actual.IsDirty
                    ? DevelopmentRunWorktreeStates.CleanupDeferredDirty
                    : DevelopmentRunWorktreeStates.Active,
                Dirty = actual.IsDirty,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            SaveReplacement(refreshed);
            return refreshed;
        }

        var created = await worktrees.CreateAsync(
            new CreateDevelopmentWorktreeRequest(
                run.RepositoryPath,
                run.Branch,
                request.StartPoint,
                $"run-{run.Id:N}",
                ConfirmCreateWorktree: true),
            cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var binding = new DevelopmentRunWorktreeBinding(
            Guid.NewGuid(),
            workspace.CurrentWorkspaceId,
            run.Id,
            run.RepositoryPath,
            run.Branch,
            created.WorktreePath,
            created.StartPoint,
            DevelopmentRunWorktreeStates.Active,
            Dirty: false,
            now,
            now,
            null);

        lock (_gate)
        {
            var all = Load();
            if (all.Any(x => x.DevelopmentRunId == run.Id))
                throw new DevelopmentRunWorktreeValidationException(
                    "DevelopmentRun đã có worktree binding.");
            all.Add(binding);
            Save(all);
        }

        audit.Record(
            AuditAgents.System,
            "development.run-worktree.create",
            $"development-run:{run.Id:D}",
            $"binding:{binding.Id:D};branch:{run.Branch};worktree:{binding.WorktreePath}",
            AuditResults.Succeeded);

        return binding;
    }

    public async Task<DevelopmentRunWorktreeBinding> CleanupAsync(
        CleanupDevelopmentRunWorktreeRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!request.ConfirmCleanup)
            throw new DevelopmentRunWorktreeValidationException(
                "Cần ConfirmCleanup=true để cleanup DevelopmentRun worktree.");

        var run = runs.Get(request.DevelopmentRunId)
            ?? throw new KeyNotFoundException("Không tìm thấy DevelopmentRun.");

        if (run.WorkspaceId != workspace.CurrentWorkspaceId)
            throw new DevelopmentRunWorktreeValidationException(
                "DevelopmentRun không thuộc workspace hiện tại.");

        if (run.Status is not ("completed" or "failed" or "cancelled"))
            throw new DevelopmentRunWorktreeValidationException(
                "Chỉ cleanup worktree khi DevelopmentRun đã terminal.");

        DevelopmentRunWorktreeBinding binding;
        lock (_gate)
        {
            binding = Load().FirstOrDefault(x =>
                x.DevelopmentRunId == run.Id)
                ?? throw new KeyNotFoundException(
                    "DevelopmentRun chưa có worktree binding.");
        }

        if (binding.State == DevelopmentRunWorktreeStates.Removed)
            return binding;

        var actualWorktrees = await worktrees.GetAllAsync(
            run.RepositoryPath,
            cancellationToken);

        var actual = actualWorktrees.FirstOrDefault(x =>
            x.WorktreePath.Equals(
                binding.WorktreePath,
                StringComparison.OrdinalIgnoreCase) &&
            x.Branch.Equals(
                binding.Branch,
                StringComparison.Ordinal));

        if (actual is null)
        {
            var missing = binding with
            {
                State = DevelopmentRunWorktreeStates.Missing,
                Dirty = false,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            SaveReplacement(missing);
            return missing;
        }

        if (actual.IsDirty)
        {
            var deferred = binding with
            {
                State = DevelopmentRunWorktreeStates.CleanupDeferredDirty,
                Dirty = true,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            SaveReplacement(deferred);

            audit.Record(
                AuditAgents.System,
                "development.run-worktree.cleanup-deferred",
                $"development-run:{run.Id:D}",
                $"binding:{binding.Id:D};worktree:{binding.WorktreePath};dirty:true",
                AuditResults.Prepared);

            return deferred;
        }

        var removed = await worktrees.RemoveAsync(
            new RemoveDevelopmentWorktreeRequest(
                run.RepositoryPath,
                binding.WorktreePath,
                ConfirmRemoveWorktree: true),
            cancellationToken);

        var result = binding with
        {
            State = DevelopmentRunWorktreeStates.Removed,
            Dirty = false,
            UpdatedAt = removed.RemovedAt,
            RemovedAt = removed.RemovedAt
        };
        SaveReplacement(result);

        audit.Record(
            AuditAgents.System,
            "development.run-worktree.cleanup",
            $"development-run:{run.Id:D}",
            $"binding:{binding.Id:D};worktree:{binding.WorktreePath};dirty:false;force:false",
            AuditResults.Succeeded);

        return result;
    }

    private void SaveReplacement(DevelopmentRunWorktreeBinding binding)
    {
        lock (_gate)
        {
            var all = Load();
            var index = all.FindIndex(x => x.Id == binding.Id);
            if (index < 0)
                throw new KeyNotFoundException(
                    "Không tìm thấy DevelopmentRun worktree binding.");
            all[index] = binding;
            Save(all);
        }
    }

    private List<DevelopmentRunWorktreeBinding> Load()
    {
        var path = PathForWorkspace();
        if (!File.Exists(path)) return [];

        try
        {
            return JsonSerializer.Deserialize<List<DevelopmentRunWorktreeBinding>>(
                File.ReadAllText(path),
                Options) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private void Save(List<DevelopmentRunWorktreeBinding> bindings)
    {
        Directory.CreateDirectory(_root);
        var path = PathForWorkspace();
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(bindings, Options));
        File.Move(temp, path, true);
    }

    private string PathForWorkspace()
    {
        var safe = string.Concat(workspace.CurrentWorkspaceId.Select(c =>
            char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_'));
        return Path.Combine(
            _root,
            $"development-run-worktrees-{safe}.json");
    }

    private static string ResolveRoot(IConfiguration configuration)
    {
        var root = configuration["Development:RunWorktreeRoot"];
        if (string.IsNullOrWhiteSpace(root))
        {
            root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PersonalAI",
                "Development",
                "RunWorktrees");
        }

        root = Path.GetFullPath(
            Environment.ExpandEnvironmentVariables(root));
        Directory.CreateDirectory(root);
        return root;
    }
}
