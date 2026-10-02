using System.Diagnostics;
using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public interface IDevelopmentWorktreeService
{
    DevelopmentWorktreeStatus GetStatus();

    Task<IReadOnlyList<DevelopmentWorktreeInfo>> GetAllAsync(
        string repositoryPath,
        CancellationToken cancellationToken = default);

    Task<DevelopmentWorktreeCreateResult> CreateAsync(
        CreateDevelopmentWorktreeRequest request,
        CancellationToken cancellationToken = default);

    Task<DevelopmentWorktreeRemoveResult> RemoveAsync(
        RemoveDevelopmentWorktreeRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class DevelopmentWorktreeService(
    IWorkspaceFileService workspaceFiles,
    IWorkspaceContextAccessor workspace,
    IAuditRecorder audit) : IDevelopmentWorktreeService
{
    public const string AgentWorktreeDirectory = ".personalai-worktrees";
    public const int GitTimeoutMs = 30_000;
    public const int MaximumOutputCharacters = 24_000;

    private static readonly StringComparison PathComparison =
        OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

    public DevelopmentWorktreeStatus GetStatus() =>
        new(
            PersonalAiRelease.Version,
            workspace.CurrentWorkspaceId,
            WorktreeIsolationEnabled: true,
            DirtyTreeOverwriteProtection: true,
            AutomaticResetEnabled: false,
            CleanupRequiresConfirmation: true,
            AgentWorktreeDirectory);

    public async Task<IReadOnlyList<DevelopmentWorktreeInfo>> GetAllAsync(
        string repositoryPath,
        CancellationToken cancellationToken = default)
    {
        var repo = await ResolveRepositoryAsync(repositoryPath, cancellationToken);
        var result = await RunGitAsync(
            repo,
            ["worktree", "list", "--porcelain"],
            cancellationToken);
        EnsureSucceeded(result, "Không đọc được danh sách worktree.");

        var entries = ParseWorktrees(result.Output);
        var output = new List<DevelopmentWorktreeInfo>(entries.Count);

        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var fullPath = Path.GetFullPath(entry.Path);
            EnsureInsideRoot(workspaceFiles.GetWorkspaceRoot(), fullPath);

            var status = await RunGitAtAsync(
                fullPath,
                ["status", "--porcelain", "--untracked-files=normal"],
                cancellationToken);
            var dirty = status.ExitCode == 0 && !string.IsNullOrWhiteSpace(status.Output);

            var relative = ToWorkspaceRelative(fullPath);
            output.Add(new DevelopmentWorktreeInfo(
                repo.RelativePath,
                relative,
                entry.Branch,
                entry.Head,
                string.Equals(fullPath, repo.FullPath, PathComparison),
                dirty));
        }

        return output;
    }

    public async Task<DevelopmentWorktreeCreateResult> CreateAsync(
        CreateDevelopmentWorktreeRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!request.ConfirmCreateWorktree)
            throw new DevelopmentWorktreeValidationException(
                "Cần ConfirmCreateWorktree=true để tạo agent worktree.");

        var repo = await ResolveRepositoryAsync(request.RepositoryPath, cancellationToken);
        var branch = NormalizeBranch(request.Branch);
        if (!branch.StartsWith("experiment/", StringComparison.Ordinal))
            throw new DevelopmentWorktreeValidationException(
                "Agent worktree chỉ được tạo branch có tiền tố experiment/.");

        var startPoint = NormalizeRef(request.StartPoint);
        var worktreeId = NormalizeWorktreeId(request.WorktreeId);

        var workspaceRoot = Path.GetFullPath(workspaceFiles.GetWorkspaceRoot());
        var root = Path.Combine(workspaceRoot, AgentWorktreeDirectory);
        Directory.CreateDirectory(root);

        var target = Path.GetFullPath(Path.Combine(root, worktreeId));
        EnsureInsideRoot(root, target);
        if (Directory.Exists(target) || File.Exists(target))
            throw new DevelopmentWorktreeValidationException(
                "Agent worktree path đã tồn tại.");

        var verify = await RunGitAsync(
            repo,
            ["rev-parse", "--verify", startPoint],
            cancellationToken);
        EnsureSucceeded(verify, $"Không tìm thấy start point '{startPoint}'.");

        var branchCheck = await RunGitAsync(
            repo,
            ["show-ref", "--verify", "--quiet", $"refs/heads/{branch}"],
            cancellationToken);

        IReadOnlyList<string> args = branchCheck.ExitCode == 0
            ? ["worktree", "add", target, branch]
            : ["worktree", "add", "-b", branch, target, startPoint];

        var create = await RunGitAsync(repo, args, cancellationToken);
        EnsureSucceeded(create, "Không tạo được agent worktree.");

        audit.Record(
            AuditAgents.System,
            "development.worktree.create",
            $"git-repository:{repo.RelativePath}",
            $"branch:{branch};start:{startPoint};worktree:{ToWorkspaceRelative(target)}",
            AuditResults.Succeeded);

        return new DevelopmentWorktreeCreateResult(
            repo.RelativePath,
            ToWorkspaceRelative(target),
            branch,
            startPoint,
            Created: true,
            DateTimeOffset.UtcNow);
    }

    public async Task<DevelopmentWorktreeRemoveResult> RemoveAsync(
        RemoveDevelopmentWorktreeRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!request.ConfirmRemoveWorktree)
            throw new DevelopmentWorktreeValidationException(
                "Cần ConfirmRemoveWorktree=true để xóa agent worktree.");

        var repo = await ResolveRepositoryAsync(request.RepositoryPath, cancellationToken);
        var workspaceRoot = Path.GetFullPath(workspaceFiles.GetWorkspaceRoot());
        var allowedRoot = Path.GetFullPath(
            Path.Combine(workspaceRoot, AgentWorktreeDirectory));
        var target = Path.GetFullPath(
            Path.Combine(workspaceRoot, (request.WorktreePath ?? string.Empty).Trim()));

        EnsureInsideRoot(allowedRoot, target);

        if (!Directory.Exists(target))
            throw new DevelopmentWorktreeValidationException(
                "Không tìm thấy agent worktree.");

        var status = await RunGitAtAsync(
            target,
            ["status", "--porcelain", "--untracked-files=normal"],
            cancellationToken);
        EnsureSucceeded(status, "Không kiểm tra được agent worktree.");

        if (!string.IsNullOrWhiteSpace(status.Output))
            throw new DevelopmentWorktreeValidationException(
                "Agent worktree đang dirty; từ chối cleanup để không mất code chưa commit.");

        var remove = await RunGitAsync(
            repo,
            ["worktree", "remove", target],
            cancellationToken);
        EnsureSucceeded(remove, "Không xóa được agent worktree.");

        audit.Record(
            AuditAgents.System,
            "development.worktree.remove",
            $"git-repository:{repo.RelativePath}",
            $"worktree:{ToWorkspaceRelative(target)};dirty:false;force:false",
            AuditResults.Succeeded);

        return new DevelopmentWorktreeRemoveResult(
            repo.RelativePath,
            ToWorkspaceRelative(target),
            Removed: true,
            DateTimeOffset.UtcNow);
    }

    private async Task<RepositoryContext> ResolveRepositoryAsync(
        string? repositoryPath,
        CancellationToken cancellationToken)
    {
        var workspaceRoot = Path.GetFullPath(workspaceFiles.GetWorkspaceRoot());
        var requested = string.IsNullOrWhiteSpace(repositoryPath) ||
                        repositoryPath == "."
            ? workspaceRoot
            : Path.GetFullPath(Path.Combine(workspaceRoot, repositoryPath.Trim()));

        EnsureInsideRoot(workspaceRoot, requested);
        if (!Directory.Exists(requested))
            throw new DevelopmentWorktreeValidationException(
                "Không tìm thấy repository path trong workspace.");

        var probe = await RunGitAtAsync(
            requested,
            ["rev-parse", "--show-toplevel"],
            cancellationToken);
        EnsureSucceeded(probe, "Đường dẫn không thuộc Git repository.");

        var top = Path.GetFullPath(probe.Output.Trim());
        EnsureInsideRoot(workspaceRoot, top);

        return new RepositoryContext(
            top,
            ToWorkspaceRelative(top));
    }

    private static IReadOnlyList<WorktreeEntry> ParseWorktrees(string output)
    {
        var result = new List<WorktreeEntry>();
        string? path = null;
        string head = string.Empty;
        string branch = string.Empty;

        void Flush()
        {
            if (!string.IsNullOrWhiteSpace(path))
                result.Add(new WorktreeEntry(path!, head, branch));
            path = null;
            head = string.Empty;
            branch = string.Empty;
        }

        foreach (var raw in output.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.Length == 0)
            {
                Flush();
                continue;
            }

            if (line.StartsWith("worktree ", StringComparison.Ordinal))
                path = line["worktree ".Length..];
            else if (line.StartsWith("HEAD ", StringComparison.Ordinal))
                head = line["HEAD ".Length..];
            else if (line.StartsWith("branch refs/heads/", StringComparison.Ordinal))
                branch = line["branch refs/heads/".Length..];
            else if (line == "detached")
                branch = "(detached)";
        }

        Flush();
        return result;
    }

    private string ToWorkspaceRelative(string fullPath)
    {
        var workspaceRoot = Path.GetFullPath(workspaceFiles.GetWorkspaceRoot());
        var relative = Path.GetRelativePath(workspaceRoot, fullPath)
            .Replace(Path.DirectorySeparatorChar, '/');
        return relative == "." ? string.Empty : relative;
    }

    private static string NormalizeWorktreeId(string? value)
    {
        var id = (value ?? string.Empty).Trim();
        if (id.Length is < 1 or > 80 ||
            id.StartsWith('.') ||
            id.Any(c => !(char.IsLetterOrDigit(c) || c is '-' or '_')))
        {
            throw new DevelopmentWorktreeValidationException(
                "WorktreeId chỉ cho phép chữ, số, '-' và '_', tối đa 80 ký tự.");
        }
        return id;
    }

    private static string NormalizeBranch(string? value)
    {
        var branch = NormalizeRef(value);
        if (branch.Equals("main", StringComparison.OrdinalIgnoreCase) ||
            branch.Equals("master", StringComparison.OrdinalIgnoreCase))
        {
            throw new DevelopmentWorktreeValidationException(
                "Không được dùng main/master làm agent branch.");
        }
        return branch;
    }

    private static string NormalizeRef(string? value)
    {
        var reference = (value ?? string.Empty).Trim();
        if (reference.Length is < 1 or > 160 ||
            reference.StartsWith('-') ||
            reference.EndsWith('.') ||
            reference.EndsWith('/') ||
            reference.Contains("..", StringComparison.Ordinal) ||
            reference.Contains("//", StringComparison.Ordinal) ||
            reference.Contains("@{", StringComparison.Ordinal) ||
            reference.Any(c =>
                char.IsControl(c) ||
                char.IsWhiteSpace(c) ||
                c is '~' or '^' or ':' or '?' or '*' or '[' or '\\'))
        {
            throw new DevelopmentWorktreeValidationException(
                "Git ref không hợp lệ.");
        }
        return reference;
    }

    private static void EnsureInsideRoot(string root, string path)
    {
        var normalizedRoot = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var normalizedPath = Path.GetFullPath(path);

        if (string.Equals(normalizedRoot, normalizedPath, PathComparison))
            return;

        var prefix = normalizedRoot + Path.DirectorySeparatorChar;
        if (!normalizedPath.StartsWith(prefix, PathComparison))
            throw new DevelopmentWorktreeValidationException(
                "Worktree/path phải nằm trong workspace được phép.");
    }

    private static async Task<GitCapture> RunGitAsync(
        RepositoryContext repo,
        IReadOnlyList<string> args,
        CancellationToken cancellationToken) =>
        await RunGitAtAsync(repo.FullPath, args, cancellationToken);

    private static async Task<GitCapture> RunGitAtAsync(
        string directory,
        IReadOnlyList<string> args,
        CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo
        {
            FileName = OperatingSystem.IsWindows() ? "git.exe" : "git",
            WorkingDirectory = directory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        start.ArgumentList.Add("-c");
        start.ArgumentList.Add("core.fsmonitor=false");
        start.ArgumentList.Add("-c");
        start.ArgumentList.Add("color.ui=false");
        foreach (var arg in args)
            start.ArgumentList.Add(arg);

        using var process = new Process { StartInfo = start };
        var stopwatch = Stopwatch.StartNew();

        try
        {
            if (!process.Start())
                throw new DevelopmentWorktreeValidationException(
                    "Không khởi động được git process.");
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or
            System.ComponentModel.Win32Exception)
        {
            throw new DevelopmentWorktreeValidationException(
                $"Không khởi động được git: {exception.Message}");
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(GitTimeoutMs);
        var timedOut = false;

        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            timedOut = true;
            try { process.Kill(entireProcessTree: true); } catch { }
            await process.WaitForExitAsync(CancellationToken.None);
        }

        cancellationToken.ThrowIfCancellationRequested();

        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        var output = string.Join(
            Environment.NewLine,
            new[] { stdout.TrimEnd(), stderr.TrimEnd() }
                .Where(x => !string.IsNullOrWhiteSpace(x)));

        if (output.Length > MaximumOutputCharacters)
            output = output[..MaximumOutputCharacters];

        stopwatch.Stop();
        return new GitCapture(
            timedOut ? -1 : process.ExitCode,
            timedOut,
            output);
    }

    private static void EnsureSucceeded(GitCapture result, string message)
    {
        if (result.TimedOut || result.ExitCode != 0)
            throw new DevelopmentWorktreeValidationException(
                $"{message} {result.Output}".Trim());
    }

    private sealed record RepositoryContext(
        string FullPath,
        string RelativePath);

    private sealed record WorktreeEntry(
        string Path,
        string Head,
        string Branch);

    private sealed record GitCapture(
        int ExitCode,
        bool TimedOut,
        string Output);
}
