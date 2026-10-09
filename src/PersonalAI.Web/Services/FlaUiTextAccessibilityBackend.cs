using System.Diagnostics;
using System.Text.Json;

namespace PersonalAI.Web.Services;

public sealed record FlaUiAutomationRequest(
    string Operation,
    string WindowId,
    string? TargetToken = null,
    string? Text = null,
    string? WriteMode = null,
    int WaitMilliseconds = 0,
    int MaxNodes = 200,
    int MaxDepth = 6);

public sealed record FlaUiAutomationResponse(
    bool Success,
    bool IsFocused,
    bool CanRead,
    bool CanDirectSet,
    bool SupportsSelection,
    bool IsReadOnly,
    bool IsSensitive,
    string ControlClass,
    long NativeWindowHandle,
    string TargetToken,
    string? Value,
    string Detail,
    int CaptureWidth = 0,
    int CaptureHeight = 0,
    string EventKind = "",
    string EventWindowId = "",
    string StructuredJson = "",
    string OcrJson = "",
    string? JpegBase64 = null,
    string CaptureBackend = "");

public interface IFlaUiAutomationClient
{
    bool Available { get; }

    FlaUiAutomationResponse Invoke(
        FlaUiAutomationRequest request);
}

public sealed class FlaUiAutomationClient(
    ILogger<FlaUiAutomationClient> logger,
    IUniversalCapabilityCache? capabilityCache = null)
    : IFlaUiAutomationClient
{
    private const int MaximumWaitMilliseconds = 4000;
    private const int OcrWaitMilliseconds = 12000;

    public bool Available =>
        OperatingSystem.IsWindows() &&
        File.Exists(ResolveSidecarPath());

    public FlaUiAutomationResponse Invoke(
        FlaUiAutomationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!OperatingSystem.IsWindows())
            return Unavailable(
                "FlaUI UIA3 chỉ khả dụng trên Windows.");

        var sidecarPath = ResolveSidecarPath();
        if (!File.Exists(sidecarPath))
            return Unavailable(
                "Không tìm thấy sidecar FlaUI UIA3 trong thư mục windows-automation.");

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
            sidecarPath);

        try
        {
            if (!process.Start())
                return Unavailable(
                    "Không khởi động được sidecar FlaUI UIA3.");

            var outputTask =
                process.StandardOutput.ReadToEndAsync();
            var errorTask =
                process.StandardError.ReadToEndAsync();

            var payload = JsonSerializer.Serialize(
                request,
                JsonOptions());

            process.StandardInput.Write(payload);
            process.StandardInput.Close();

            var waitMilliseconds =
                request.Operation.Equals(
                    "ocr-window",
                    StringComparison.OrdinalIgnoreCase)
                    ? OcrWaitMilliseconds
                    : MaximumWaitMilliseconds;

            if (!process.WaitForExit(
                    waitMilliseconds))
            {
                try
                {
                    process.Kill(
                        entireProcessTree: true);
                }
                catch
                {
                    // Best effort. Timeout là ranh giới an toàn chính.
                }

                capabilityCache?.MarkTemporarilyUnavailable(
                    "desktop.flaui-uia3",
                    TimeSpan.FromSeconds(15),
                    "Sidecar FlaUI UIA3 timeout.");

                return Unavailable(
                    "FlaUI UIA3 quá thời gian phản hồi; chuyển sang adapter dự phòng.");
            }

            var output =
                outputTask.GetAwaiter().GetResult();

            var error =
                errorTask.GetAwaiter().GetResult();

            if (string.IsNullOrWhiteSpace(output))
            {
                logger.LogDebug(
                    "FlaUI UIA3 không trả JSON. ExitCode={ExitCode}; stderr={Error}",
                    process.ExitCode,
                    Limit(error, 400));

                return Unavailable(
                    "FlaUI UIA3 không trả kết quả; chuyển sang adapter dự phòng.");
            }

            var response =
                JsonSerializer.Deserialize<FlaUiAutomationResponse>(
                    output,
                    JsonOptions());

            return response
                ?? Unavailable(
                    "Không đọc được phản hồi FlaUI UIA3.");
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
                "FlaUI UIA3 sidecar không khả dụng.");

            capabilityCache?.MarkTemporarilyUnavailable(
                "desktop.flaui-uia3",
                TimeSpan.FromSeconds(15),
                exception.Message);

            return Unavailable(
                $"FlaUI UIA3 không khả dụng: {exception.Message}");
        }
    }

    private static string ResolveSidecarPath() =>
        Path.Combine(
            AppContext.BaseDirectory,
            "windows-automation",
            "PersonalAI.WindowsAutomation.dll");

    private static FlaUiAutomationResponse Unavailable(
        string detail) =>
        new(
            false,
            false,
            false,
            false,
            SupportsSelection: false,
            IsReadOnly: false,
            IsSensitive: false,
            ControlClass: string.Empty,
            NativeWindowHandle: 0,
            TargetToken: string.Empty,
            Value: null,
            detail);

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
}

