using System.Diagnostics;

namespace PersonalAI.Web.Services;

public sealed record ComputerBackendBenchmarkStatus(
    string Baseline,
    bool Ufo2Configured,
    bool Ufo2Ready,
    string Ufo2Reason,
    string? Ufo2Root,
    string PythonCommand,
    bool OpenClawConfigured,
    string OpenClawReason);

public sealed record ComputerBackendBenchmarkRequest(
    string Goal,
    string? TaskName = null);

public sealed record ComputerBackendBenchmarkRunResult(
    string Backend,
    string Goal,
    string TaskName,
    bool Started,
    bool Completed,
    int? ExitCode,
    long DurationMilliseconds,
    string StandardOutput,
    string StandardError,
    string? LogDirectory,
    string Summary);

public interface IComputerBackendBenchmarkService
{
    ComputerBackendBenchmarkStatus GetStatus();

    Task<ComputerBackendBenchmarkRunResult> RunUfo2Async(
        ComputerBackendBenchmarkRequest request,
        CancellationToken cancellationToken);
}

public sealed class ComputerBackendBenchmarkService(
    ComputerOperatorExecutionControl execution,
    ILogger<ComputerBackendBenchmarkService> logger)
    : IComputerBackendBenchmarkService
{
    private const int MaximumOutputCharacters = 40_000;
    private static readonly TimeSpan MaximumRunTime =
        TimeSpan.FromMinutes(5);

    private static readonly string[] SecretTerms =
    [
        "password", "mật khẩu", "otp", "2fa", "mã xác thực",
        "verification code", "api key", "secret", "access token",
        "refresh token", "private key"
    ];

    public ComputerBackendBenchmarkStatus GetStatus()
    {
        var root = NormalizeRoot(
            Environment.GetEnvironmentVariable("PERSONALAI_UFO2_ROOT"));
        var python = NormalizeCommand(
            Environment.GetEnvironmentVariable("PERSONALAI_UFO2_PYTHON"),
            "python");

        var configured = !string.IsNullOrWhiteSpace(root);
        var ready = configured &&
                    Directory.Exists(root) &&
                    Directory.Exists(Path.Combine(root, "ufo"));

        var reason = ready
            ? "UFO² đã được cấu hình và tìm thấy thư mục ufo."
            : !configured
                ? "Chưa đặt PERSONALAI_UFO2_ROOT."
                : !Directory.Exists(root)
                    ? "PERSONALAI_UFO2_ROOT không tồn tại."
                    : "Không tìm thấy thư mục ufo trong PERSONALAI_UFO2_ROOT.";

        var openClawCommand =
            Environment.GetEnvironmentVariable("PERSONALAI_OPENCLAW_COMMAND");

        return new(
            Baseline: "AI-Ca-Nhan v4.0.25 Universal Reliable Operator",
            Ufo2Configured: configured,
            Ufo2Ready: ready,
            Ufo2Reason: reason,
            Ufo2Root: root,
            PythonCommand: python,
            OpenClawConfigured:
                !string.IsNullOrWhiteSpace(openClawCommand),
            OpenClawReason:
                string.IsNullOrWhiteSpace(openClawCommand)
                    ? "OpenClaw chưa được cấu hình cho benchmark; PoC đầu tiên ưu tiên UFO²."
                    : "Đã có lệnh OpenClaw cấu hình; adapter chạy task sẽ được bật sau khi UFO² PoC ổn định.");
    }

    public async Task<ComputerBackendBenchmarkRunResult> RunUfo2Async(
        ComputerBackendBenchmarkRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var goal = (request.Goal ?? string.Empty).Trim();
        if (goal.Length is < 2 or > 1200)
            throw new ToolExecutionInputException(
                "Mục tiêu benchmark phải từ 2 đến 1200 ký tự.");

        if (SecretTerms.Any(term =>
                goal.Contains(
                    term,
                    StringComparison.OrdinalIgnoreCase)))
            throw new ToolExecutionInputException(
                "Benchmark không nhận mật khẩu, OTP, token, khóa hoặc bí mật.");

        if (execution.Running)
            throw new ToolExecutionInputException(
                "Computer Operator hiện đang chạy. Hãy dừng hoặc chờ hoàn tất trước khi chạy UFO² để tránh hai backend cùng điều khiển máy.");

        var status = GetStatus();
        if (!status.Ufo2Ready ||
            string.IsNullOrWhiteSpace(status.Ufo2Root))
            throw new ToolExecutionInputException(
                status.Ufo2Reason);

        var taskName = NormalizeTaskName(
            request.TaskName,
            DateTimeOffset.Now);

        var startInfo = new ProcessStartInfo
        {
            FileName = status.PythonCommand,
            WorkingDirectory = status.Ufo2Root,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = false
        };

        startInfo.ArgumentList.Add("-m");
        startInfo.ArgumentList.Add("ufo");
        startInfo.ArgumentList.Add("--task");
        startInfo.ArgumentList.Add(taskName);
        startInfo.ArgumentList.Add("-r");
        startInfo.ArgumentList.Add(goal);

        using var process = new Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true
        };

        var stopwatch = Stopwatch.StartNew();

        try
        {
            if (!process.Start())
            {
                return new(
                    "ufo2",
                    goal,
                    taskName,
                    Started: false,
                    Completed: false,
                    ExitCode: null,
                    DurationMilliseconds: stopwatch.ElapsedMilliseconds,
                    StandardOutput: string.Empty,
                    StandardError: string.Empty,
                    LogDirectory: null,
                    Summary: "Không khởi động được tiến trình UFO².");
            }

            var stdoutTask =
                process.StandardOutput.ReadToEndAsync(
                    cancellationToken);
            var stderrTask =
                process.StandardError.ReadToEndAsync(
                    cancellationToken);

            using var timeout =
                CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken);
            timeout.CancelAfter(MaximumRunTime);

            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
                when (!cancellationToken.IsCancellationRequested)
            {
                TryKill(process);

                var timedOutOutput =
                    await SafeReadAsync(stdoutTask);
                var timedOutError =
                    await SafeReadAsync(stderrTask);

                stopwatch.Stop();

                return new(
                    "ufo2",
                    goal,
                    taskName,
                    Started: true,
                    Completed: false,
                    ExitCode: null,
                    DurationMilliseconds: stopwatch.ElapsedMilliseconds,
                    StandardOutput: Limit(timedOutOutput),
                    StandardError: Limit(timedOutError),
                    LogDirectory: FindLogDirectory(
                        status.Ufo2Root,
                        taskName),
                    Summary:
                        "UFO² vượt quá giới hạn benchmark 5 phút và đã được dừng. Không tự coi đây là thất bại của action cuối.");
            }

            var output =
                await SafeReadAsync(stdoutTask);
            var error =
                await SafeReadAsync(stderrTask);

            stopwatch.Stop();

            var success = process.ExitCode == 0;

            return new(
                "ufo2",
                goal,
                taskName,
                Started: true,
                Completed: success,
                ExitCode: process.ExitCode,
                DurationMilliseconds: stopwatch.ElapsedMilliseconds,
                StandardOutput: Limit(output),
                StandardError: Limit(error),
                LogDirectory: FindLogDirectory(
                    status.Ufo2Root,
                    taskName),
                Summary: success
                    ? "UFO² kết thúc tiến trình với exit code 0. Cần đối chiếu log/screenshot để xác nhận mục tiêu thực sự đạt."
                    : $"UFO² kết thúc với exit code {process.ExitCode}. Cần đọc log để xác định nguyên nhân.");
        }
        catch (Exception exception)
            when (exception is not OperationCanceledException)
        {
            stopwatch.Stop();

            logger.LogWarning(
                exception,
                "PoC UFO² không khởi chạy được.");

            return new(
                "ufo2",
                goal,
                taskName,
                Started: false,
                Completed: false,
                ExitCode: null,
                DurationMilliseconds: stopwatch.ElapsedMilliseconds,
                StandardOutput: string.Empty,
                StandardError: Limit(exception.Message),
                LogDirectory: FindLogDirectory(
                    status.Ufo2Root,
                    taskName),
                Summary:
                    "Không chạy được UFO². Kiểm tra Python, dependency và cấu hình UFO².");
        }
    }

    private static string? NormalizeRoot(
        string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        try
        {
            return Path.GetFullPath(
                Environment.ExpandEnvironmentVariables(
                    value.Trim()));
        }
        catch
        {
            return null;
        }
    }

    private static string NormalizeCommand(
        string? configured,
        string fallback) =>
        string.IsNullOrWhiteSpace(configured)
            ? fallback
            : configured.Trim();

    private static string NormalizeTaskName(
        string? requested,
        DateTimeOffset now)
    {
        var raw = string.IsNullOrWhiteSpace(requested)
            ? $"personalai-benchmark-{now:yyyyMMdd-HHmmss}"
            : requested.Trim();

        var safe = new string(
            raw.Select(ch =>
                    char.IsLetterOrDigit(ch) ||
                    ch is '-' or '_'
                        ? ch
                        : '-')
                .Take(80)
                .ToArray());

        return string.IsNullOrWhiteSpace(safe)
            ? $"personalai-benchmark-{now:yyyyMMdd-HHmmss}"
            : safe;
    }

    private static string? FindLogDirectory(
        string root,
        string taskName)
    {
        var candidates = new[]
        {
            Path.Combine(root, "logs", taskName),
            Path.Combine(root, "ufo", "logs", taskName)
        };

        return candidates.FirstOrDefault(
            Directory.Exists);
    }

    private static async Task<string> SafeReadAsync(
        Task<string> task)
    {
        try
        {
            return await task;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static void TryKill(
        Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(
                    entireProcessTree: true);
        }
        catch
        {
            // Best effort: timeout/cancel không được biến thành replay hay action mới.
        }
    }

    private static string Limit(
        string? value)
    {
        var text = value ?? string.Empty;
        return text.Length <= MaximumOutputCharacters
            ? text
            : text[..MaximumOutputCharacters] +
              "\n...[đã cắt bớt log benchmark]...";
    }
}
