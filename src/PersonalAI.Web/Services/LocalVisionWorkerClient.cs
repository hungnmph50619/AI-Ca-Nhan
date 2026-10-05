using System.Diagnostics;
using System.Text.Json;

namespace PersonalAI.Web.Services;

public sealed record LocalVisionWorkerRequest(
    string Operation,
    string? JpegBase64 = null,
    string? TemplateBase64 = null,
    double MinimumScore = 0);

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
    LocalVisionWorkerMatch? Match);

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
    : ILocalVisionWorkerClient
{
    public LocalVisionWorkerInvocation Invoke(
        LocalVisionWorkerRequest request,
        int timeoutMilliseconds)
    {
        ArgumentNullException.ThrowIfNull(request);

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

        var payload =
            JsonSerializer.Serialize(
                request,
                JsonOptions());

        return InvokeProcess(
            worker,
            payload,
            timeoutMilliseconds);
    }

    internal static LocalVisionWorkerInvocation InvokeProcessForAcceptance(
        string executable,
        string arguments,
        string request,
        int timeoutMilliseconds) =>
        InvokeProcess(
            new WorkerLaunch(
                executable,
                arguments),
            request,
            timeoutMilliseconds);

    private static LocalVisionWorkerInvocation InvokeProcess(
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
                Math.Clamp(
                    timeoutMilliseconds,
                    250,
                    15000);

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