public sealed class FlaUiTextAccessibilityBackend(
    IFlaUiAutomationClient client,
    IUniversalResilienceExecutor resilience)
    : IFlaUiTextAccessibilityBackend
{
    private const string BackendName = "flaui-uia3";

    public TextTargetCapabilities Probe(
        string windowId)
    {
        var response = InvokeReadOnlySafely(
            new FlaUiAutomationRequest(
                "probe",
                windowId));

        return ToCapabilities(
            windowId,
            response);
    }

    public string? TryRead(
        TextTargetCapabilities target)
    {
        if (!IsMine(target))
            return null;

        var response = InvokeReadOnlySafely(
            new FlaUiAutomationRequest(
                "read",
                target.WindowId,
                target.TargetToken));

        return response.Success
            ? response.Value
            : null;
    }

    public bool TryDirectSet(
        TextTargetCapabilities target,
        string text,
        string writeMode)
    {
        if (!IsMine(target))
            return false;

        var response = client.Invoke(
            new FlaUiAutomationRequest(
                "set",
                target.WindowId,
                target.TargetToken,
                text,
                writeMode));

        return response.Success;
    }

    public bool IsSameFocusedTarget(
        TextTargetCapabilities target)
    {
        if (!IsMine(target))
            return false;

        var response = InvokeReadOnlySafely(
            new FlaUiAutomationRequest(
                "same-focus",
                target.WindowId,
                target.TargetToken));

        return response.Success &&
               response.IsFocused;
    }

    private FlaUiAutomationResponse InvokeReadOnlySafely(
        FlaUiAutomationRequest request)
    {
        try
        {
            return resilience.ExecuteReadOnly(
                "flaui-uia3-readonly",
                () =>
                {
                    var response = client.Invoke(request);

                    if (!response.Success &&
                        IsTransientTechnicalFailure(
                            response.Detail))
                    {
                        throw new IOException(
                            response.Detail);
                    }

                    return response;
                });
        }
        catch (Exception exception) when (
            exception is IOException or
            TimeoutException or
            Polly.CircuitBreaker.BrokenCircuitException)
        {
            return new(
                Success: false,
                IsFocused: false,
                CanRead: false,
                CanDirectSet: false,
                SupportsSelection: false,
                IsReadOnly: false,
                IsSensitive: false,
                ControlClass: string.Empty,
                NativeWindowHandle: 0,
                TargetToken: string.Empty,
                Value: null,
                Detail:
                    $"FlaUI read-only path tạm ngừng bởi resilience policy: {exception.Message}");
        }
    }

    private static bool IsTransientTechnicalFailure(
        string? detail)
    {
        var value =
            (detail ?? string.Empty)
                .Trim()
                .ToLowerInvariant();

        return value.Contains("timeout") ||
               value.Contains("quá thời gian") ||
               value.Contains("không trả kết quả") ||
               value.Contains("không khả dụng") ||
               value.Contains("không khởi động được");
    }

    private static TextTargetCapabilities ToCapabilities(
        string windowId,
        FlaUiAutomationResponse response) =>
        new(
            windowId,
            response.NativeWindowHandle > 0
                ? new nint(response.NativeWindowHandle)
                : nint.Zero,
            response.ControlClass ?? string.Empty,
            response.IsFocused,
            response.CanRead,
            response.CanDirectSet,
            response.SupportsSelection,
            response.IsReadOnly,
            response.IsSensitive,
            BackendName,
            response.TargetToken ?? string.Empty);

    private static bool IsMine(
        TextTargetCapabilities target) =>
        target.Backend.Equals(
            BackendName,
            StringComparison.OrdinalIgnoreCase);
}

public sealed class CompositeTextAccessibilityBackend(
    IFlaUiTextAccessibilityBackend flaUi,
    IWin32TextAccessibilityBackend win32)
    : ITextAccessibilityBackend
{
    public TextTargetCapabilities Probe(
        string windowId)
    {
        var structured = flaUi.Probe(
            windowId);

        if (structured.IsFocused &&
            (structured.CanRead ||
             structured.CanDirectSet ||
             structured.IsReadOnly ||
             structured.IsSensitive))
        {
            return structured;
        }

        return win32.Probe(
            windowId);
    }

    public string? TryRead(
        TextTargetCapabilities target) =>
        IsFlaUi(target)
            ? flaUi.TryRead(target)
            : win32.TryRead(target);

    public bool TryDirectSet(
        TextTargetCapabilities target,
        string text,
        string writeMode) =>
        IsFlaUi(target)
            ? flaUi.TryDirectSet(
                target,
                text,
                writeMode)
            : win32.TryDirectSet(
                target,
                text,
                writeMode);

    public bool IsSameFocusedTarget(
        TextTargetCapabilities target) =>
        IsFlaUi(target)
            ? flaUi.IsSameFocusedTarget(target)
            : win32.IsSameFocusedTarget(target);

    private static bool IsFlaUi(
        TextTargetCapabilities target) =>
        target.Backend.Equals(
            "flaui-uia3",
            StringComparison.OrdinalIgnoreCase);
}
