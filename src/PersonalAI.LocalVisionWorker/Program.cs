using System.Text.Json;
using CoreOCROnnx.SDK;

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

    if (request is null ||
        !request.Operation.Equals(
            "paddle-ocr",
            StringComparison.OrdinalIgnoreCase) ||
        string.IsNullOrWhiteSpace(
            request.JpegBase64))
    {
        Write(
            new WorkerResponse(
                false,
                "Request worker không hợp lệ.",
                string.Empty,
                Array.Empty<WorkerLine>()));
        return;
    }

    var modelDirectory =
        FindModelDirectory(
            AppContext.BaseDirectory);

    if (modelDirectory is null)
    {
        Write(
            new WorkerResponse(
                false,
                "Không tìm thấy bộ model PP-OCRv6 trong local vision worker output.",
                string.Empty,
                Array.Empty<WorkerLine>()));
        return;
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
        Write(
            new WorkerResponse(
                false,
                $"Khởi tạo CoreOCROnnx thất bại: {init}",
                string.Empty,
                Array.Empty<WorkerLine>()));
        return;
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

    Write(
        new WorkerResponse(
            true,
            $"CoreOCROnnx đọc được {lines.Length} block; detect={result.DbNetTime:0.0}ms; recognize={result.DetectTime:0.0}ms.",
            text,
            lines));
}
catch (Exception exception)
{
    Write(
        new WorkerResponse(
            false,
            $"{exception.GetType().Name}: {exception.Message}",
            string.Empty,
            Array.Empty<WorkerLine>()));
}

void Write(
    WorkerResponse response)
{
    Console.Out.Write(
        JsonSerializer.Serialize(
            response,
            jsonOptions));
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
    string JpegBase64);

sealed record WorkerResponse(
    bool Success,
    string Detail,
    string Text,
    WorkerLine[] Lines);

sealed record WorkerLine(
    string Text,
    WorkerWord[] Words);

sealed record WorkerWord(
    string Text,
    double Left,
    double Top,
    double Width,
    double Height);

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
