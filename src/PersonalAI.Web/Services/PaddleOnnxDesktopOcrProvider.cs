using CoreOCROnnx.SDK;

namespace PersonalAI.Web.Services;

public sealed class PaddleOnnxDesktopOcrProvider(
    IFlaUiAutomationClient sidecar)
    : IDesktopOcrProvider,
      IDisposable
{
    private static readonly string[] RequiredModelFiles =
    [
        "PP-OCRv6_tiny_det.onnx",
        "PP-OCRv6_tiny_rec.onnx",
        "ch_PP-LCNet_x0_25_textline_ori_cls_mobile.onnx",
        "ppocrv6tiny_dict.txt"
    ];

    private readonly object gate =
        new();

    private OCRService? engine;
    private string? modelDirectory;
    private string? initializationFailure;
    private bool initialized;
    private bool disposed;

    public string Name =>
        "paddleocr-onnx";

    public DesktopOcrObservation ReadWindow(
        string windowId)
    {
        if (disposed)
            return Unavailable(
                "PaddleOCR/ONNX provider đã được dispose.");

        if (!OperatingSystem.IsWindows())
            return Unavailable(
                "PaddleOCR/ONNX CPU provider hiện chỉ bật trên Windows.");

        if (!Environment.Is64BitProcess)
            return Unavailable(
                "PaddleOCR/ONNX runtime yêu cầu tiến trình x64.");

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

        var init =
            EnsureInitialized();

        if (!init.Success)
            return Unavailable(
                init.Reason);

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

        try
        {
            var image =
                Convert.FromBase64String(
                    capture.JpegBase64);

            OCRResult result;
            lock (gate)
            {
                if (engine is null)
                {
                    return Unavailable(
                        "PaddleOCR/ONNX engine chưa được khởi tạo.");
                }

                result =
                    engine.Detect(
                        image);
            }

            var lines =
                (result.TextBlocks ??
                 new List<JsonResult>())
                    .Where(block =>
                        !string.IsNullOrWhiteSpace(
                            block.Text))
                    .Select(ToLine)
                    .Where(line =>
                        line is not null)
                    .Cast<DesktopOcrLine>()
                    .ToArray();

            var text =
                string.IsNullOrWhiteSpace(
                    result.StrRes)
                    ? string.Join(
                        Environment.NewLine,
                        lines.Select(line =>
                            line.Text))
                    : result.StrRes.Trim();

            return new(
                Available: true,
                Text:
                    text,
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
                    $"PaddleOCR/ONNX đọc được {lines.Length} text block; detect={result.DbNetTime:0.0}ms; recognize={result.DetectTime:0.0}ms.");
        }
        catch (Exception exception) when (
            exception is
                FormatException or
                OCRException or
                DllNotFoundException or
                EntryPointNotFoundException or
                BadImageFormatException or
                TypeInitializationException or
                InvalidOperationException)
        {
            return Unavailable(
                $"PaddleOCR/ONNX inference thất bại: {exception.GetType().Name}: {exception.Message}");
        }
    }

    private (bool Success, string Reason) EnsureInitialized()
    {
        lock (gate)
        {
            if (initialized &&
                engine is not null)
            {
                return (
                    true,
                    $"PaddleOCR/ONNX đã sẵn sàng từ {modelDirectory}.");
            }

            if (!string.IsNullOrWhiteSpace(
                    initializationFailure))
            {
                return (
                    false,
                    initializationFailure);
            }

            var directory =
                FindModelDirectory(
                    AppContext.BaseDirectory);

            if (directory is null)
            {
                initializationFailure =
                    "Không tìm thấy bộ model PP-OCRv6 đi kèm CoreOCR runtime trong output directory.";
                return (
                    false,
                    initializationFailure);
            }

            try
            {
                var candidate =
                    new OCRService();

                var message =
                    candidate.InitDefaultOCREngine(
                        directory);

                if (!message.Contains(
                        "成功",
                        StringComparison.OrdinalIgnoreCase) &&
                    !message.Contains(
                        "success",
                        StringComparison.OrdinalIgnoreCase))
                {
                    candidate.FreeEngine();
                    initializationFailure =
                        $"PaddleOCR/ONNX init không thành công: {message}";
                    return (
                        false,
                        initializationFailure);
                }

                engine =
                    candidate;
                modelDirectory =
                    directory;
                initialized =
                    true;

                return (
                    true,
                    $"PaddleOCR/ONNX khởi tạo thành công từ {directory}.");
            }
            catch (Exception exception) when (
                exception is
                    OCRException or
                    DllNotFoundException or
                    EntryPointNotFoundException or
                    BadImageFormatException or
                    TypeInitializationException or
                    InvalidOperationException)
            {
                initializationFailure =
                    $"PaddleOCR/ONNX init thất bại: {exception.GetType().Name}: {exception.Message}";
                return (
                    false,
                    initializationFailure);
            }
        }
    }

    internal static string? FindModelDirectory(
        string baseDirectory)
    {
        if (string.IsNullOrWhiteSpace(
                baseDirectory) ||
            !Directory.Exists(
                baseDirectory))
        {
            return null;
        }

        var candidates =
            new[]
            {
                baseDirectory,
                Path.Combine(
                    baseDirectory,
                    "PaddleOCR"),
                Path.Combine(
                    baseDirectory,
                    "models"),
                Path.Combine(
                    baseDirectory,
                    "OCRModels"),
                Path.Combine(
                    baseDirectory,
                    "CoreOCR"),
                Path.Combine(
                    baseDirectory,
                    "runtimes",
                    "win-x64",
                    "native")
            }
            .Distinct(
                StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in
                 candidates)
        {
            if (HasAllModels(
                    candidate))
            {
                return candidate;
            }
        }

        try
        {
            var detection =
                Directory
                    .EnumerateFiles(
                        baseDirectory,
                        RequiredModelFiles[0],
                        SearchOption.AllDirectories)
                    .Take(8)
                    .ToArray();

            foreach (var file in
                     detection)
            {
                var directory =
                    Path.GetDirectoryName(
                        file);

                if (!string.IsNullOrWhiteSpace(
                        directory) &&
                    HasAllModels(
                        directory))
                {
                    return directory;
                }
            }
        }
        catch (Exception exception) when (
            exception is
                IOException or
                UnauthorizedAccessException)
        {
            return null;
        }

        return null;
    }

    private static bool HasAllModels(
        string? directory) =>
        !string.IsNullOrWhiteSpace(
            directory) &&
        Directory.Exists(
            directory) &&
        RequiredModelFiles.All(file =>
            File.Exists(
                Path.Combine(
                    directory,
                    file)));

    internal static DesktopOcrLine? ToLine(
        JsonResult block)
    {
        var text =
            (block.Text ??
             string.Empty)
                .Trim();

        if (text.Length == 0)
            return null;

        var boxes =
            block.Boxes ??
            new List<OCRLocation>();

        if (boxes.Count == 0)
        {
            return new(
                text,
                Array.Empty<DesktopOcrWord>());
        }

        var left =
            boxes.Min(point =>
                point.x);
        var top =
            boxes.Min(point =>
                point.y);
        var right =
            boxes.Max(point =>
                point.x);
        var bottom =
            boxes.Max(point =>
                point.y);

        var width =
            Math.Max(
                2,
                right - left);
        var height =
            Math.Max(
                2,
                bottom - top);

        return new(
            text,
            [
                new DesktopOcrWord(
                    text,
                    left,
                    top,
                    width,
                    height)
            ]);
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

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed)
                return;

            disposed = true;

            try
            {
                engine?.FreeEngine();
            }
            catch
            {
                // Dispose không được làm crash host.
            }

            engine =
                null;
        }
    }
}
