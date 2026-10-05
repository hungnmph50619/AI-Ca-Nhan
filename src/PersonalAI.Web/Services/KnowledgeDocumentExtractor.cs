using System.IO.Compression;
using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using DocumentFormat.OpenXml.Spreadsheet;
using DocumentFormat.OpenXml.Presentation;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace PersonalAI.Web.Services;

public sealed record KnowledgeDocumentBlock(
    string Text,
    int? PageNumber = null,
    string? Heading = null,
    string? Section = null);

public sealed record ExtractedKnowledgeDocument(
    string Text,
    int? PageCount,
    IReadOnlyList<KnowledgeDocumentBlock> Blocks);

public sealed class KnowledgeDocumentExtractor
{
    private const int MaximumExtractedCharacters = 2_000_000;
    private const int MaximumPdfPages = 2_000;
    private const long MaximumDocxUncompressedBytes = 50L * 1024 * 1024;
    private const long MaximumOfficeUncompressedBytes = 80L * 1024 * 1024;

    private static readonly IReadOnlyDictionary<string, HashSet<string>> AcceptedContentTypes =
        new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase)
        {
            [".txt"] = new(StringComparer.OrdinalIgnoreCase)
            {
                "text/plain",
                "application/octet-stream"
            },
            [".md"] = new(StringComparer.OrdinalIgnoreCase)
            {
                "text/markdown",
                "text/x-markdown",
                "text/plain",
                "application/octet-stream"
            },
            [".pdf"] = new(StringComparer.OrdinalIgnoreCase)
            {
                "application/pdf",
                "application/x-pdf",
                "application/octet-stream"
            },
            [".docx"] = new(StringComparer.OrdinalIgnoreCase)
            {
                "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                "application/zip",
                "application/x-zip-compressed",
                "application/octet-stream"
            },
            [".xlsx"] = new(StringComparer.OrdinalIgnoreCase)
            {
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                "application/zip",
                "application/x-zip-compressed",
                "application/octet-stream"
            },
            [".pptx"] = new(StringComparer.OrdinalIgnoreCase)
            {
                "application/vnd.openxmlformats-officedocument.presentationml.presentation",
                "application/zip",
                "application/x-zip-compressed",
                "application/octet-stream"
            },
            [".csv"] = new(StringComparer.OrdinalIgnoreCase)
            {
                "text/csv",
                "application/csv",
                "text/plain",
                "application/octet-stream"
            },
            [".json"] = new(StringComparer.OrdinalIgnoreCase)
            {
                "application/json",
                "text/json",
                "text/plain",
                "application/octet-stream"
            }
        };

    public async Task<ExtractedKnowledgeDocument> ExtractAsync(
        IFormFile file,
        CancellationToken cancellationToken = default)
    {
        await using var stream = file.OpenReadStream();
        return await ExtractAsync(
            stream,
            Path.GetFileName(file.FileName),
            file.ContentType,
            cancellationToken);
    }

    public async Task<ExtractedKnowledgeDocument> ExtractAsync(
        Stream source,
        string fileName,
        string? contentType = null,
        CancellationToken cancellationToken = default)
    {
        var extension = Path.GetExtension(Path.GetFileName(fileName)).ToLowerInvariant();
        ValidateContentType(extension, contentType);

        await using var memory = await CopyToMemoryAsync(source, cancellationToken);
        return extension switch
        {
            ".txt" => await ExtractUtf8TextAsync(memory, isMarkdown: false, cancellationToken),
            ".md" => await ExtractUtf8TextAsync(memory, isMarkdown: true, cancellationToken),
            ".csv" or ".json" or ".xml" or ".yaml" or ".yml" or
            ".sql" or ".cs" or ".js" or ".ts" or ".py" or
            ".html" or ".css" =>
                await ExtractUtf8TextAsync(memory, isMarkdown: false, cancellationToken),
            ".pdf" => ExtractPdf(memory, cancellationToken),
            ".docx" => ExtractDocx(memory, cancellationToken),
            ".xlsx" => ExtractXlsx(memory, cancellationToken),
            ".pptx" => ExtractPptx(memory, cancellationToken),
            _ => throw new KnowledgeDocumentValidationException(
                "Định dạng tài liệu chưa được hỗ trợ.")
        };
    }

    private static async Task<ExtractedKnowledgeDocument> ExtractUtf8TextAsync(
        MemoryStream stream,
        bool isMarkdown,
        CancellationToken cancellationToken)
    {
        try
        {
            stream.Position = 0;
            using var reader = new StreamReader(
                stream,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true),
                detectEncodingFromByteOrderMarks: true,
                leaveOpen: true);
            var rawText = await reader.ReadToEndAsync(cancellationToken);
            ValidateExtractedText(rawText);
            var text = NormalizeText(rawText);
            var blocks = isMarkdown
                ? ParseMarkdownBlocks(text)
                : ParsePlainTextBlocks(text);
            return new ExtractedKnowledgeDocument(text, null, blocks);
        }
        catch (DecoderFallbackException)
        {
            throw new KnowledgeDocumentValidationException(
                "Không đọc được bảng mã của tệp. Hãy lưu TXT/Markdown dưới dạng UTF-8 rồi thử lại.");
        }
    }

    private static ExtractedKnowledgeDocument ExtractPdf(
        MemoryStream memory,
        CancellationToken cancellationToken)
    {
        EnsurePdfSignature(memory);

        try
        {
            memory.Position = 0;
            using var document = PdfDocument.Open(memory);
            if (document.NumberOfPages <= 0)
            {
                throw new KnowledgeDocumentValidationException("Tệp PDF không có trang nào.");
            }

            if (document.NumberOfPages > MaximumPdfPages)
            {
                throw new KnowledgeDocumentValidationException(
                    $"PDF có quá nhiều trang. Phiên bản hiện tại hỗ trợ tối đa {MaximumPdfPages:N0} trang mỗi tệp.");
            }

            var builder = new StringBuilder();
            var blocks = new List<KnowledgeDocumentBlock>();

            for (var pageNumber = 1; pageNumber <= document.NumberOfPages; pageNumber++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var page = document.GetPage(pageNumber);
                var pageText = NormalizeText(ContentOrderTextExtractor.GetText(page) ?? string.Empty);
                if (string.IsNullOrWhiteSpace(pageText))
                {
                    continue;
                }

                if (builder.Length > 0)
                {
                    builder.AppendLine().AppendLine();
                }

                builder.Append("[Trang ").Append(pageNumber).AppendLine("]");
                builder.Append(pageText);

                foreach (var paragraph in SplitParagraphs(pageText))
                {
                    blocks.Add(new KnowledgeDocumentBlock(
                        paragraph,
                        PageNumber: pageNumber,
                        Section: $"Trang {pageNumber}"));
                }

                if (builder.Length > MaximumExtractedCharacters)
                {
                    throw new KnowledgeDocumentValidationException(
                        "Nội dung văn bản trích xuất quá lớn. Hãy chia tài liệu thành các tệp nhỏ hơn.");
                }
            }

            var text = builder.ToString();
            if (string.IsNullOrWhiteSpace(text))
            {
                throw new KnowledgeDocumentValidationException(
                    "PDF không có lớp văn bản có thể đọc. Chức năng nhận dạng chữ từ ảnh sẽ được bổ sung ở phiên bản sau.");
            }

            ValidateExtractedText(text);
            return new ExtractedKnowledgeDocument(
                NormalizeText(text),
                document.NumberOfPages,
                blocks);
        }
        catch (KnowledgeDocumentValidationException)
        {
            throw;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new KnowledgeDocumentValidationException(
                "Không đọc được PDF. Tệp có thể bị hỏng, được mã hóa hoặc dùng cấu trúc chưa được hỗ trợ.");
        }
    }

    private static ExtractedKnowledgeDocument ExtractDocx(
        MemoryStream memory,
        CancellationToken cancellationToken)
    {
        ValidateDocxContainer(memory);

        try
        {
            memory.Position = 0;
            using var document = WordprocessingDocument.Open(memory, false);
            var body = document.MainDocumentPart?.Document?.Body;
            if (body is null)
            {
                throw new KnowledgeDocumentValidationException("DOCX không có nội dung tài liệu Word hợp lệ.");
            }

            var blocks = new List<KnowledgeDocumentBlock>();
            string? currentHeading = null;
            string? currentSection = null;

            foreach (var element in body.ChildElements)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (element is Paragraph paragraph)
                {
                    var paragraphText = GetParagraphText(paragraph);
                    if (paragraphText.Length == 0)
                    {
                        continue;
                    }

                    var headingLevel = GetHeadingLevel(paragraph);
                    if (headingLevel.HasValue)
                    {
                        currentHeading = paragraphText;
                        if (headingLevel.Value <= 1 || currentSection is null)
                        {
                            currentSection = paragraphText;
                        }

                        blocks.Add(new KnowledgeDocumentBlock(
                            paragraphText,
                            Heading: currentHeading,
                            Section: currentSection));
                        continue;
                    }

                    blocks.Add(new KnowledgeDocumentBlock(
                        paragraphText,
                        Heading: currentHeading,
                        Section: currentSection));
                }
                else if (element is Table table)
                {
                    foreach (var row in table.Elements<TableRow>())
                    {
                        var cells = row.Elements<TableCell>()
                            .Select(cell => string.Join(
                                " ",
                                cell.Descendants<Paragraph>()
                                    .Select(GetParagraphText)
                                    .Where(text => text.Length > 0)))
                            .Where(text => text.Length > 0)
                            .ToArray();
                        if (cells.Length == 0)
                        {
                            continue;
                        }

                        blocks.Add(new KnowledgeDocumentBlock(
                            string.Join(" | ", cells),
                            Heading: currentHeading,
                            Section: currentSection));
                    }
                }
            }

            var text = string.Join(
                "\n\n",
                blocks.Select(block => block.Text).Where(text => !string.IsNullOrWhiteSpace(text)));
            ValidateExtractedText(text);
            return new ExtractedKnowledgeDocument(NormalizeText(text), null, blocks);
        }
        catch (KnowledgeDocumentValidationException)
        {
            throw;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new KnowledgeDocumentValidationException(
                "Không đọc được DOCX. Tệp có thể bị hỏng hoặc không phải tài liệu Word hợp lệ.");
        }
    }

    private static ExtractedKnowledgeDocument ExtractXlsx(
        MemoryStream memory,
        CancellationToken cancellationToken)
    {
        ValidateOpenXmlContainer(
            memory,
            "xl/workbook.xml",
            "XLSX");

        try
        {
            memory.Position = 0;
            using var document = SpreadsheetDocument.Open(memory, false);
            var workbookPart = document.WorkbookPart
                ?? throw new KnowledgeDocumentValidationException(
                    "XLSX không có workbook hợp lệ.");

            var sharedStrings = workbookPart.SharedStringTablePart?
                .SharedStringTable?
                .Elements<SharedStringItem>()
                .Select(item => item.InnerText)
                .ToArray()
                ?? Array.Empty<string>();

            var blocks = new List<KnowledgeDocumentBlock>();
            foreach (var sheet in workbookPart.Workbook.Sheets?.Elements<Sheet>()
                         ?? Enumerable.Empty<Sheet>())
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (sheet.Id?.Value is not { Length: > 0 } relationshipId)
                    continue;

                if (workbookPart.GetPartById(relationshipId) is not WorksheetPart worksheetPart)
                    continue;

                var sheetName = sheet.Name?.Value ?? "Sheet";
                foreach (var row in worksheetPart.Worksheet.Descendants<Row>())
                {
                    var values = row.Elements<Cell>()
                        .Select(cell => ReadSpreadsheetCell(cell, sharedStrings))
                        .Where(value => !string.IsNullOrWhiteSpace(value))
                        .ToArray();

                    if (values.Length == 0)
                        continue;

                    blocks.Add(new KnowledgeDocumentBlock(
                        string.Join(" | ", values),
                        Section: sheetName));
                }
            }

            var text = string.Join(
                "\n",
                blocks.Select(block => $"[{block.Section}] {block.Text}"));

            ValidateExtractedText(text);
            return new ExtractedKnowledgeDocument(
                NormalizeText(text),
                null,
                blocks);
        }
        catch (KnowledgeDocumentValidationException)
        {
            throw;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new KnowledgeDocumentValidationException(
                "Không đọc được XLSX. Tệp có thể bị hỏng hoặc không phải bảng tính Excel hợp lệ.");
        }
        finally
        {
            memory.Position = 0;
        }
    }

    private static string ReadSpreadsheetCell(
        Cell cell,
        IReadOnlyList<string> sharedStrings)
    {
        if (cell.DataType?.Value == CellValues.SharedString &&
            int.TryParse(cell.CellValue?.Text, out var sharedIndex) &&
            sharedIndex >= 0 &&
            sharedIndex < sharedStrings.Count)
        {
            return sharedStrings[sharedIndex].Trim();
        }

        if (cell.DataType?.Value == CellValues.InlineString)
            return cell.InlineString?.InnerText?.Trim() ?? string.Empty;

        return (cell.CellValue?.Text ?? cell.InnerText ?? string.Empty).Trim();
    }

    private static ExtractedKnowledgeDocument ExtractPptx(
        MemoryStream memory,
        CancellationToken cancellationToken)
    {
        ValidateOpenXmlContainer(
            memory,
            "ppt/presentation.xml",
            "PPTX");

        try
        {
            memory.Position = 0;
            using var document = PresentationDocument.Open(memory, false);
            var presentationPart = document.PresentationPart
                ?? throw new KnowledgeDocumentValidationException(
                    "PPTX không có presentation hợp lệ.");

            var slideIds = presentationPart.Presentation.SlideIdList?
                .Elements<SlideId>()
                .ToArray()
                ?? Array.Empty<SlideId>();

            var blocks = new List<KnowledgeDocumentBlock>();

            for (var index = 0; index < slideIds.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var relationshipId = slideIds[index].RelationshipId?.Value;
                if (string.IsNullOrWhiteSpace(relationshipId))
                    continue;

                if (presentationPart.GetPartById(relationshipId) is not SlidePart slidePart)
                    continue;

                var lines = slidePart.Slide
                    .Descendants<DocumentFormat.OpenXml.Drawing.Text>()
                    .Select(text => text.Text?.Trim() ?? string.Empty)
                    .Where(text => text.Length > 0)
                    .ToArray();

                if (lines.Length == 0)
                    continue;

                var slideNumber = index + 1;
                blocks.Add(new KnowledgeDocumentBlock(
                    string.Join("\n", lines),
                    PageNumber: slideNumber,
                    Section: $"Slide {slideNumber}"));
            }

            var text = string.Join(
                "\n\n",
                blocks.Select(block =>
                    $"[{block.Section}]\n{block.Text}"));

            ValidateExtractedText(text);
            return new ExtractedKnowledgeDocument(
                NormalizeText(text),
                slideIds.Length,
                blocks);
        }
        catch (KnowledgeDocumentValidationException)
        {
            throw;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new KnowledgeDocumentValidationException(
                "Không đọc được PPTX. Tệp có thể bị hỏng hoặc không phải bài trình chiếu PowerPoint hợp lệ.");
        }
        finally
        {
            memory.Position = 0;
        }
    }

    private static IReadOnlyList<KnowledgeDocumentBlock> ParsePlainTextBlocks(string text) =>
        SplitParagraphs(text)
            .Select(paragraph => new KnowledgeDocumentBlock(paragraph))
            .ToArray();

    private static IReadOnlyList<KnowledgeDocumentBlock> ParseMarkdownBlocks(string text)
    {
        var blocks = new List<KnowledgeDocumentBlock>();
        var paragraph = new StringBuilder();
        string? currentHeading = null;
        string? currentSection = null;

        void FlushParagraph()
        {
            var value = paragraph.ToString().Trim();
            paragraph.Clear();
            if (value.Length > 0)
            {
                blocks.Add(new KnowledgeDocumentBlock(
                    value,
                    Heading: currentHeading,
                    Section: currentSection));
            }
        }

        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.TrimEnd();
            if (TryParseMarkdownHeading(line, out var level, out var heading))
            {
                FlushParagraph();
                currentHeading = heading;
                if (level <= 1 || currentSection is null)
                {
                    currentSection = heading;
                }

                blocks.Add(new KnowledgeDocumentBlock(
                    heading,
                    Heading: currentHeading,
                    Section: currentSection));
                continue;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                FlushParagraph();
                continue;
            }

            if (paragraph.Length > 0)
            {
                paragraph.AppendLine();
            }
            paragraph.Append(line.Trim());
        }

        FlushParagraph();
        return blocks;
    }

    private static bool TryParseMarkdownHeading(
        string line,
        out int level,
        out string heading)
    {
        level = 0;
        heading = string.Empty;
        var trimmed = line.TrimStart();
        while (level < trimmed.Length && level < 6 && trimmed[level] == '#')
        {
            level++;
        }

        if (level == 0 || level >= trimmed.Length || !char.IsWhiteSpace(trimmed[level]))
        {
            level = 0;
            return false;
        }

        heading = trimmed[(level + 1)..].Trim().TrimEnd('#').Trim();
        return heading.Length > 0;
    }

    private static int? GetHeadingLevel(Paragraph paragraph)
    {
        var styleId = paragraph.ParagraphProperties?.ParagraphStyleId?.Val?.Value;
        if (string.IsNullOrWhiteSpace(styleId))
        {
            return null;
        }

        if (styleId.Equals("Title", StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        if (styleId.Equals("Subtitle", StringComparison.OrdinalIgnoreCase))
        {
            return 2;
        }

        if (!styleId.StartsWith("Heading", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var suffix = styleId["Heading".Length..].Trim();
        return int.TryParse(suffix, out var level) && level > 0
            ? Math.Min(level, 9)
            : 1;
    }

    private static string GetParagraphText(Paragraph paragraph) =>
        string.Concat(paragraph.Descendants<DocumentFormat.OpenXml.Wordprocessing.Text>().Select(text => text.Text)).Trim();

    private static IEnumerable<string> SplitParagraphs(string text)
    {
        var normalized = NormalizeText(text);
        var paragraph = new StringBuilder();

        foreach (var rawLine in normalized.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
            {
                var value = paragraph.ToString().Trim();
                paragraph.Clear();
                if (value.Length > 0)
                {
                    yield return value;
                }
                continue;
            }

            if (paragraph.Length > 0)
            {
                paragraph.AppendLine();
            }
            paragraph.Append(line);
        }

        var finalValue = paragraph.ToString().Trim();
        if (finalValue.Length > 0)
        {
            yield return finalValue;
        }
    }

    private static async Task<MemoryStream> CopyToMemoryAsync(
        Stream source,
        CancellationToken cancellationToken)
    {
        var memory = new MemoryStream();
        if (source.CanSeek)
        {
            source.Position = 0;
        }
        await source.CopyToAsync(memory, cancellationToken);
        memory.Position = 0;
        return memory;
    }

    private static void EnsurePdfSignature(MemoryStream stream)
    {
        if (stream.Length < 5)
        {
            throw new KnowledgeDocumentValidationException("Tệp PDF không hợp lệ.");
        }

        stream.Position = 0;
        Span<byte> signature = stackalloc byte[5];
        var bytesRead = stream.Read(signature);
        stream.Position = 0;
        if (bytesRead != 5 || signature[0] != (byte)'%' || signature[1] != (byte)'P'
            || signature[2] != (byte)'D' || signature[3] != (byte)'F' || signature[4] != (byte)'-')
        {
            throw new KnowledgeDocumentValidationException(
                "Phần mở rộng là PDF nhưng nội dung tệp không có chữ ký PDF hợp lệ.");
        }
    }

    private static void ValidateOpenXmlContainer(
        MemoryStream stream,
        string requiredEntry,
        string label)
    {
        try
        {
            stream.Position = 0;
            using var archive =
                new ZipArchive(
                    stream,
                    ZipArchiveMode.Read,
                    leaveOpen: true);

            var totalUncompressedBytes =
                archive.Entries.Sum(entry => entry.Length);

            if (totalUncompressedBytes > MaximumOfficeUncompressedBytes)
            {
                throw new KnowledgeDocumentValidationException(
                    $"{label} giải nén quá lớn. Hãy chia tệp thành các phần nhỏ hơn.");
            }

            if (archive.GetEntry("[Content_Types].xml") is null ||
                archive.GetEntry(requiredEntry) is null)
            {
                throw new KnowledgeDocumentValidationException(
                    $"Phần mở rộng là {label} nhưng cấu trúc tệp không hợp lệ.");
            }
        }
        catch (KnowledgeDocumentValidationException)
        {
            throw;
        }
        catch (InvalidDataException)
        {
            throw new KnowledgeDocumentValidationException(
                $"Phần mở rộng là {label} nhưng tệp không phải gói OpenXML hợp lệ.");
        }
        finally
        {
            stream.Position = 0;
        }
    }

    private static void ValidateDocxContainer(MemoryStream stream)
    {
        try
        {
            stream.Position = 0;
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
            var totalUncompressedBytes = archive.Entries.Sum(entry => entry.Length);
            if (totalUncompressedBytes > MaximumDocxUncompressedBytes)
            {
                throw new KnowledgeDocumentValidationException(
                    "DOCX giải nén quá lớn. Hãy chia tài liệu thành các tệp nhỏ hơn.");
            }

            var hasContentTypes = archive.GetEntry("[Content_Types].xml") is not null;
            var hasWordDocument = archive.GetEntry("word/document.xml") is not null;
            if (!hasContentTypes || !hasWordDocument)
            {
                throw new KnowledgeDocumentValidationException(
                    "Phần mở rộng là DOCX nhưng cấu trúc tệp không phải tài liệu Word hợp lệ.");
            }
        }
        catch (KnowledgeDocumentValidationException)
        {
            throw;
        }
        catch (InvalidDataException)
        {
            throw new KnowledgeDocumentValidationException(
                "Phần mở rộng là DOCX nhưng tệp không phải gói tài liệu Word hợp lệ.");
        }
        finally
        {
            stream.Position = 0;
        }
    }

    private static void ValidateContentType(string extension, string? contentType)
    {
        if (!AcceptedContentTypes.TryGetValue(extension, out var accepted))
        {
            return;
        }

        var normalized = contentType?.Split(';', 2)[0].Trim();
        if (string.IsNullOrWhiteSpace(normalized) || accepted.Contains(normalized))
        {
            return;
        }

        throw new KnowledgeDocumentValidationException(
            $"Kiểu nội dung “{normalized}” không phù hợp với tệp {extension.ToUpperInvariant()}.");
    }

    private static void ValidateExtractedText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new KnowledgeDocumentValidationException("Tài liệu không có nội dung văn bản có thể đọc.");
        }

        if (text.IndexOf('\0') >= 0)
        {
            throw new KnowledgeDocumentValidationException("Tài liệu chứa dữ liệu văn bản không hợp lệ.");
        }

        if (text.Length > MaximumExtractedCharacters)
        {
            throw new KnowledgeDocumentValidationException(
                "Nội dung văn bản trích xuất quá lớn. Hãy chia tài liệu thành các tệp nhỏ hơn.");
        }
    }

    private static string NormalizeText(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Trim();
}
