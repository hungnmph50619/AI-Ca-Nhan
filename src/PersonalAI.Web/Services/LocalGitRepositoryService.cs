using System.Diagnostics;
using System.Text;
using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public interface ILocalGitRepositoryService
{
    LocalGitRepositoryStatus GetStatus();

    Task<LocalGitCommandResult> StatusAsync(
        string repositoryPath,
        CancellationToken cancellationToken = default);

    Task<LocalGitCommandResult> FetchAsync(
        LocalGitFetchRequest request,
        CancellationToken cancellationToken = default);

    Task<LocalGitBranchList> BranchesAsync(
        string repositoryPath,
        CancellationToken cancellationToken = default);

    Task<LocalGitCommandResult> CheckoutAsync(
        LocalGitCheckoutRequest request,
        CancellationToken cancellationToken = default);

    Task<LocalGitCommandResult> CreateBranchAsync(
        LocalGitCreateBranchRequest request,
        CancellationToken cancellationToken = default);

    Task<LocalGitCommandResult> DiffAsync(
        LocalGitDiffRequest request,
        CancellationToken cancellationToken = default);

    Task<LocalGitCommandResult> LogAsync(
        LocalGitLogRequest request,
        CancellationToken cancellationToken = default);

    Task<LocalGitCommandResult> PullAsync(
        LocalGitPullRequest request,
        CancellationToken cancellationToken = default);

    Task<LocalGitCommandResult> CommitAsync(
        LocalGitCommitRequest request,
        CancellationToken cancellationToken = default);

    Task<LocalGitCommandResult> PushAsync(
        LocalGitPushRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class LocalGitRepositoryService(
    IWorkspaceFileService workspaceFiles,
    IWorkspaceContextAccessor workspace,
    IGitCredentialService credentials,
    IAuditRecorder audit) : ILocalGitRepositoryService
{
    public const int GitTimeoutMs = 30_000;
    public const int NetworkGitTimeoutMs = 90_000;
    public const int MaximumOutputCharacters = 48_000;
    public const int MaximumCommitPaths = 50;
    public const int MaximumLogEntries = 100;

    private static readonly StringComparison PathComparison =
        OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

    public LocalGitRepositoryStatus GetStatus() =>
        new(
            PersonalAiRelease.Version,
            workspace.CurrentWorkspaceId,
            GitAvailable: FindGitExecutable() is not null,
            FetchEnabled: true,
            CheckoutEnabled: true,
            PullFastForwardOnly: true,
            CommitEnabled: true,
            PushEnabled: true,
            ForcePushEnabled: false,
            ResetEnabled: false,
            GenericShellEnabled: false);

    public async Task<LocalGitCommandResult> StatusAsync(
        string repositoryPath,
        CancellationToken cancellationToken = default)
    {
        var repo = await ResolveRepositoryAsync(repositoryPath, cancellationToken);
        return await RunGitAsync(
            repo,
            "status",
            ["status", "--short", "--branch", "--untracked-files=normal"],
            GitTimeoutMs,
            cancellationToken);
    }

    public async Task<LocalGitCommandResult> FetchAsync(
        LocalGitFetchRequest request,
        CancellationToken cancellationToken = default)
    {
        RequireConfirmation(request.ConfirmGitWrite, "fetch");
        var repo = await ResolveRepositoryAsync(request.RepositoryPath, cancellationToken);
        var remote = NormalizeRemote(request.Remote);

        var credential = ResolveCredential(request.CredentialRef);
        var result = await RunGitAsync(
            repo,
            "fetch",
            ["fetch", "--prune", remote],
            NetworkGitTimeoutMs,
            cancellationToken,
            credential);

        AuditWrite("development.git.fetch", repo.RelativePath, $"remote:{remote}", result);
        return result;
    }

    public async Task<LocalGitBranchList> BranchesAsync(
        string repositoryPath,
        CancellationToken cancellationToken = default)
    {
        var repo = await ResolveRepositoryAsync(repositoryPath, cancellationToken);
        var current = await ReadCurrentBranchAsync(repo, cancellationToken);

        var local = await RunGitRawAsync(
            repo.FullPath,
            ["branch", "--format=%(refname:short)"],
            GitTimeoutMs,
            cancellationToken);
        EnsureSucceeded(local, "Không đọc được local branches.");

        var remote = await RunGitRawAsync(
            repo.FullPath,
            ["branch", "-r", "--format=%(refname:short)"],
            GitTimeoutMs,
            cancellationToken);
        EnsureSucceeded(remote, "Không đọc được remote branches.");

        return new LocalGitBranchList(
            repo.RelativePath,
            current,
            SplitLines(local.Output),
            SplitLines(remote.Output));
    }

    public async Task<LocalGitCommandResult> CheckoutAsync(
        LocalGitCheckoutRequest request,
        CancellationToken cancellationToken = default)
    {
        RequireConfirmation(request.ConfirmGitWrite, "checkout");
        var repo = await ResolveRepositoryAsync(request.RepositoryPath, cancellationToken);
        var branch = NormalizeBranch(request.Branch);

        await EnsureCleanWorktreeAsync(repo, cancellationToken);

        var result = await RunGitAsync(
            repo,
            "checkout",
            ["switch", branch],
            GitTimeoutMs,
            cancellationToken);

        AuditWrite("development.git.checkout", repo.RelativePath, $"branch:{branch}", result);
        return result;
    }

    public async Task<LocalGitCommandResult> CreateBranchAsync(
        LocalGitCreateBranchRequest request,
        CancellationToken cancellationToken = default)
    {
        RequireConfirmation(request.ConfirmGitWrite, "branch create");
        var repo = await ResolveRepositoryAsync(request.RepositoryPath, cancellationToken);
        var branch = NormalizeBranch(request.Branch);
        var startPoint = string.IsNullOrWhiteSpace(request.StartPoint)
            ? await ReadCurrentBranchAsync(repo, cancellationToken)
            : NormalizeRef(request.StartPoint);

        if (string.Equals(branch, startPoint, StringComparison.Ordinal))
            throw new LocalGitRepositoryValidationException(
                "Branch mới phải khác start point.");

        await EnsureCleanWorktreeAsync(repo, cancellationToken);

        IReadOnlyList<string> args = request.Checkout
            ? ["switch", "-c", branch, startPoint]
            : ["branch", branch, startPoint];

        var result = await RunGitAsync(
            repo,
            "branch-create",
            args,
            GitTimeoutMs,
            cancellationToken);

        AuditWrite(
            "development.git.branch-create",
            repo.RelativePath,
            $"branch:{branch};start:{startPoint};checkout:{request.Checkout}",
            result);
        return result;
    }

    public async Task<LocalGitCommandResult> DiffAsync(
        LocalGitDiffRequest request,
        CancellationToken cancellationToken = default)
    {
        var repo = await ResolveRepositoryAsync(request.RepositoryPath, cancellationToken);
        var args = new List<string>
        {
            "diff",
            "--no-ext-diff",
            "--no-textconv",
            "--no-color",
            "--ignore-submodules=all",
            "--unified=3"
        };
        if (request.Staged) args.Add("--cached");
        args.Add("--");

        return await RunGitAsync(
            repo,
            request.Staged ? "diff-staged" : "diff",
            args,
            GitTimeoutMs,
            cancellationToken);
    }

    public async Task<LocalGitCommandResult> LogAsync(
        LocalGitLogRequest request,
        CancellationToken cancellationToken = default)
    {
        var repo = await ResolveRepositoryAsync(request.RepositoryPath, cancellationToken);
        var count = Math.Clamp(request.MaximumEntries, 1, MaximumLogEntries);

        return await RunGitAsync(
            repo,
            "log",
            [
                "log",
                $"--max-count={count}",
                "--date=iso-strict",
                "--pretty=format:%H%x09%ad%x09%an%x09%s"
            ],
            GitTimeoutMs,
            cancellationToken);
    }

    public async Task<LocalGitCommandResult> PullAsync(
        LocalGitPullRequest request,
        CancellationToken cancellationToken = default)
    {
        RequireConfirmation(request.ConfirmGitWrite, "pull");
        var repo = await ResolveRepositoryAsync(request.RepositoryPath, cancellationToken);
        var remote = NormalizeRemote(request.Remote);
        var current = await ReadCurrentBranchAsync(repo, cancellationToken);
        var branch = string.IsNullOrWhiteSpace(request.Branch)
            ? current
            : NormalizeBranch(request.Branch);

        if (!string.Equals(branch, current, StringComparison.Ordinal))
            throw new LocalGitRepositoryValidationException(
                "Pull chỉ được thực hiện vào branch hiện tại.");

        await EnsureCleanWorktreeAsync(repo, cancellationToken);

        var credential = ResolveCredential(request.CredentialRef);
        var result = await RunGitAsync(
            repo,
            "pull-ff-only",
            ["pull", "--ff-only", remote, branch],
            NetworkGitTimeoutMs,
            cancellationToken,
            credential);

        AuditWrite(
            "development.git.pull",
            repo.RelativePath,
            $"remote:{remote};branch:{branch};mode:ff-only",
            result);
        return result;
    }

    public async Task<LocalGitCommandResult> CommitAsync(
        LocalGitCommitRequest request,
        CancellationToken cancellationToken = default)
    {
        RequireConfirmation(request.ConfirmGitWrite, "commit");
        var repo = await ResolveRepositoryAsync(request.RepositoryPath, cancellationToken);
        var message = NormalizeCommitMessage(request.Message);
        var paths = NormalizeCommitPaths(repo, request.Paths);

        var addArgs = new List<string> { "add", "--" };
        addArgs.AddRange(paths);

        var add = await RunGitRawAsync(
            repo.FullPath,
            addArgs,
            GitTimeoutMs,
            cancellationToken);
        EnsureSucceeded(add, "Không stage được các path đã chọn.");

        var staged = await RunGitRawAsync(
            repo.FullPath,
            ["diff", "--cached", "--quiet", "--exit-code"],
            GitTimeoutMs,
            cancellationToken);

        if (!staged.TimedOut && staged.ExitCode == 0)
            throw new LocalGitRepositoryValidationException(
                "Không có staged change để commit.");
        if (staged.TimedOut || staged.ExitCode is not (0 or 1))
            throw new LocalGitRepositoryValidationException(
                "Không xác minh được staged changes trước commit.");

        var result = await RunGitAsync(
            repo,
            "commit",
            ["commit", "-m", message],
            NetworkGitTimeoutMs,
            cancellationToken);

        AuditWrite(
            "development.git.commit",
            repo.RelativePath,
            $"paths:{paths.Count};message-length:{message.Length}",
            result);
        return result;
    }

    public async Task<LocalGitCommandResult> PushAsync(
        LocalGitPushRequest request,
        CancellationToken cancellationToken = default)
    {
        RequireConfirmation(request.ConfirmGitWrite, "push");
        var repo = await ResolveRepositoryAsync(request.RepositoryPath, cancellationToken);
        var remote = NormalizeRemote(request.Remote);
        var current = await ReadCurrentBranchAsync(repo, cancellationToken);
        var branch = string.IsNullOrWhiteSpace(request.Branch)
            ? current
            : NormalizeBranch(request.Branch);

        if (!string.Equals(branch, current, StringComparison.Ordinal))
            throw new LocalGitRepositoryValidationException(
                "Push chỉ được phép với branch hiện tại.");

        var credential = ResolveCredential(request.CredentialRef);
        var result = await RunGitAsync(
            repo,
            "push",
            ["push", remote, branch],
            NetworkGitTimeoutMs,
            cancellationToken,
            credential);

        AuditWrite(
            "development.git.push",
            repo.RelativePath,
            $"remote:{remote};branch:{branch};force:false",
            result);
        return result;
    }

    private async Task<RepositoryContext> ResolveRepositoryAsync(
        string? repositoryPath,
        CancellationToken cancellationToken)
    {
        if (FindGitExecutable() is null)
            throw new LocalGitRepositoryValidationException(
                "Không tìm thấy git executable.");

        var workspaceRoot = Path.GetFullPath(workspaceFiles.GetWorkspaceRoot());
        var requested = string.IsNullOrWhiteSpace(repositoryPath)
            ? workspaceRoot
            : Path.GetFullPath(Path.Combine(workspaceRoot, repositoryPath.Trim()));

        EnsureInsideRoot(workspaceRoot, requested);
        EnsureNoSymlinkTraversal(workspaceRoot, requested);

        if (!Directory.Exists(requested))
            throw new LocalGitRepositoryValidationException(
                "Không tìm thấy repository path trong workspace.");

        var probe = await RunGitRawAsync(
            requested,
            ["rev-parse", "--show-toplevel"],
            GitTimeoutMs,
            cancellationToken);
        EnsureSucceeded(probe, "Đường dẫn không thuộc Git worktree.");

        var topLevel = Path.GetFullPath(probe.Output.Trim());
        EnsureInsideRoot(workspaceRoot, topLevel);
        EnsureNoSymlinkTraversal(workspaceRoot, topLevel);

        var relative = Path.GetRelativePath(workspaceRoot, topLevel)
            .Replace(Path.DirectorySeparatorChar, '/');
        if (relative == ".") relative = string.Empty;

        return new RepositoryContext(topLevel, relative);
    }

    private static async Task EnsureCleanWorktreeAsync(
        RepositoryContext repo,
        CancellationToken cancellationToken)
    {
        var status = await RunGitRawAsync(
            repo.FullPath,
            ["status", "--porcelain", "--untracked-files=normal"],
            GitTimeoutMs,
            cancellationToken);
        EnsureSucceeded(status, "Không kiểm tra được worktree.");
        if (!string.IsNullOrWhiteSpace(status.Output))
            throw new LocalGitRepositoryValidationException(
                "Worktree đang có thay đổi chưa commit; từ chối checkout/branch/pull.");
    }

    private static async Task<string> ReadCurrentBranchAsync(
        RepositoryContext repo,
        CancellationToken cancellationToken)
    {
        var result = await RunGitRawAsync(
            repo.FullPath,
            ["branch", "--show-current"],
            GitTimeoutMs,
            cancellationToken);
        EnsureSucceeded(result, "Không đọc được branch hiện tại.");

        var branch = result.Output.Trim();
        if (string.IsNullOrWhiteSpace(branch))
            throw new LocalGitRepositoryValidationException(
                "Repository đang ở detached HEAD.");
        return NormalizeBranch(branch);
    }

    private static IReadOnlyList<string> NormalizeCommitPaths(
        RepositoryContext repo,
        IReadOnlyList<string>? paths)
    {
        if (paths is null || paths.Count is < 1 or > MaximumCommitPaths)
            throw new LocalGitRepositoryValidationException(
                $"Commit cần từ 1 đến {MaximumCommitPaths} path cụ thể.");

        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var value in paths)
        {
            var path = (value ?? string.Empty).Trim().Replace('\\', '/');
            if (path.Length is < 1 or > 300 ||
                Path.IsPathRooted(path) ||
                path.StartsWith("../", StringComparison.Ordinal) ||
                path.Contains("/../", StringComparison.Ordinal) ||
                path.Equals("..", StringComparison.Ordinal) ||
                path.Split('/').Any(x => x.Equals(".git", StringComparison.OrdinalIgnoreCase)))
            {
                throw new LocalGitRepositoryValidationException(
                    $"Commit path không hợp lệ: '{path}'.");
            }

            var full = Path.GetFullPath(
                Path.Combine(repo.FullPath, path.Replace('/', Path.DirectorySeparatorChar)));
            EnsureInsideRoot(repo.FullPath, full);

            if (!seen.Add(path))
                throw new LocalGitRepositoryValidationException(
                    $"Commit path bị trùng: '{path}'.");

            result.Add(path);
        }

        return result;
    }

    private static string NormalizeCommitMessage(string? value)
    {
        var message = (value ?? string.Empty).Trim();
        if (message.Length is < 1 or > 300 ||
            message.IndexOf('\0') >= 0 ||
            message.Contains('\r') ||
            message.Contains('\n'))
        {
            throw new LocalGitRepositoryValidationException(
                "Commit message phải có 1-300 ký tự và chỉ một dòng.");
        }
        return message;
    }

    private static string NormalizeRemote(string? value)
    {
        var remote = (value ?? string.Empty).Trim();
        if (remote.Length is < 1 or > 80 ||
            remote.StartsWith('-') ||
            remote.Any(c => !(char.IsLetterOrDigit(c) || c is '-' or '_' or '.')))
        {
            throw new LocalGitRepositoryValidationException(
                "Remote name không hợp lệ.");
        }
        return remote;
    }

    private static string NormalizeBranch(string? value) =>
        NormalizeRef(value, branchOnly: true);

    private static string NormalizeRef(string? value, bool branchOnly = false)
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
            throw new LocalGitRepositoryValidationException(
                branchOnly ? "Tên branch không hợp lệ." : "Git ref không hợp lệ.");
        }
        return reference;
    }

    private static void RequireConfirmation(bool confirmed, string action)
    {
        if (!confirmed)
            throw new LocalGitRepositoryValidationException(
                $"Git {action} cần ConfirmGitWrite=true.");
    }

    private GitCredentialMaterial? ResolveCredential(string? credentialRef)
    {
        if (string.IsNullOrWhiteSpace(credentialRef))
            return null;

        try
        {
            return credentials.Resolve(credentialRef);
        }
        catch (GitCredentialValidationException)
        {
            throw;
        }
        catch (KeyNotFoundException exception)
        {
            throw new LocalGitRepositoryValidationException(exception.Message);
        }
    }

    private void AuditWrite(
        string action,
        string repositoryPath,
        string reason,
        ProcessCapture result)
    {
        audit.Record(
            AuditAgents.User,
            action,
            $"git-repository:{repositoryPath}",
            reason,
            result.ExitCode == 0 && !result.TimedOut
                ? AuditResults.Succeeded
                : AuditResults.Failed);
    }

    private async Task<LocalGitCommandResult> RunGitAsync(
        RepositoryContext repo,
        string operation,
        IReadOnlyList<string> arguments,
        int timeoutMs,
        CancellationToken cancellationToken,
        GitCredentialMaterial? credential = null)
    {
        var result = await RunGitRawAsync(
            repo.FullPath,
            arguments,
            timeoutMs,
            cancellationToken,
            credential);

        return new LocalGitCommandResult(
            operation,
            repo.RelativePath,
            result.ExitCode,
            result.ExitCode == 0 && !result.TimedOut,
            result.TimedOut,
            result.DurationMs,
            result.Output,
            result.OutputTruncated);
    }

    private static async Task<ProcessCapture> RunGitRawAsync(
        string workingDirectory,
        IReadOnlyList<string> arguments,
        int timeoutMs,
        CancellationToken cancellationToken,
        GitCredentialMaterial? credential = null)
    {
        var executable = FindGitExecutable()
            ?? throw new LocalGitRepositoryValidationException(
                "Không tìm thấy git executable.");

        var start = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        string? temporarySshKey = null;
        if (credential is not null)
        {
            if (credential.Kind == GitCredentialKinds.HttpsToken)
            {
                var username = string.IsNullOrWhiteSpace(credential.Username)
                    ? "x-access-token"
                    : credential.Username!;
                var basic = Convert.ToBase64String(
                    Encoding.UTF8.GetBytes($"{username}:{credential.Secret}"));

                start.Environment["GIT_CONFIG_COUNT"] = "1";
                start.Environment["GIT_CONFIG_KEY_0"] = "http.extraHeader";
                start.Environment["GIT_CONFIG_VALUE_0"] = $"Authorization: Basic {basic}";
                start.Environment["GIT_TERMINAL_PROMPT"] = "0";
            }
            else if (credential.Kind == GitCredentialKinds.SshPrivateKey)
            {
                temporarySshKey = Path.Combine(
                    Path.GetTempPath(),
                    $"personalai-git-{Guid.NewGuid():N}.key");
                await File.WriteAllTextAsync(
                    temporarySshKey,
                    credential.Secret,
                    Encoding.UTF8,
                    cancellationToken);

                if (!OperatingSystem.IsWindows())
                {
                    File.SetUnixFileMode(
                        temporarySshKey,
                        UnixFileMode.UserRead | UnixFileMode.UserWrite);
                }

                start.Environment["GIT_SSH_COMMAND"] =
                    $"ssh -i \"{temporarySshKey}\" -o IdentitiesOnly=yes";
                start.Environment["GIT_TERMINAL_PROMPT"] = "0";
            }
            else
            {
                throw new LocalGitRepositoryValidationException(
                    "Loại Git credential chưa được hỗ trợ.");
            }
        }

        start.ArgumentList.Add("-c");
        start.ArgumentList.Add("core.fsmonitor=false");
        start.ArgumentList.Add("-c");
        start.ArgumentList.Add("color.ui=false");

        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = start };
        var started = Stopwatch.StartNew();

        try
        {
            if (!process.Start())
                throw new LocalGitRepositoryValidationException(
                    "Không khởi động được git process.");
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or
            System.ComponentModel.Win32Exception)
        {
            throw new LocalGitRepositoryValidationException(
                $"Không khởi động được git: {exception.Message}");
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(timeoutMs);
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

        var truncated = output.Length > MaximumOutputCharacters;
        if (truncated)
            output = output[..MaximumOutputCharacters];

        started.Stop();

        if (!string.IsNullOrWhiteSpace(temporarySshKey))
        {
            try { File.Delete(temporarySshKey); } catch { }
        }

        return new ProcessCapture(
            timedOut ? -1 : process.ExitCode,
            timedOut,
            (int)Math.Min(int.MaxValue, started.ElapsedMilliseconds),
            output,
            truncated);
    }

    private static void EnsureSucceeded(ProcessCapture result, string message)
    {
        if (result.TimedOut || result.ExitCode != 0)
            throw new LocalGitRepositoryValidationException(
                $"{message} {LimitInline(result.Output, 500)}".Trim());
    }

    private static IReadOnlyList<string> SplitLines(string value) =>
        value.Split(
                ['\r', '\n'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    private static void EnsureInsideRoot(string root, string path)
    {
        var normalizedRoot = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var normalizedPath = Path.GetFullPath(path);

        if (string.Equals(normalizedRoot, normalizedPath, PathComparison))
            return;

        var prefix = normalizedRoot + Path.DirectorySeparatorChar;
        if (!normalizedPath.StartsWith(prefix, PathComparison))
            throw new LocalGitRepositoryValidationException(
                "Repository/path phải nằm trong workspace hiện tại.");
    }

    private static void EnsureNoSymlinkTraversal(string root, string path)
    {
        var normalizedRoot = Path.GetFullPath(root);
        var normalizedPath = Path.GetFullPath(path);
        EnsureInsideRoot(normalizedRoot, normalizedPath);

        var relative = Path.GetRelativePath(normalizedRoot, normalizedPath);
        if (relative == ".") return;

        var current = normalizedRoot;
        foreach (var segment in relative.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if (!File.Exists(current) && !Directory.Exists(current))
                break;

            var attributes = File.GetAttributes(current);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
                throw new LocalGitRepositoryValidationException(
                    "Không cho phép Git operation đi qua symlink/reparse point.");
        }
    }

    private static string? FindGitExecutable()
    {
        var executable = OperatingSystem.IsWindows() ? "git.exe" : "git";
        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;

        foreach (var directory in path.Split(
            Path.PathSeparator,
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            try
            {
                var candidate = Path.Combine(directory, executable);
                if (File.Exists(candidate))
                    return candidate;
            }
            catch
            {
            }
        }

        return null;
    }

    private static string LimitInline(string value, int maximum) =>
        string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Length <= maximum
                ? value.Replace('\r', ' ').Replace('\n', ' ')
                : value[..maximum].Replace('\r', ' ').Replace('\n', ' ');

    private sealed record RepositoryContext(
        string FullPath,
        string RelativePath);

    private sealed record ProcessCapture(
        int ExitCode,
        bool TimedOut,
        int DurationMs,
        string Output,
        bool OutputTruncated);
}
