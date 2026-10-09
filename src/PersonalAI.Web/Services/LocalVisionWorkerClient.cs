using System.Diagnostics;
using System.Text.Json;

namespace PersonalAI.Web.Services;

public sealed record LocalVisionWorkerRequest(
    string Operation,
    string? JpegBase64 = null,
    string? TemplateBase64 = null,
    double MinimumScore = 0,
    string? RequestId = null);

public sealed record LocalVisionWorkerWord(
    string? Text,
    double Left,
    double Top,
    double Width,
    double Height);

public sealed record LocalVisionWorkerLine(
    string? Text,
    LocalVisionWorkerWord[]? Words);

public sealed record LocalVisionWorkerMatch(
    bool Matched,
    bool Ambiguous,
    double Score,
    double SecondBestScore,
    int Left,
    int Top,
    int Width,
    int Height,
    double Scale);

public sealed record LocalVisionWorkerResponse(
    bool Success,
    string? Detail,
    string? Text,
    LocalVisionWorkerLine[]? Lines,
    LocalVisionWorkerMatch? Match,
    string? RequestId = null);

public sealed record LocalVisionWorkerInvocation(
    bool Success,
    bool TimedOut,
    LocalVisionWorkerResponse? Response,
    string Detail);

public interface ILocalVisionWorkerClient
{
    LocalVisionWorkerInvocation Invoke(
        LocalVisionWorkerRequest request,
        int timeoutMilliseconds);
}

