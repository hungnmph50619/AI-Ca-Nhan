namespace PersonalAI.Web.Services;

public sealed class PaddleOnnxDesktopOcrProvider(
    IFlaUiAutomationClient sidecar,
    ILocalVisionWorkerClient worker)
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

        var invocation =
            worker.Invoke(
                new LocalVisionWorkerRequest(
                    Operation: "paddle-ocr",
                    JpegBase64:
                        capture.JpegBase64),
                WorkerTimeoutMilliseconds);

        if (!invocation.Success ||
            invocation.Response is null)
        {
            return Unavailable(
                invocation.Detail);
        }

        var response =
            invocation.Response;

        var lines =
            (response.Lines ??
             Array.Empty<LocalVisionWorkerLine>())
                .Select(line =>
                    new DesktopOcrLine(
                        line.Text ??
                        string.Empty,
                        (line.Words ??
                         Array.Empty<LocalVisionWorkerWord>())
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
}
