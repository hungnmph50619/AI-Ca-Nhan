using System.Text.Json;
using CoreOCROnnx.SDK;
using OpenCvSharp;

var jsonOptions =
    new JsonSerializerOptions(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

try
{
    var input =
        await Console.In.ReadToEndAsync();

    var request =
        JsonSerializer.Deserialize<WorkerRequest>(
            input,
            jsonOptions);

    if (request is null)
    {
        Write(
            Fail(
                "Request worker không hợp lệ."));
        return;
    }

    if (request.Operation.Equals(
            "paddle-ocr",
            StringComparison.OrdinalIgnoreCase))
    {
        Write(
            RunPaddleOcr(
                request));
        return;
    }

    if (request.Operation.Equals(
            "opencv-template",
            StringComparison.OrdinalIgnoreCase))
    {
        Write(
            RunOpenCvTemplate(
                request));
        return;
    }

    Write(
        Fail(
            $"Worker operation không hỗ trợ: {request.Operation}."));
}
catch (Exception exception)
{
    Write(
        Fail(
            $"{exception.GetType().Name}: {exception.Message}"));
}

void Write(
    WorkerResponse response)
{
    Console.Out.Write(
        JsonSerializer.Serialize(
            response,
            jsonOptions));
}

WorkerResponse RunPaddleOcr(
    WorkerRequest request)
{
    if (string.IsNullOrWhiteSpace(
            request.JpegBase64))
    {
        return Fail(
            "PaddleOCR request thiếu JPEG.");
    }

    var modelDirectory =
        FindModelDirectory(
            AppContext.BaseDirectory);

    if (modelDirectory is null)
    {
        return Fail(
            "Không tìm thấy bộ model PP-OCRv6 trong local vision worker output.");
    }

    using var engine =
        new EngineLease(
            new OCRService());

    var init =
        engine.Service.InitDefaultOCREngine(
            modelDirectory);

    if (!init.Contains(
            "成功",
            StringComparison.OrdinalIgnoreCase) &&
        !init.Contains(
            "success",
            StringComparison.OrdinalIgnoreCase))
    {
        return Fail(
            $"Khởi tạo CoreOCROnnx thất bại: {init}");
    }

    var image =
        Convert.FromBase64String(
            request.JpegBase64);

    var result =
        engine.Service.Detect(
            image);

    var lines =
        (result.TextBlocks ??
         new List<JsonResult>())
            .Where(block =>
                !string.IsNullOrWhiteSpace(
                    block.Text))
            .Select(ToLine)
            .Where(line =>
                line is not null)
            .Cast<WorkerLine>()
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
        true,
        $"CoreOCROnnx đọc được {lines.Length} block; detect={result.DbNetTime:0.0}ms; recognize={result.DetectTime:0.0}ms.",
        text,
        lines,
        null);
}

WorkerResponse RunOpenCvTemplate(
    WorkerRequest request)
{
    if (string.IsNullOrWhiteSpace(
            request.JpegBase64) ||
        string.IsNullOrWhiteSpace(
            request.TemplateBase64))
    {
        return Fail(
            "OpenCV request thiếu frame/template.");
    }

    var frame =
        Convert.FromBase64String(
            request.JpegBase64);
    var templateBytes =
        Convert.FromBase64String(
            request.TemplateBase64);

    using var source =
        Cv2.ImDecode(
            frame,
            ImreadModes.Grayscale);
    using var template =
        Cv2.ImDecode(
            templateBytes,
            ImreadModes.Grayscale);

    if (source.Empty() ||
        template.Empty())
    {
        return Fail(
            "OpenCV không decode được frame/template.");
    }

    var minimumScore =
        Math.Clamp(
            request.MinimumScore <= 0
                ? 0.88
                : request.MinimumScore,
            0.50,
            0.999);

    var scaleCandidates =
        new[]
        {
            1.00,
            0.95,
            1.05,
            0.90,
            1.10,
            0.85,
            1.15
        };

    var candidates =
        new List<MatchCandidate>();

    foreach (var scale in
             scaleCandidates)
    {
        var width =
            Math.Max(
                2,
                (int)Math.Round(
                    template.Width *
                    scale));
        var height =
            Math.Max(
                2,
                (int)Math.Round(
                    template.Height *
                    scale));

        if (width >
                source.Width ||
            height >
                source.Height)
        {
            continue;
        }

        using var scaled =
            new Mat();

        Cv2.Resize(
            template,
            scaled,
            new Size(
                width,
                height),
            interpolation:
                scale < 1.0
                    ? InterpolationFlags.Area
                    : InterpolationFlags.Linear);

        using var result =
            new Mat();

        Cv2.MatchTemplate(
            source,
            scaled,
            result,
            TemplateMatchModes.CCoeffNormed);

        Cv2.MinMaxLoc(
            result,
            out _,
            out var maxValue,
            out _,
            out var maxLocation);

        var best =
            new MatchCandidate(
                maxValue,
                maxLocation.X,
                maxLocation.Y,
                width,
                height,
                scale);

        candidates.Add(
            best);

        using var suppressed =
            result.Clone();

        var suppressLeft =
            Math.Max(
                0,
                maxLocation.X -
                width / 2);
        var suppressTop =
            Math.Max(
                0,
                maxLocation.Y -
                height / 2);
        var suppressWidth =
            Math.Min(
                suppressed.Width -
                suppressLeft,
                Math.Max(
                    1,
                    width * 2));
        var suppressHeight =
            Math.Min(
                suppressed.Height -
                suppressTop,
                Math.Max(
                    1,
                    height * 2));

        Cv2.Rectangle(
            suppressed,
            new Rect(
                suppressLeft,
                suppressTop,
                suppressWidth,
                suppressHeight),
            Scalar.All(-1),
            thickness: -1);

        Cv2.MinMaxLoc(
            suppressed,
            out _,
            out var secondValue,
            out _,
            out var secondLocation);

        candidates.Add(
            new MatchCandidate(
                secondValue,
                secondLocation.X,
                secondLocation.Y,
                width,
                height,
                scale));
    }

    if (candidates.Count == 0)
    {
        return Fail(
            "Không có scale template hợp lệ trong frame.");
    }

    var ordered =
        candidates
            .OrderByDescending(candidate =>
                candidate.Score)
            .ToArray();

    var first =
        ordered[0];

    var second =
        ordered
            .Skip(1)
            .FirstOrDefault(candidate =>
                IsSpatiallyDistinct(
                    first,
                    candidate));

    var secondScore =
        second?.Score ??
        -1.0;

    const double minimumUniquenessMargin = 0.04;

    var ambiguous =
        first.Score >=
            minimumScore &&
        second is not null &&
        second.Score >=
            minimumScore &&
        first.Score -
            second.Score <
            minimumUniquenessMargin;

    var matched =
        first.Score >=
            minimumScore &&
        !ambiguous;

    var detail =
        ambiguous
            ? $"OpenCV có hai match gần ngang nhau: best={first.Score:0.000}; second={secondScore:0.000}; từ chối đoán."
            : matched
                ? $"OpenCV template match đạt {first.Score:0.000}, second={secondScore:0.000}, scale={first.Scale:0.00}."
                : $"OpenCV template match tốt nhất {first.Score:0.000} thấp hơn ngưỡng {minimumScore:0.000}.";

    return new(
        true,
        detail,
        string.Empty,
        Array.Empty<WorkerLine>(),
        new WorkerMatch(
            matched,
            ambiguous,
            Math.Clamp(
                first.Score,
                -1,
                1),
            Math.Clamp(
                secondScore,
                -1,
                1),
            first.Left,
            first.Top,
            first.Width,
            first.Height,
            first.Scale));
}

