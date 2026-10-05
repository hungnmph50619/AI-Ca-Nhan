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

public sealed class WindowsDesktopOcrSensor(
    IFlaUiAutomationClient sidecar)
    : IDesktopOcrSensor
{
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
