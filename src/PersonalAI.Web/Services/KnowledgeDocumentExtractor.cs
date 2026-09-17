using System.IO.Compression;
using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace PersonalAI.Web.Services;

public sealed record ExtractedKnowledgeDocument(string Text, int? PageCount);

public sealed class KnowledgeDocumentExtractor
{
    private const int MaximumExtractedCharacters = 2_000_000;
    private const int MaximumPdfPages = 2_000;
    private const long MaximumDocxUncompressedBytes = 50L * 1024 * 1024;

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
            }
        };

    public async Task<ExtractedKnowledgeDocument> ExtractAsync(
        IFormFile file,
        CancellationToken cancellationToken = default)
    {
        var extension = Path.GetExtension(Path.GetFileName(file.FileName)).ToLowerInvariant();
        ValidateContentType(extension, file.ContentType);

        return extension switch
        {
            ".txt" or ".md" => await ExtractUtf8TextAsync(file, cancellationToken),
            ".pdf" => await ExtractPdfAsync(file, cancellationToken),
            ".docx" => await ExtractDocxAsync(file, cancellationToken),
            _ => throw new KnowledgeDocumentValidationException(
                "Định dạng tài liệu chưa được hỗ trợ. Hãy dùng PDF, DOCX, TXT hoặc Markdown (.md).")
        };
    }

    private static async Task<ExtractedKnowledgeDocument> ExtractUtf8TextAsync(
        IFormFile file,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = file.OpenReadStream();
            using var reader = new StreamReader(
                stream,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true),
                detectEncodingFromByteOrderMarks: true);
            var text = await reader.ReadToEndAsync(cancellationToken);
            ValidateExtractedText(text);
            return new ExtractedKnowledgeDocument(NormalizeText(text), null);
        }
        catch (DecoderFallbackException)
        {
            throw new KnowledgeDocumentValidationException(
                "Không đọc được bảng mã của tệp. Hãy lưu TXT/Markdown dưới dạng UTF-8 rồi thử lại.");
        }
    }

    private static async Task<ExtractedKnowledgeDocument> ExtractPdfAsync(
        IFormFile file,
        CancellationToken cancellationToken)
    {
        await using var memory = await CopyToMemoryAsync(file, cancellationToken);
        EnsurePdfSignature(memory);

        try
        {
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
            for (var pageNumber = 1; pageNumber <= document.NumberOfPages; pageNumber++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var page = document.GetPage(pageNumber);
                var pageText = ContentOrderTextExtractor.GetText(page)?.Trim();
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
                    "PDF không có lớp văn bản có thể đọc. OCR cho PDF ảnh sẽ được bổ sung ở phiên bản sau.");
            }

            ValidateExtractedText(text);
            return new ExtractedKnowledgeDocument(NormalizeText(text), document.NumberOfPages);
        }
        catch (KnowledgeDocumentValidationException)
        {
            throw;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new KnowledgeDocumentValidationException(
                $"Không đọc được PDF. Tệp có thể bị hỏng, được mã hóa hoặc dùng cấu trúc chưa được hỗ trợ. ({exception.GetType().Name})");
        }
    }

    private static async Task<ExtractedKnowledgeDocument> ExtractDocxAsync(
        IFormFile file,
        CancellationToken cancellationToken)
    {
        await using var memory = await CopyToMemoryAsync(file, cancellationToken);
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

            var builder = new StringBuilder();
            foreach (var paragraph in body.Descendants<Paragraph>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var paragraphText = string.Concat(
                        paragraph.Descendants<Text>().Select(text => text.Text))
                    .Trim();
                if (paragraphText.Length == 0)
                {
                    continue;
                }

                if (builder.Length > 0)
                {
                    builder.AppendLine().AppendLine();
                }
                builder.Append(paragraphText);

                if (builder.Length > MaximumExtractedCharacters)
                {
                    throw new KnowledgeDocumentValidationException(
                        "Nội dung văn bản trích xuất quá lớn. Hãy chia tài liệu thành các tệp nhỏ hơn.");
                }
            }

            var text = builder.ToString();
            ValidateExtractedText(text);
            return new ExtractedKnowledgeDocument(NormalizeText(text), null);
        }
        catch (KnowledgeDocumentValidationException)
        {
            throw;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new KnowledgeDocumentValidationException(
                $"Không đọc được DOCX. Tệp có thể bị hỏng hoặc không phải tài liệu Word hợp lệ. ({exception.GetType().Name})");
        }
    }

    private static async Task<MemoryStream> CopyToMemoryAsync(
        IFormFile file,
        CancellationToken cancellationToken)
    {
        var memory = new MemoryStream(capacity: checked((int)Math.Min(file.Length, int.MaxValue)));
        await file.CopyToAsync(memory, cancellationToken);
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