static bool IsSpatiallyDistinct(
    MatchCandidate left,
    MatchCandidate right)
{
    var leftCenterX =
        left.Left +
        left.Width / 2.0;
    var leftCenterY =
        left.Top +
        left.Height / 2.0;
    var rightCenterX =
        right.Left +
        right.Width / 2.0;
    var rightCenterY =
        right.Top +
        right.Height / 2.0;

    var threshold =
        Math.Max(
            8.0,
            Math.Min(
                left.Width,
                left.Height) *
            0.5);

    return Math.Abs(
               leftCenterX -
               rightCenterX) >
               threshold ||
           Math.Abs(
               leftCenterY -
               rightCenterY) >
               threshold;
}

static WorkerLine? ToLine(
    JsonResult block)
{
    var text =
        (block.Text ?? string.Empty)
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
            Array.Empty<WorkerWord>());
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

    return new(
        text,
        [
            new WorkerWord(
                text,
                left,
                top,
                Math.Max(
                    2,
                    right - left),
                Math.Max(
                    2,
                    bottom - top))
        ]);
}

static WorkerResponse Fail(
    string detail) =>
    new(
        false,
        detail,
        string.Empty,
        Array.Empty<WorkerLine>(),
        null);

static string? FindModelDirectory(
    string baseDirectory)
{
    var required =
        new[]
        {
            "PP-OCRv6_tiny_det.onnx",
            "PP-OCRv6_tiny_rec.onnx",
            "ch_PP-LCNet_x0_25_textline_ori_cls_mobile.onnx",
            "ppocrv6tiny_dict.txt"
        };

    bool HasAll(
        string? directory) =>
        !string.IsNullOrWhiteSpace(
            directory) &&
        Directory.Exists(
            directory) &&
        required.All(file =>
            File.Exists(
                Path.Combine(
                    directory,
                    file)));

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
        };

    foreach (var candidate in
             candidates)
    {
        if (HasAll(candidate))
            return candidate;
    }

    try
    {
        foreach (var detection in
                 Directory.EnumerateFiles(
                     baseDirectory,
                     required[0],
                     SearchOption.AllDirectories)
                     .Take(12))
        {
            var directory =
                Path.GetDirectoryName(
                    detection);

            if (HasAll(directory))
                return directory;
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

sealed record WorkerRequest(
    string Operation,
    string? JpegBase64,
    string? TemplateBase64,
    double MinimumScore);

sealed record WorkerResponse(
    bool Success,
    string Detail,
    string Text,
    WorkerLine[] Lines,
    WorkerMatch? Match);

sealed record WorkerMatch(
    bool Matched,
    bool Ambiguous,
    double Score,
    double SecondBestScore,
    int Left,
    int Top,
    int Width,
    int Height,
    double Scale);

sealed record WorkerLine(
    string Text,
    WorkerWord[] Words);

sealed record WorkerWord(
    string Text,
    double Left,
    double Top,
    double Width,
    double Height);

sealed record MatchCandidate(
    double Score,
    int Left,
    int Top,
    int Width,
    int Height,
    double Scale);

sealed class EngineLease(
    OCRService service)
    : IDisposable
{
    public OCRService Service { get; } =
        service;

    public void Dispose()
    {
        try
        {
            Service.FreeEngine();
        }
        catch
        {
            // Worker process sắp kết thúc; không propagate lỗi dispose.
        }
    }
}