public sealed class LocalVisionWorkerClient
    : ILocalVisionWorkerClient,
      IDisposable
{
    private const int MaximumRequestsPerWorker = 128;
    private static readonly TimeSpan MaximumWorkerAge =
        TimeSpan.FromMinutes(30);
    private static readonly TimeSpan IdleRecycleAfter =
        TimeSpan.FromMinutes(2);

    private readonly object sync =
        new();

    private Process? process;
    private WorkerLaunch? activeLaunch;
    private Task<string>? stderrDrain;
    private DateTimeOffset startedAt;
    private DateTimeOffset lastUsedAt;
    private int requestCount;
    private bool disposed;

    public LocalVisionWorkerInvocation Invoke(
        LocalVisionWorkerRequest request,
        int timeoutMilliseconds)
    {
        ArgumentNullException.ThrowIfNull(request);

        lock (sync)
        {
            if (disposed)
            {
                return new(
                    false,
                    false,
                    null,
                    "Local vision worker client đã được dispose.");
            }

            var worker =
                ResolveWorker();

            if (worker is null)
            {
                return new(
                    false,
                    false,
                    null,
                    "Không tìm thấy PersonalAI.LocalVisionWorker trong output directory.");
            }

            var timeout =
                NormalizeTimeout(
                    timeoutMilliseconds);

            var ready =
                EnsureWorkerReady(
                    worker,
                    timeout);

            if (!ready.Success)
                return ready;

            return InvokePersistent(
                request,
                timeout,
                countRequest: true);
        }
    }

    internal static LocalVisionWorkerInvocation InvokeProcessForAcceptance(
        string executable,
        string arguments,
        string request,
        int timeoutMilliseconds) =>
        InvokeOneShotProcess(
            new WorkerLaunch(
                executable,
                arguments),
            request,
            timeoutMilliseconds);

    internal static int NormalizeTimeoutForAcceptance(
        int timeoutMilliseconds) =>
        NormalizeTimeout(
            timeoutMilliseconds);

    internal static bool ShouldRecycleForAcceptance(
        int requestCount,
        TimeSpan age,
        TimeSpan idle) =>
        requestCount >=
            MaximumRequestsPerWorker ||
        age >=
            MaximumWorkerAge ||
        idle >=
            IdleRecycleAfter;

    public void Dispose()
    {
        lock (sync)
        {
            if (disposed)
                return;

            disposed = true;
            StopWorker();
        }
    }

    private LocalVisionWorkerInvocation EnsureWorkerReady(
        WorkerLaunch worker,
        int timeoutMilliseconds)
    {
        if (process is not null &&
            (!IsWorkerAlive() ||
             !Equals(
                 activeLaunch,
                 worker) ||
             ShouldRecycleCurrentWorker()))
        {
            StopWorker();
        }

        if (process is not null)
        {
            return new(
                true,
                false,
                null,
                "Local vision warm worker đang hoạt động.");
        }

        var started =
            StartWorker(
                worker);

        if (!started.Success)
            return started;

        var heartbeatTimeout =
            Math.Min(
                timeoutMilliseconds,
                2000);

        var heartbeat =
            InvokePersistent(
                new LocalVisionWorkerRequest(
                    Operation: "heartbeat"),
                heartbeatTimeout,
                countRequest: false);

        if (!heartbeat.Success)
        {
            StopWorker();
            return new(
                false,
                heartbeat.TimedOut,
                heartbeat.Response,
                $"Local vision worker heartbeat thất bại: {heartbeat.Detail}");
        }

        return new(
            true,
            false,
            heartbeat.Response,
            "Local vision warm worker đã khởi động và heartbeat thành công.");
    }

    private LocalVisionWorkerInvocation StartWorker(
        WorkerLaunch worker)
    {
        var candidate =
            new Process
            {
                StartInfo =
                    new ProcessStartInfo
                    {
                        FileName =
                            worker.Executable,
                        Arguments =
                            worker.Arguments,
                        RedirectStandardInput = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
            };

        try
        {
            if (!candidate.Start())
            {
                candidate.Dispose();
                return new(
                    false,
                    false,
                    null,
                    "Không khởi động được local vision worker.");
            }

            process = candidate;
            activeLaunch = worker;
            stderrDrain =
                candidate.StandardError.ReadToEndAsync();
            startedAt =
                DateTimeOffset.UtcNow;
            lastUsedAt =
                startedAt;
            requestCount = 0;

            return new(
                true,
                false,
                null,
                "Đã khởi động local vision warm worker.");
        }
        catch (Exception exception) when (
            exception is
                InvalidOperationException or
                System.ComponentModel.Win32Exception or
                IOException)
        {
            candidate.Dispose();
            return new(
                false,
                false,
                null,
                $"Không gọi được local vision worker: {exception.GetType().Name}: {exception.Message}");
        }
    }

    private LocalVisionWorkerInvocation InvokePersistent(
        LocalVisionWorkerRequest request,
        int timeoutMilliseconds,
        bool countRequest)
    {
        var current =
            process;

        if (current is null ||
            !IsWorkerAlive())
        {
            StopWorker();
            return new(
                false,
                false,
                null,
                "Local vision worker không còn hoạt động.");
        }

        var requestId =
            string.IsNullOrWhiteSpace(
                request.RequestId)
                ? Guid.NewGuid()
                    .ToString("N")
                : request.RequestId.Trim();

        var payload =
            JsonSerializer.Serialize(
                request with
                {
                    RequestId =
                        requestId
                },
                JsonOptions());

        try
        {
            current.StandardInput.WriteLine(
                payload);
            current.StandardInput.Flush();

            var outputTask =
                current.StandardOutput
                    .ReadLineAsync();

            if (!outputTask.Wait(
                    timeoutMilliseconds))
            {
                StopWorker();

                return new(
                    false,
                    true,
                    null,
                    $"Local vision worker request {requestId} vượt timeout {timeoutMilliseconds}ms; process đã bị terminate và sẽ restart ở request sau.");
            }

            var output =
                outputTask
                    .GetAwaiter()
                    .GetResult();

            if (string.IsNullOrWhiteSpace(
                    output))
            {
                var error =
                    SnapshotWorkerError();

                StopWorker();

                return new(
                    false,
                    false,
                    null,
                    $"Local vision worker kết thúc stream mà không trả response. {error}".Trim());
            }

            LocalVisionWorkerResponse? response;
            try
            {
                response =
                    JsonSerializer.Deserialize<LocalVisionWorkerResponse>(
                        output,
                        JsonOptions());
            }
            catch (JsonException exception)
            {
                StopWorker();

                return new(
                    false,
                    false,
                    null,
                    $"Local vision worker trả JSON không hợp lệ: {exception.Message}");
            }

            if (response is null)
            {
                StopWorker();

                return new(
                    false,
                    false,
                    null,
                    "Local vision worker không trả response.");
            }

            if (!string.Equals(
                    response.RequestId,
                    requestId,
                    StringComparison.Ordinal))
            {
                StopWorker();

                return new(
                    false,
                    false,
                    response,
                    $"Local vision worker protocol mismatch: expected requestId={requestId}, actual={response.RequestId ?? "<null>"}. Worker đã bị restart để tránh ghép nhầm response.");
            }

            lastUsedAt =
                DateTimeOffset.UtcNow;

            if (countRequest)
            {
                requestCount++;
            }

            return new(
                response.Success,
                false,
                response,
                response.Success
                    ? response.Detail ?? "Worker hoàn tất."
                    : response.Detail ?? "Worker báo thất bại.");
        }
        catch (Exception exception) when (
            exception is
                InvalidOperationException or
                IOException or
                ObjectDisposedException)
        {
            StopWorker();

            return new(
                false,
                false,
                null,
                $"Mất kết nối tới local vision worker: {exception.GetType().Name}: {exception.Message}");
        }
    }

    private bool ShouldRecycleCurrentWorker()
    {
        if (process is null)
            return false;

        var now =
            DateTimeOffset.UtcNow;

        return ShouldRecycleForAcceptance(
            requestCount,
            now - startedAt,
            now - lastUsedAt);
    }

    private bool IsWorkerAlive()
    {
        try
        {
            return process is not null &&
                   !process.HasExited;
        }
        catch (
            InvalidOperationException)
        {
            return false;
        }
    }

    private string SnapshotWorkerError()
    {
        try
        {
            if (stderrDrain is not null &&
                stderrDrain.IsCompletedSuccessfully)
            {
                return stderrDrain.Result.Trim();
            }
        }
        catch
        {
            // Diagnostic best effort.
        }

        return string.Empty;
    }

    private void StopWorker()
    {
        var current =
            process;

        process = null;
        activeLaunch = null;

        if (current is null)
            return;

        try
        {
            if (!current.HasExited)
            {
                try
                {
                    current.StandardInput.Close();
                }
                catch
                {
                    // Best effort graceful close.
                }

                if (!current.WaitForExit(
                        150))
                {
                    current.Kill(
                        entireProcessTree: true);
                }
            }
        }
        catch
        {
            try
            {
                current.Kill(
                    entireProcessTree: true);
            }
            catch
            {
                // Best effort isolation cleanup.
            }
        }
        finally
        {
            current.Dispose();
            stderrDrain = null;
            requestCount = 0;
            startedAt = default;
            lastUsedAt = default;
        }
    }

    private static LocalVisionWorkerInvocation InvokeOneShotProcess(
        WorkerLaunch worker,
        string request,
        int timeoutMilliseconds)
    {
        using var process =
            new Process
            {
                StartInfo =
                    new ProcessStartInfo
                    {
                        FileName =
                            worker.Executable,
                        Arguments =
                            worker.Arguments,
                        RedirectStandardInput = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
            };

        var started =
            false;

        try
        {
            if (!process.Start())
            {
                return new(
                    false,
                    false,
                    null,
                    "Không khởi động được local vision worker.");
            }

            started =
                true;

            var stdoutTask =
                process.StandardOutput.ReadToEndAsync();
            var stderrTask =
                process.StandardError.ReadToEndAsync();

            process.StandardInput.Write(
                request);
            process.StandardInput.Close();

            var timeout =
                NormalizeTimeout(
                    timeoutMilliseconds);

            if (!process.WaitForExit(
                    timeout))
            {
                try
                {
                    process.Kill(
                        entireProcessTree: true);
                }
                catch
                {
                    // Best effort; timeout result vẫn được trả.
                }

                return new(
                    false,
                    true,
                    null,
                    $"Local vision worker vượt timeout {timeout}ms và đã bị terminate.");
            }

            var output =
                stdoutTask
                    .GetAwaiter()
                    .GetResult();

            var error =
                stderrTask
                    .GetAwaiter()
                    .GetResult();

            if (process.ExitCode != 0)
            {
                return new(
                    false,
                    false,
                    null,
                    $"Local vision worker exit code={process.ExitCode}. {error}".Trim());
            }

            LocalVisionWorkerResponse? response;
            try
            {
                response =
                    JsonSerializer.Deserialize<LocalVisionWorkerResponse>(
                        output,
                        JsonOptions());
            }
            catch (JsonException exception)
            {
                return new(
                    false,
                    false,
                    null,
                    $"Local vision worker trả JSON không hợp lệ: {exception.Message}");
            }

            if (response is null)
            {
                return new(
                    false,
                    false,
                    null,
                    "Local vision worker không trả response.");
            }

            return new(
                response.Success,
                false,
                response,
                response.Success
                    ? response.Detail ?? "Worker hoàn tất."
                    : response.Detail ?? "Worker báo thất bại.");
        }
        catch (Exception exception) when (
            exception is
                InvalidOperationException or
                System.ComponentModel.Win32Exception or
                IOException)
        {
            return new(
                false,
                false,
                null,
                $"Không gọi được local vision worker: {exception.GetType().Name}: {exception.Message}");
        }
        finally
        {
            if (started &&
                !process.HasExited)
            {
                try
                {
                    process.Kill(
                        entireProcessTree: true);
                }
                catch
                {
                    // Best effort isolation cleanup.
                }
            }
        }
    }

    private static int NormalizeTimeout(
        int timeoutMilliseconds) =>
        Math.Clamp(
            timeoutMilliseconds,
            250,
            15000);

    private static WorkerLaunch? ResolveWorker()
    {
        var directory =
            Path.Combine(
                AppContext.BaseDirectory,
                "local-vision-worker");

        var exe =
            Path.Combine(
                directory,
                "PersonalAI.LocalVisionWorker.exe");

        if (File.Exists(exe))
        {
            return new(
                exe,
                string.Empty);
        }

        var dll =
            Path.Combine(
                directory,
                "PersonalAI.LocalVisionWorker.dll");

        if (File.Exists(dll))
        {
            return new(
                "dotnet",
                Quote(
                    dll));
        }

        return null;
    }

    private static string Quote(
        string value) =>
        $"\"{value.Replace("\"", "\\\"")}\"";

    private static JsonSerializerOptions JsonOptions() =>
        new(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true
        };

    private sealed record WorkerLaunch(
        string Executable,
        string Arguments);
}
