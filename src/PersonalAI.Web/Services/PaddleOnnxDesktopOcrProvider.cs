using System.Diagnostics;
using System.Text.Json;

namespace PersonalAI.Web.Services;

public sealed class PaddleOnnxDesktopOcrProvider(
    IFlaUiAutomationClient sidecar)
    : IDesktopOcrProvider
{
    private const int WorkerTimeoutMilliseconds = 4500;

    public string Name =>
        "paddleocr-onnx";

    public DesktopOcrObservation ReadWindow(
        string windowId)
    {
        if (!OperatingSystem.IsWindows())
            return Unavailable(
                "PaddleOCR/ONNX worker hiện chỉ bật trên Windows.");

        if (!Environment.Is64BitProcess)
            return Unavailable(
                "PaddleOCR/ONNX worker yêu cầu tiến trình x64.");

        if (string.IsNullOrWhiteSpace(
                windowId))
        {
            return Unavailable(
                "Thiếu WindowId cho PaddleOCR/ONNX.");
        }

        if (!sidecar.Available)
        {
            return Unavailable(
                "Windows Automation sidecar không khả dụng để capture cửa sổ.");
        }

        FlaUiAutomationResponse capture;
        try
        {
            capture =
                sidecar.Invoke(
                    new FlaUiAutomationRequest(
                        Operation: "capture-window",
                        WindowId: windowId));
        }
        catch (Exception exception) when (
            exception is
                InvalidOperationException or
                IOException or
                TimeoutException)
        {
            return Unavailable(
                $"Không capture được cửa sổ cho PaddleOCR/ONNX: {exception.Message}");
        }

        if (!capture.Success ||
            string.IsNullOrWhiteSpace(
                capture.JpegBase64))
        {
            return Unavailable(
                $"Capture cho PaddleOCR/ONNX thất bại: {capture.Detail}");
        }

        var worker =
            ResolveWorker();

        if (worker is null)
        {
            return Unavailable(
                "Không tìm thấy PersonalAI.LocalVisionWorker trong output directory.");
        }

        var request =
            JsonSerializer.Serialize(
                new WorkerRequest(
                    "paddle-ocr",
                    capture.JpegBase64),
                JsonOptions());

        var result =
            InvokeWorker(
                worker,
                request);

        if (!result.Success ||
            string.IsNullOrWhiteSpace(
                result.Output))
        {
            return Unavailable(
                result.Detail);
        }

        WorkerResponse? response;
        try
        {
            response =
                JsonSerializer.Deserialize<WorkerResponse>(
                    result.Output,
                    JsonOptions());
        }
        catch (JsonException exception)
        {
            return Unavailable(
                $"Local vision worker trả JSON không hợp lệ: {exception.Message}");
        }

        if (response is null ||
            !response.Success)
        {
            return Unavailable(
                response?.Detail ??
                "Local vision worker không trả kết quả.");
        }

        var lines =
            (response.Lines ??
             Array.Empty<WorkerLine>())
                .Select(line =>
                    new DesktopOcrLine(
                        line.Text ??
                        string.Empty,
                        (line.Words ??
                         Array.Empty<WorkerWord>())
                            .Select(word =>
                                new DesktopOcrWord(
                                    word.Text ??
                                    string.Empty,
                                    word.Left,
                                    word.Top,
                                    word.Width,
                                    word.Height))
                            .ToArray()))
                .ToArray();

        return new(
            Available: true,
            Text:
                response.Text ??
                string.Empty,
            Language:
                "multilingual",
            Lines:
                lines,
            CaptureWidth:
                capture.CaptureWidth,
            CaptureHeight:
                capture.CaptureHeight,
            Provider:
                Name,
            Reason:
                $"Isolated worker: {response.Detail}");
    }

    internal static WorkerInvocationResult InvokeWorkerForAcceptance(
        string executable,
        string arguments,
        string request,
        int timeoutMilliseconds) =>
        InvokeWorker(
            new WorkerLaunch(
                executable,
                arguments),
            request,
            timeoutMilliseconds);

    private static WorkerInvocationResult InvokeWorker(
        WorkerLaunch worker,
        string request,
        int timeoutMilliseconds =
            WorkerTimeoutMilliseconds)
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
                    string.Empty,
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

            if (!process.WaitForExit(
                    Math.Max(
                        250,
                        timeoutMilliseconds)))
            {
                try
                {
                    process.Kill(
                        entireProcessTree: true);
                }
                catch
                {
                    // Worker đã chết hoặc OS không cho kill; caller vẫn xem là timeout.
                }

                return new(
                    false,
                    true,
                    string.Empty,
                    $"Local vision worker vượt timeout {timeoutMilliseconds}ms và đã bị terminate.");
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
                    output,
                    $"Local vision worker exit code={process.ExitCode}. {error}".Trim());
            }

            return new(
                true,
                false,
                output,
                string.IsNullOrWhiteSpace(error)
                    ? "Worker hoàn tất."
                    : error.Trim());
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
                string.Empty,
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
        var baseDirectory =
            AppContext.BaseDirectory;

        var workerDirectory =
            Path.Combine(
                baseDirectory,
                "local-vision-worker");

        var exe =
            Path.Combine(
                workerDirectory,
                "PersonalAI.LocalVisionWorker.exe");

        if (File.Exists(exe))
        {
            return new(
                exe,
                string.Empty);
        }

        var dll =
            Path.Combine(
                workerDirectory,
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

    private DesktopOcrObservation Unavailable(
        string reason) =>
        new(
            Available: false,
            Text: string.Empty,
            Language: string.Empty,
            Lines:
                Array.Empty<DesktopOcrLine>(),
            CaptureWidth: 0,
            CaptureHeight: 0,
            Provider:
                Name,
            Reason:
                reason);

    private static JsonSerializerOptions JsonOptions() =>
        new(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true
        };

    internal sealed record WorkerInvocationResult(
        bool Success,
        bool TimedOut,
        string Output,
        string Detail);

    private sealed record WorkerLaunch(
        string Executable,
        string Arguments);

    private sealed record WorkerRequest(
        string Operation,
        string JpegBase64);

    private sealed record WorkerResponse(
        bool Success,
        string? Detail,
        string? Text,
        WorkerLine[]? Lines);

    private sealed record WorkerLine(
        string? Text,
        WorkerWord[]? Words);

    private sealed record WorkerWord(
        string? Text,
        double Left,
        double Top,
        double Width,
        double Height);
}
