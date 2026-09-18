using System.Diagnostics;
using System.Text;
using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public interface IDevelopmentAgentService
{
    DevelopmentStatusResponse GetStatus();

    DevelopmentWorkspaceInspection InspectWorkspace();

    Task<DevelopmentSearchResult> SearchTextAsync(
        string query,
        bool caseSensitive,
        int maximumHits,
        CancellationToken cancellationToken = default);

    Task<DevelopmentGitResult> GitStatusAsync(
        string repositoryPath,
        CancellationToken cancellationToken = default);

    Task<DevelopmentGitResult> GitDiffAsync(
        string repositoryPath,
        bool staged,
        CancellationToken cancellationToken = default);

    Task<DevelopmentProcessResult> DotnetRestoreAsync(
        string targetPath,
        CancellationToken cancellationToken = default);

    Task<DevelopmentProcessResult> DotnetBuildAsync(
        string targetPath,
        string configuration,
        CancellationToken cancellationToken = default);

    Task<DevelopmentProcessResult> DotnetTestAsync(
        string targetPath,
        string configuration,
        CancellationToken cancellationToken = default);
}

public sealed class DevelopmentAgentService(
    IWorkspaceFileService workspaceFiles,
    IWorkspaceContextAccessor workspaceContext,
    ILogger<DevelopmentAgentService> logger) : IDevelopmentAgentService
{
    public const int MaximumScannedFiles = 2_000;
    public const int MaximumSearchFiles = 400;
    public const int MaximumSearchHits = 100;
    public const int MaximumSearchPreviewCharacters = 260;
    public const int MaximumProcessOutputCharacters = 48_000;
    public const int MaximumSearchFileBytes = 512 * 1024;
    public const int GitTimeoutMs = 15_000;
    public const int DotnetRestoreTimeoutMs = 110_000;
    public const int DotnetBuildTimeoutMs = 110_000;
    public const int DotnetTestTimeoutMs = 170_000;

    private static readonly StringComparer PathComparer =
        OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

    private static readonly HashSet<string> IgnoredDirectories =
        new(
            [
                ".git",
                ".vs",
                ".idea",
                ".vscode",
                "bin",
                "obj",
                "node_modules",
                "dist",
                "build",
                "coverage",
                ".next",
                ".nuxt",
                "target",
                "__pycache__"
            ],
            StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> SearchableExtensions =
        new(
            [
                ".cs", ".fs", ".vb", ".cshtml", ".razor",
                ".js", ".mjs", ".cjs", ".ts", ".tsx", ".jsx",
                ".json", ".jsonc", ".xml", ".yml", ".yaml",
                ".md", ".txt", ".html", ".htm", ".css", ".scss",
                ".py", ".go", ".rs", ".java", ".kt", ".kts",
                ".c", ".h", ".cpp", ".hpp", ".sql", ".toml",
                ".props", ".targets", ".sln", ".slnx", ".csproj",
                ".fsproj", ".vbproj", ".gradle", ".sh", ".ps1"
            ],
            StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> DotnetTargetExtensions =
        new(
            [".sln", ".slnx", ".csproj", ".fsproj", ".vbproj"],
            StringComparer.OrdinalIgnoreCase);

    public DevelopmentStatusResponse GetStatus() =>
        new(
            PersonalAiRelease.Version,
            workspaceContext.CurrentWorkspaceId,
            GitAvailable: FindExecutable("git") is not null,
            DotnetAvailable: FindExecutable("dotnet") is not null,
            ArbitraryShellEnabled: false,
            ArbitraryProcessEnabled: false,
            GitWriteActionsEnabled: false,
            MaximumScannedFiles,
            MaximumSearchHits,
            MaximumProcessOutputCharacters,
            [
                DevelopmentCapabilities.WorkspaceInspect,
                DevelopmentCapabilities.TextSearch,
                DevelopmentCapabilities.GitStatus,
                DevelopmentCapabilities.GitDiff,
                DevelopmentCapabilities.DotnetRestore,
                DevelopmentCapabilities.DotnetBuild,
                DevelopmentCapabilities.DotnetTest
            ],
            [
                "Không có generic shell, command string, cmd /c hoặc PowerShell execution.",
                "Không có git add/commit/push/pull/reset/checkout/merge/rebase.",
                "Chỉ dotnet restore/build/test với target project/solution nằm trong workspace.",
                "Không nhận arbitrary process arguments từ tool input.",
                "dotnet build/test có thể thực thi MSBuild targets hoặc test code của project; vì vậy luôn cần xác nhận.",
                "Source search bỏ qua .git, bin, obj, node_modules và các build-output directory phổ biến.",
                "Mọi source/process output của development tools được coi là dữ liệu nhạy cảm."
            ]);

    public DevelopmentWorkspaceInspection InspectWorkspace()
    {
        var root = workspaceFiles.GetWorkspaceRoot();
        var files = EnumerateWorkspaceFiles(
            root,
            MaximumScannedFiles,
            out var truncated);

        var projects = files
            .Where(item => IsProjectManifest(item.FullPath))
            .Select(item => new DevelopmentProjectEntry(
                item.RelativePath,
                ProjectKind(item.FullPath),
                item.SizeBytes))
            .OrderBy(item => item.Path, PathComparer)
            .ToArray();

        var languages = DetectLanguages(files)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var rootFiles = files
            .Where(item =>
                !item.RelativePath.Contains('/'))
            .Select(item => item.RelativePath)
            .Order(PathComparer)
            .Take(80)
            .ToArray();

        return new DevelopmentWorkspaceInspection(
            workspaceContext.CurrentWorkspaceId,
            files.Count,
            truncated,
            projects,
            languages,
            rootFiles);
    }

    public async Task<DevelopmentSearchResult> SearchTextAsync(
        string query,
        bool caseSensitive,
        int maximumHits,
        CancellationToken cancellationToken = default)
    {
        var normalizedQuery = (query ?? string.Empty).Trim();
        if (normalizedQuery.Length is < 1 or > 200
            || normalizedQuery.IndexOf('\0') >= 0)
        {
            throw new ToolExecutionInputException(
                "Từ khóa tìm kiếm phải có từ 1 đến 200 ký tự.");
        }

        var safeMaximumHits = Math.Clamp(
            maximumHits,
            1,
            MaximumSearchHits);
        var root = workspaceFiles.GetWorkspaceRoot();
        var files = EnumerateWorkspaceFiles(
                root,
                MaximumSearchFiles,
                out var fileEnumerationTruncated)
            .Where(item =>
                item.SizeBytes <= MaximumSearchFileBytes
                && SearchableExtensions.Contains(
                    Path.GetExtension(item.FullPath)))
            .ToArray();

        var comparison = caseSensitive
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;
        var hits = new List<DevelopmentSearchHit>();
        var truncated = fileEnumerationTruncated;

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await using var stream = new FileStream(
                    file.FullPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite,
                    81920,
                    useAsync: true);
                using var reader = new StreamReader(
                    stream,
                    new UTF8Encoding(
                        encoderShouldEmitUTF8Identifier: false,
                        throwOnInvalidBytes: true),
                    detectEncodingFromByteOrderMarks: true);

                var lineNumber = 0;
                while (true)
                {
                    var line = await reader.ReadLineAsync(
                        cancellationToken);
                    if (line is null)
                    {
                        break;
                    }

                    lineNumber++;
                    if (line.IndexOf(
                        normalizedQuery,
                        comparison) < 0)
                    {
                        continue;
                    }

                    hits.Add(
                        new DevelopmentSearchHit(
                            file.RelativePath,
                            lineNumber,
                            LimitInline(
                                line.Trim(),
                                MaximumSearchPreviewCharacters)));

                    if (hits.Count >= safeMaximumHits)
                    {
                        truncated = true;
                        break;
                    }
                }
            }
            catch (DecoderFallbackException)
            {
                continue;
            }
            catch (IOException)
            {
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            if (hits.Count >= safeMaximumHits)
            {
                break;
            }
        }

        return new DevelopmentSearchResult(
            workspaceContext.CurrentWorkspaceId,
            normalizedQuery,
            files.Length,
            hits.Count,
            truncated,
            hits);
    }

    public async Task<DevelopmentGitResult> GitStatusAsync(
        string repositoryPath,
        CancellationToken cancellationToken = default)
    {
        EnsureExecutableAvailable("git");
        var directory = ResolveDirectory(
            repositoryPath,
            allowRoot: true);
        EnsureGitRepository(directory);

        var result = await RunProcessAsync(
            "git",
            [
                "-c",
                "core.fsmonitor=false",
                "status",
                "--short",
                "--branch",
                "--untracked-files=normal",
                "--ignore-submodules=all"
            ],
            directory,
            GitTimeoutMs,
            cancellationToken);

        return new DevelopmentGitResult(
            "git status",
            result.ExitCode,
            result.ExitCode == 0 && !result.TimedOut,
            result.DurationMs,
            result.Output,
            result.OutputTruncated);
    }

    public async Task<DevelopmentGitResult> GitDiffAsync(
        string repositoryPath,
        bool staged,
        CancellationToken cancellationToken = default)
    {
        EnsureExecutableAvailable("git");
        var directory = ResolveDirectory(
            repositoryPath,
            allowRoot: true);
        EnsureGitRepository(directory);

        var arguments = new List<string>
        {
            "-c",
            "core.fsmonitor=false",
            "diff",
            "--no-ext-diff",
            "--no-textconv",
            "--ignore-submodules=all",
            "--no-color",
            "--unified=3"
        };
        if (staged)
        {
            arguments.Add("--cached");
        }

        arguments.Add("--");

        var result = await RunProcessAsync(
            "git",
            arguments,
            directory,
            GitTimeoutMs,
            cancellationToken);

        return new DevelopmentGitResult(
            staged
                ? "git diff --cached"
                : "git diff",
            result.ExitCode,
            result.ExitCode == 0 && !result.TimedOut,
            result.DurationMs,
            result.Output,
            result.OutputTruncated);
    }

    public Task<DevelopmentProcessResult> DotnetRestoreAsync(
        string targetPath,
        CancellationToken cancellationToken = default) =>
        RunDotnetAsync(
            DevelopmentCapabilities.DotnetRestore,
            targetPath,
            [
                "restore",
                "--nologo",
                "--verbosity",
                "minimal"
            ],
            DotnetRestoreTimeoutMs,
            cancellationToken);

    public Task<DevelopmentProcessResult> DotnetBuildAsync(
        string targetPath,
        string configuration,
        CancellationToken cancellationToken = default)
    {
        var normalizedConfiguration =
            NormalizeConfiguration(configuration);

        return RunDotnetAsync(
            DevelopmentCapabilities.DotnetBuild,
            targetPath,
            [
                "build",
                "--no-restore",
                "--nologo",
                "--configuration",
                normalizedConfiguration,
                "--verbosity",
                "minimal"
            ],
            DotnetBuildTimeoutMs,
            cancellationToken);
    }

    public Task<DevelopmentProcessResult> DotnetTestAsync(
        string targetPath,
        string configuration,
        CancellationToken cancellationToken = default)
    {
        var normalizedConfiguration =
            NormalizeConfiguration(configuration);

        return RunDotnetAsync(
            DevelopmentCapabilities.DotnetTest,
            targetPath,
            [
                "test",
                "--no-restore",
                "--nologo",
                "--configuration",
                normalizedConfiguration,
                "--verbosity",
                "minimal",
                "--logger",
                "console;verbosity=minimal"
            ],
            DotnetTestTimeoutMs,
            cancellationToken);
    }

    private async Task<DevelopmentProcessResult> RunDotnetAsync(
        string tool,
        string targetPath,
        IReadOnlyList<string> arguments,
        int timeoutMs,
        CancellationToken cancellationToken)
    {
        EnsureExecutableAvailable("dotnet");
        var target = ResolveDotnetTarget(targetPath);
        var workingDirectory = Path.GetDirectoryName(target.FullPath)
            ?? workspaceFiles.GetWorkspaceRoot();

        var processArguments = new List<string>();
        if (arguments.Count > 0)
        {
            processArguments.Add(arguments[0]);
            processArguments.Add(target.FullPath);
            processArguments.AddRange(arguments.Skip(1));
        }

        var result = await RunProcessAsync(
            "dotnet",
            processArguments,
            workingDirectory,
            timeoutMs,
            cancellationToken);

        return new DevelopmentProcessResult(
            tool,
            target.RelativePath,
            result.ExitCode,
            result.ExitCode == 0 && !result.TimedOut,
            result.TimedOut,
            result.DurationMs,
            result.Output,
            result.OutputTruncated);
    }

    private async Task<ProcessRunResult> RunProcessAsync(
        string executable,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        int timeoutMs,
        CancellationToken cancellationToken)
    {
        var executablePath = FindExecutable(executable)
            ?? throw new ToolExecutionInputException(
                $"Không tìm thấy executable {executable} trong PATH.");

        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        startInfo.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        startInfo.Environment["DOTNET_NOLOGO"] = "1";
        startInfo.Environment["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1";
        startInfo.Environment["NUGET_XMLDOC_MODE"] = "skip";
        startInfo.Environment["CI"] = "true";
        startInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";
        startInfo.Environment["GIT_PAGER"] = "cat";
        startInfo.Environment["PAGER"] = "cat";
        startInfo.Environment["GIT_OPTIONAL_LOCKS"] = "0";

        using var process = new Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true
        };

        var output = new BoundedProcessOutput(
            MaximumProcessOutputCharacters);
        process.OutputDataReceived += (_, eventArgs) =>
            output.Append(eventArgs.Data);
        process.ErrorDataReceived += (_, eventArgs) =>
            output.Append(eventArgs.Data);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            if (!process.Start())
            {
                throw new ToolExecutionInputException(
                    $"Không thể khởi động {executable}.");
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            using var timeoutCts =
                CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken);
            timeoutCts.CancelAfter(timeoutMs);

            try
            {
                await process.WaitForExitAsync(
                    timeoutCts.Token);
                process.WaitForExit();
            }
            catch (OperationCanceledException) when (
                !cancellationToken.IsCancellationRequested)
            {
                TryKillProcessTree(process);
                stopwatch.Stop();
                return new ProcessRunResult(
                    -1,
                    TimedOut: true,
                    checked((int)Math.Min(
                        stopwatch.ElapsedMilliseconds,
                        int.MaxValue)),
                    SanitizeProcessOutput(
                        output.ToString()),
                    output.Truncated);
            }
            catch (OperationCanceledException)
            {
                TryKillProcessTree(process);
                throw;
            }

            stopwatch.Stop();
            return new ProcessRunResult(
                process.ExitCode,
                TimedOut: false,
                checked((int)Math.Min(
                    stopwatch.ElapsedMilliseconds,
                    int.MaxValue)),
                SanitizeProcessOutput(
                    output.ToString()),
                output.Truncated);
        }
        catch (System.ComponentModel.Win32Exception exception)
        {
            logger.LogWarning(
                exception,
                "Không thể khởi động development process {Executable}.",
                executable);
            throw new ToolExecutionInputException(
                $"Không thể khởi động {executable}.");
        }
    }

    private string SanitizeProcessOutput(
        string value)
    {
        var root = workspaceFiles.GetWorkspaceRoot();
        var normalizedRoot = root.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);

        var result = value.Replace(
            normalizedRoot,
            ".",
            OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal);

        return result.Trim();
    }

    private DotnetTarget ResolveDotnetTarget(
        string relativePath)
    {
        var fullPath = ResolvePathInsideWorkspace(
            relativePath,
            allowRoot: false);

        if (!File.Exists(fullPath))
        {
            throw new ToolExecutionInputException(
                "Không tìm thấy project/solution target trong workspace.");
        }

        var extension = Path.GetExtension(fullPath);
        if (!DotnetTargetExtensions.Contains(extension))
        {
            throw new ToolExecutionInputException(
                "dotnet tool chỉ nhận .sln, .slnx, .csproj, .fsproj hoặc .vbproj.");
        }

        EnsureNoSymlinkTraversal(
            workspaceFiles.GetWorkspaceRoot(),
            fullPath);

        return new DotnetTarget(
            fullPath,
            ToRelativePath(fullPath));
    }

    private string ResolveDirectory(
        string relativePath,
        bool allowRoot)
    {
        var fullPath = ResolvePathInsideWorkspace(
            string.IsNullOrWhiteSpace(relativePath)
                ? "."
                : relativePath,
            allowRoot);

        if (!Directory.Exists(fullPath))
        {
            throw new ToolExecutionInputException(
                "Không tìm thấy thư mục repository trong workspace.");
        }

        EnsureNoSymlinkTraversal(
            workspaceFiles.GetWorkspaceRoot(),
            fullPath);
        return fullPath;
    }

    private string ResolvePathInsideWorkspace(
        string relativePath,
        bool allowRoot)
    {
        var root = workspaceFiles.GetWorkspaceRoot();
        var value = (relativePath ?? string.Empty).Trim();

        if (value.Length == 0)
        {
            value = ".";
        }

        if (value.Length > 500
            || value.IndexOf('\0') >= 0
            || Path.IsPathRooted(value))
        {
            throw new ToolExecutionInputException(
                "Development path phải là đường dẫn tương đối trong workspace.");
        }

        var segments = value
            .Replace('\\', '/')
            .Split(
                '/',
                StringSplitOptions.RemoveEmptyEntries);
        if (segments.Any(segment =>
            segment == ".."))
        {
            throw new ToolExecutionInputException(
                "Development path không cho phép '..'.");
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(
                Path.Combine(
                    root,
                    value));
        }
        catch (Exception exception) when (
            exception is ArgumentException
                or NotSupportedException
                or PathTooLongException)
        {
            throw new ToolExecutionInputException(
                "Development path không hợp lệ.");
        }

        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        var rootWithSeparator = root.EndsWith(
            Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;

        if (!(fullPath.Equals(root, comparison)
            || fullPath.StartsWith(
                rootWithSeparator,
                comparison)))
        {
            throw new ToolExecutionInputException(
                "Development path nằm ngoài workspace.");
        }

        if (!allowRoot
            && fullPath.Equals(
                root,
                comparison))
        {
            throw new ToolExecutionInputException(
                "Cần chỉ định target bên trong workspace.");
        }

        return fullPath;
    }

    private static void EnsureGitRepository(
        string directory)
    {
        var marker = Path.Combine(
            directory,
            ".git");
        if (!Directory.Exists(marker))
        {
            throw new ToolExecutionInputException(
                "v1.4 chỉ cho Git repository có .git directory nằm trực tiếp trong workspace; gitfile/worktree ngoài workspace chưa được hỗ trợ.");
        }

        var info = new DirectoryInfo(marker);
        if (IsSymlink(info))
        {
            throw new ToolExecutionInputException(
                ".git directory không được là symlink/reparse point.");
        }
    }

    private static string NormalizeConfiguration(
        string value)
    {
        var normalized = string.IsNullOrWhiteSpace(value)
            ? "Debug"
            : value.Trim();

        if (!normalized.Equals(
                "Debug",
                StringComparison.OrdinalIgnoreCase)
            && !normalized.Equals(
                "Release",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ToolExecutionInputException(
                "Configuration chỉ được là Debug hoặc Release.");
        }

        return normalized.Equals(
            "Release",
            StringComparison.OrdinalIgnoreCase)
            ? "Release"
            : "Debug";
    }

    private static IReadOnlyList<WorkspaceFileEntry> EnumerateWorkspaceFiles(
        string root,
        int maximumFiles,
        out bool truncated)
    {
        var files = new List<WorkspaceFileEntry>();
        var stack = new Stack<(string Directory, int Depth)>();
        stack.Push((root, 0));
        truncated = false;

        while (stack.Count > 0)
        {
            var (directory, depth) = stack.Pop();
            if (depth > 14)
            {
                truncated = true;
                continue;
            }

            IEnumerable<string> entries;
            try
            {
                entries = Directory
                    .EnumerateFileSystemEntries(directory)
                    .OrderBy(
                        path => path,
                        PathComparer)
                    .ToArray();
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }
            catch (IOException)
            {
                continue;
            }

            foreach (var entry in entries)
            {
                FileSystemInfo info = Directory.Exists(entry)
                    ? new DirectoryInfo(entry)
                    : new FileInfo(entry);

                try
                {
                    if (IsSymlink(info))
                    {
                        continue;
                    }
                }
                catch (IOException)
                {
                    continue;
                }

                if (info is DirectoryInfo directoryInfo)
                {
                    if (!IgnoredDirectories.Contains(
                        directoryInfo.Name))
                    {
                        stack.Push((
                            directoryInfo.FullName,
                            depth + 1));
                    }

                    continue;
                }

                if (info is not FileInfo fileInfo)
                {
                    continue;
                }

                files.Add(
                    new WorkspaceFileEntry(
                        fileInfo.FullName,
                        Path.GetRelativePath(
                                root,
                                fileInfo.FullName)
                            .Replace('\\', '/'),
                        fileInfo.Length));

                if (files.Count >= maximumFiles)
                {
                    truncated = true;
                    return files;
                }
            }
        }

        return files;
    }

    private static IEnumerable<string> DetectLanguages(
        IReadOnlyList<WorkspaceFileEntry> files)
    {
        var result = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var file in files)
        {
            var extension = Path
                .GetExtension(file.FullPath)
                .ToLowerInvariant();

            var language = extension switch
            {
                ".cs" or ".cshtml" or ".razor" => "C# / .NET",
                ".fs" => "F# / .NET",
                ".vb" => "Visual Basic / .NET",
                ".js" or ".mjs" or ".cjs" or ".jsx" => "JavaScript",
                ".ts" or ".tsx" => "TypeScript",
                ".py" => "Python",
                ".go" => "Go",
                ".rs" => "Rust",
                ".java" => "Java",
                ".kt" or ".kts" => "Kotlin",
                ".c" or ".h" => "C",
                ".cpp" or ".hpp" => "C++",
                ".sql" => "SQL",
                ".html" or ".htm" => "HTML",
                ".css" or ".scss" => "CSS",
                _ => null
            };

            if (language is not null)
            {
                result.Add(language);
            }
        }

        return result;
    }

    private static bool IsProjectManifest(
        string fullPath)
    {
        var fileName = Path.GetFileName(fullPath);
        var extension = Path.GetExtension(fullPath);

        return DotnetTargetExtensions.Contains(extension)
            || fileName.Equals(
                "package.json",
                StringComparison.OrdinalIgnoreCase)
            || fileName.Equals(
                "pyproject.toml",
                StringComparison.OrdinalIgnoreCase)
            || fileName.Equals(
                "requirements.txt",
                StringComparison.OrdinalIgnoreCase)
            || fileName.Equals(
                "go.mod",
                StringComparison.OrdinalIgnoreCase)
            || fileName.Equals(
                "Cargo.toml",
                StringComparison.OrdinalIgnoreCase)
            || fileName.Equals(
                "pom.xml",
                StringComparison.OrdinalIgnoreCase)
            || fileName.Equals(
                "build.gradle",
                StringComparison.OrdinalIgnoreCase)
            || fileName.Equals(
                "build.gradle.kts",
                StringComparison.OrdinalIgnoreCase);
    }

    private static string ProjectKind(
        string fullPath)
    {
        var fileName = Path.GetFileName(fullPath);
        var extension = Path.GetExtension(fullPath)
            .ToLowerInvariant();

        if (extension is ".sln" or ".slnx")
        {
            return "dotnet-solution";
        }

        if (extension is ".csproj" or ".fsproj" or ".vbproj")
        {
            return "dotnet-project";
        }

        return fileName.ToLowerInvariant() switch
        {
            "package.json" => "node",
            "pyproject.toml" or "requirements.txt" => "python",
            "go.mod" => "go",
            "cargo.toml" => "rust",
            "pom.xml" or "build.gradle" or "build.gradle.kts" => "jvm",
            _ => "project"
        };
    }

    private static string? FindExecutable(
        string name)
    {
        var path = Environment.GetEnvironmentVariable(
            "PATH");
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var candidateNames = new List<string> { name };
        if (OperatingSystem.IsWindows())
        {
            var pathExt = Environment.GetEnvironmentVariable(
                "PATHEXT")
                ?? ".EXE;.CMD;.BAT";
            foreach (var extension in pathExt.Split(
                ';',
                StringSplitOptions.RemoveEmptyEntries))
            {
                candidateNames.Add(
                    name.EndsWith(
                        extension,
                        StringComparison.OrdinalIgnoreCase)
                        ? name
                        : name + extension.ToLowerInvariant());
            }
        }

        foreach (var directory in path.Split(
            Path.PathSeparator,
            StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = directory.Trim().Trim('"');
            if (trimmed.Length == 0)
            {
                continue;
            }

            foreach (var candidateName in candidateNames)
            {
                try
                {
                    var candidate = Path.Combine(
                        trimmed,
                        candidateName);
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                }
                catch
                {
                    // Skip malformed PATH entries.
                }
            }
        }

        return null;
    }

    private static void EnsureExecutableAvailable(
        string executable)
    {
        if (FindExecutable(executable) is null)
        {
            throw new ToolExecutionInputException(
                $"Không tìm thấy {executable} trong PATH của PersonalAI.");
        }
    }

    private static void EnsureNoSymlinkTraversal(
        string root,
        string fullPath)
    {
        var relative = Path.GetRelativePath(
            root,
            fullPath);
        if (relative == ".")
        {
            return;
        }

        var current = root;
        foreach (var segment in relative.Split(
            Path.DirectorySeparatorChar,
            StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(
                current,
                segment);
            FileSystemInfo? info = Directory.Exists(current)
                ? new DirectoryInfo(current)
                : File.Exists(current)
                    ? new FileInfo(current)
                    : null;

            if (info is not null
                && IsSymlink(info))
            {
                throw new ToolExecutionInputException(
                    "Development tools không đi qua symlink/reparse point.");
            }
        }
    }

    private static bool IsSymlink(
        FileSystemInfo info) =>
        info.LinkTarget is not null
        || (info.Attributes
            & FileAttributes.ReparsePoint) != 0;

    private string ToRelativePath(
        string fullPath) =>
        Path.GetRelativePath(
                workspaceFiles.GetWorkspaceRoot(),
                fullPath)
            .Replace('\\', '/');

    private static string LimitInline(
        string value,
        int maximum)
    {
        var normalized = string.Join(
            " ",
            (value ?? string.Empty)
                .Split(
                    [' ', '\t', '\r', '\n'],
                    StringSplitOptions.RemoveEmptyEntries));

        return normalized.Length <= maximum
            ? normalized
            : normalized[..Math.Max(
                0,
                maximum - 1)] + "…";
    }

    private static void TryKillProcessTree(
        Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(
                    entireProcessTree: true);
            }
        }
        catch
        {
            // Best effort cleanup.
        }
    }

    private sealed class BoundedProcessOutput(
        int maximumCharacters)
    {
        private readonly object _gate = new();
        private readonly StringBuilder _builder = new();

        public bool Truncated { get; private set; }

        public void Append(
            string? line)
        {
            if (line is null)
            {
                return;
            }

            lock (_gate)
            {
                if (_builder.Length >= maximumCharacters)
                {
                    Truncated = true;
                    return;
                }

                var remaining =
                    maximumCharacters
                    - _builder.Length;
                var addition = line.Length <= remaining
                    ? line
                    : line[..remaining];

                _builder.Append(addition);
                if (_builder.Length < maximumCharacters)
                {
                    _builder.AppendLine();
                }

                if (addition.Length < line.Length)
                {
                    Truncated = true;
                }
            }
        }

        public override string ToString()
        {
            lock (_gate)
            {
                return _builder.ToString();
            }
        }
    }

    private sealed record WorkspaceFileEntry(
        string FullPath,
        string RelativePath,
        long SizeBytes);

    private sealed record DotnetTarget(
        string FullPath,
        string RelativePath);

    private sealed record ProcessRunResult(
        int ExitCode,
        bool TimedOut,
        int DurationMs,
        string Output,
        bool OutputTruncated);
}
