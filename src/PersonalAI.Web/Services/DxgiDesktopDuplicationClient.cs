using System.Diagnostics;
using System.Text.Json;

namespace PersonalAI.Web.Services;

public sealed record DxgiDesktopDuplicationResponse(
    bool Success,
    string Detail,
    byte[] Jpeg,
    int Width,
    int Height,
    string Backend);

public interface IDxgiDesktopDuplicationClient
{
    bool Available { get; }

    DxgiDesktopDuplicationResponse CaptureMonitor(
        string monitorDevice);
}

public sealed class DxgiDesktopDuplicationClient(
    ILogger<DxgiDesktopDuplicationClient> logger)
    : IDxgiDesktopDuplicationClient
{
    private const int MaximumWaitMilliseconds = 5000;

    public bool Available =>
        OperatingSystem.IsWindowsVersionAtLeast(
            6,
            2) &&
        Environment.UserInteractive &&
        File.Exists(
            ResolveSidecarPath());

    public DxgiDesktopDuplicationResponse CaptureMonitor(
        string monitorDevice)
    {
        if (!Available)
        {
            return Unavailable(
                "DXGI Desktop Duplication sidecar chưa khả dụng trên máy này.");
        }

        var request = new
        {
            operation = "capture-monitor-dxgi",
            windowId = "0x0",
            monitorDevice
        };

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };

        process.StartInfo.ArgumentList.Add(
            ResolveSidecarPath());

        try
        {
            if (!process.Start())
            {
                return Unavailable(
                    "Không khởi động được DXGI sidecar.");
            }

            var outputTask =
                process.StandardOutput.ReadToEndAsync();
            var errorTask =
                process.StandardError.ReadToEndAsync();

            process.StandardInput.Write(
                JsonSerializer.Serialize(
                    request,
                    JsonOptions()));

            process.StandardInput.Close();

            if (!process.WaitForExit(
                    MaximumWaitMilliseconds))
            {
                try
                {
                    process.Kill(
                        entireProcessTree: true);
                }
                catch
                {
                    // Best effort cleanup.
                }

                return Unavailable(
                    "DXGI Desktop Duplication quá thời gian phản hồi.");
            }

            var output =
                outputTask.GetAwaiter().GetResult();

            var error =
                errorTask.GetAwaiter().GetResult();

            if (string.IsNullOrWhiteSpace(output))
            {
                logger.LogDebug(
                    "DXGI sidecar không trả JSON. ExitCode={ExitCode}; stderr={Error}",
                    process.ExitCode,
                    Limit(error, 400));

                return Unavailable(
                    "DXGI sidecar không trả kết quả.");
            }

            var response =
                JsonSerializer.Deserialize<SidecarResponse>(
                    output,
                    JsonOptions());

            if (response is null ||
                !response.Success ||
                string.IsNullOrWhiteSpace(
                    response.JpegBase64))
            {
                return Unavailable(
                    response?.Detail ??
                    "Không đọc được phản hồi DXGI.");
            }

            byte[] jpeg;
            try
            {
                jpeg = Convert.FromBase64String(
                    response.JpegBase64);
            }
            catch (FormatException)
            {
                return Unavailable(
                    "DXGI trả dữ liệu ảnh không hợp lệ.");
            }

            if (jpeg.Length is < 24 or > 8 * 1024 * 1024 ||
                response.CaptureWidth <= 0 ||
                response.CaptureHeight <= 0)
            {
                Array.Clear(
                    jpeg,
                    0,
                    jpeg.Length);

                return Unavailable(
                    "DXGI trả frame có metadata không hợp lệ.");
            }

            return new(
                true,
                response.Detail ??
                "DXGI Desktop Duplication thành công.",
                jpeg,
                response.CaptureWidth,
                response.CaptureHeight,
                string.IsNullOrWhiteSpace(
                    response.CaptureBackend)
                    ? DesktopCaptureBackends.DxgiDesktopDuplication
                    : response.CaptureBackend);
        }
        catch (Exception exception) when (
            exception is
                InvalidOperationException or
                System.ComponentModel.Win32Exception or
                IOException or
                JsonException)
        {
            logger.LogDebug(
                exception,
                "DXGI Desktop Duplication sidecar không khả dụng.");

            return Unavailable(
                $"DXGI Desktop Duplication không khả dụng: {exception.Message}");
        }
    }

    private static string ResolveSidecarPath() =>
        Path.Combine(
            AppContext.BaseDirectory,
            "windows-automation",
            "PersonalAI.WindowsAutomation.dll");

    private static DxgiDesktopDuplicationResponse Unavailable(
        string detail) =>
        new(
            false,
            detail,
            Array.Empty<byte>(),
            0,
            0,
            DesktopCaptureBackends.DxgiDesktopDuplication);

    private static JsonSerializerOptions JsonOptions() =>
        new(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true
        };

    private static string Limit(
        string? value,
        int maximum)
    {
        var text = value ?? string.Empty;
        return text.Length <= maximum
            ? text
            : text[..maximum];
    }

    private sealed record SidecarResponse(
        bool Success,
        string? Detail,
        string? JpegBase64,
        int CaptureWidth,
        int CaptureHeight,
        string? CaptureBackend);
}
