using System.Text.Json;

namespace PersonalAI.Web.Services;

public sealed record DesktopOcrWord(
    string Text,
    double Left,
    double Top,
    double Width,
    double Height);

public sealed record DesktopOcrLine(
    string Text,
    IReadOnlyList<DesktopOcrWord> Words);

public sealed record DesktopOcrObservation(
    bool Available,
    string Text,
    string Language,
    IReadOnlyList<DesktopOcrLine> Lines,
    int CaptureWidth,
    int CaptureHeight,
    string Provider,
    string Reason)
{
    public int WordCount =>
        Lines.Sum(line =>
            line.Words.Count);
}

public interface IDesktopOcrSensor
{
    DesktopOcrObservation ReadWindow(
        string windowId);
}

public interface IDesktopOcrProvider
{
    string Name { get; }

    DesktopOcrObservation ReadWindow(
        string windowId);
}

public sealed class WindowsDesktopOcrSensor(
    IFlaUiAutomationClient sidecar)
    : IDesktopOcrProvider
{
    public string Name => "windows-ocr";

    public DesktopOcrObservation ReadWindow(
        string windowId)
    {
        if (string.IsNullOrWhiteSpace(
                windowId))
        {
            return Unavailable(
                "Thiếu WindowId cho OCR.");
        }

        if (!sidecar.Available)
        {
            return Unavailable(
                "Windows Automation sidecar không khả dụng.");
        }

        var response =
            sidecar.Invoke(
                new FlaUiAutomationRequest(
                    Operation: "ocr-window",
                    WindowId: windowId));

        if (!response.Success ||
            string.IsNullOrWhiteSpace(
                response.OcrJson))
        {
            return Unavailable(
                response.Detail);
        }

        try
        {
            var payload =
                JsonSerializer.Deserialize<SidecarOcrPayload>(
                    response.OcrJson,
                    JsonOptions());

            if (payload is null)
            {
                return Unavailable(
                    "Không đọc được payload Windows OCR.");
            }

            var lines =
                (payload.Lines ??
                 Array.Empty<SidecarOcrLine>())
                    .Select(line =>
                        new DesktopOcrLine(
                            line.Text ??
                            string.Empty,
                            (line.Words ??
                             Array.Empty<SidecarOcrWord>())
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
                    payload.Text ??
                    string.Empty,
                Language:
                    payload.Language ??
                    string.Empty,
                Lines:
                    lines,
                CaptureWidth:
                    response.CaptureWidth,
                CaptureHeight:
                    response.CaptureHeight,
                Provider:
                    "windows-ocr",
                Reason:
                    response.Detail);
        }
        catch (JsonException exception)
        {
            return Unavailable(
                $"Payload Windows OCR không hợp lệ: {exception.Message}");
        }
    }

    internal static DesktopOcrObservation ParseForAcceptance(
        string json)
    {
        try
        {
            var payload =
                JsonSerializer.Deserialize<SidecarOcrPayload>(
                    json,
                    JsonOptions());

            if (payload is null)
                return Unavailable(
                    "Payload OCR rỗng.");

            var lines =
                (payload.Lines ??
                 Array.Empty<SidecarOcrLine>())
                    .Select(line =>
                        new DesktopOcrLine(
                            line.Text ??
                            string.Empty,
                            (line.Words ??
                             Array.Empty<SidecarOcrWord>())
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
                true,
                payload.Text ??
                    string.Empty,
                payload.Language ??
                    string.Empty,
                lines,
                0,
                0,
                "windows-ocr",
                "acceptance");
        }
        catch (JsonException exception)
        {
            return Unavailable(
                exception.Message);
        }
    }

    private static DesktopOcrObservation Unavailable(
        string? reason) =>
        new(
            Available: false,
            Text: string.Empty,
            Language: string.Empty,
            Lines:
                Array.Empty<DesktopOcrLine>(),
            CaptureWidth: 0,
            CaptureHeight: 0,
            Provider:
                "windows-ocr",
            Reason:
                string.IsNullOrWhiteSpace(reason)
                    ? "Windows OCR không khả dụng."
                    : reason.Trim());

    private static JsonSerializerOptions JsonOptions() =>
        new(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true
        };

    private sealed record SidecarOcrPayload(
        string? Text,
        string? Language,
        SidecarOcrLine[]? Lines);

    private sealed record SidecarOcrLine(
        string? Text,
        SidecarOcrWord[]? Words);

    private sealed record SidecarOcrWord(
        string? Text,
        double Left,
        double Top,
        double Width,
        double Height);
}


public sealed class DesktopOcrSensorRouter(
    IEnumerable<IDesktopOcrProvider> providers,
    ILocalVisualProviderHealthRegistry health)
    : IDesktopOcrSensor
{
    private readonly IReadOnlyList<IDesktopOcrProvider> orderedProviders =
        providers
            .OrderBy(provider =>
                ProviderPriority(
                    provider.Name))
            .ThenBy(provider =>
                provider.Name,
                StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public DesktopOcrObservation ReadWindow(
        string windowId)
    {
        DesktopOcrObservation? last =
            null;

        foreach (var provider in
                 orderedProviders)
        {
            if (health.ShouldSkip(
                    provider.Name,
                    DateTimeOffset.UtcNow,
                    out var skipReason))
            {
                last =
                    new(
                        Available: false,
                        Text: string.Empty,
                        Language: string.Empty,
                        Lines:
                            Array.Empty<DesktopOcrLine>(),
                        CaptureWidth: 0,
                        CaptureHeight: 0,
                        Provider:
                            provider.Name,
                        Reason:
                            skipReason);
                continue;
            }

            var stopwatch =
                System.Diagnostics.Stopwatch.StartNew();

            DesktopOcrObservation result;
            try
            {
                result =
                    provider.ReadWindow(
                        windowId);
            }
            catch (Exception exception) when (
                exception is
                    InvalidOperationException or
                    IOException or
                    TimeoutException)
            {
                stopwatch.Stop();
                health.RecordFailure(
                    provider.Name,
                    stopwatch.ElapsedMilliseconds,
                    exception.Message);

                last =
                    new(
                        Available: false,
                        Text: string.Empty,
                        Language: string.Empty,
                        Lines:
                            Array.Empty<DesktopOcrLine>(),
                        CaptureWidth: 0,
                        CaptureHeight: 0,
                        Provider:
                            provider.Name,
                        Reason:
                            $"{provider.Name} lỗi kỹ thuật: {exception.Message}");
                continue;
            }

            stopwatch.Stop();
            last =
                result;

            if (result.Available)
            {
                health.RecordSuccess(
                    provider.Name,
                    stopwatch.ElapsedMilliseconds,
                    result.Reason);

                if (result.WordCount > 0 ||
                    !string.IsNullOrWhiteSpace(
                        result.Text))
                {
                    return result;
                }

                // Provider chạy bình thường nhưng ảnh không có text:
                // đây không phải health failure; thử provider kế tiếp nếu có.
                continue;
            }

            health.RecordFailure(
                provider.Name,
                stopwatch.ElapsedMilliseconds,
                result.Reason);
        }

        return last ??
            new(
                Available: false,
                Text: string.Empty,
                Language: string.Empty,
                Lines:
                    Array.Empty<DesktopOcrLine>(),
                CaptureWidth: 0,
                CaptureHeight: 0,
                Provider:
                    "none",
                Reason:
                    "Không có OCR provider khả dụng.");
    }

    private static int ProviderPriority(
        string? name) =>
        (name ?? string.Empty)
            .Trim()
            .ToLowerInvariant() switch
        {
            "windows-ocr" => 10,
            "paddleocr-onnx" => 20,
            _ => 100
        };
}
