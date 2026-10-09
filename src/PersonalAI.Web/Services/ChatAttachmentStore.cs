using System.Text.Json;
using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public sealed record StoredChatAttachment(
    Guid Id,
    string WorkspaceId,
    string FileName,
    string MimeType,
    long Size,
    string Kind,
    string Route,
    string Path,
    DateTimeOffset CreatedAt,
    Guid? KnowledgeDocumentId = null,
    int KnowledgeChunkCount = 0);

public interface IChatAttachmentStore
{
    Task<ChatAttachmentReference> AddAsync(
        IFormFile file,
        CancellationToken cancellationToken = default);

    StoredChatAttachment GetRequired(
        Guid id);

    byte[] ReadAllBytes(
        Guid id);

    ChatAttachmentReference MarkKnowledgeDocument(
        Guid id,
        Guid documentId,
        int chunkCount);
}

public sealed class ChatAttachmentStore(
    IWorkspaceStoragePathResolver storagePaths) : IChatAttachmentStore
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web)
        {
            WriteIndented = true
        };

    public const long MaximumFileSize =
        10 * 1024 * 1024;

    public const int MaximumAttachmentsPerMessage =
        4;

    private static readonly HashSet<string> AllowedMimeTypes =
        new(
            [
                "image/png",
                "image/jpeg",
                "image/webp",
                "application/pdf",
                "text/plain",
                "text/markdown",
                "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                "application/vnd.openxmlformats-officedocument.presentationml.presentation",
                "text/csv",
                "application/csv",
                "application/json",
                "text/json",
                "application/xml",
                "text/xml",
                "application/x-yaml",
                "text/yaml",
                "text/x-yaml",
                "text/x-python",
                "text/javascript",
                "application/javascript",
                "text/css",
                "text/html",
                "application/sql",
                "text/x-sql",
                "application/zip",
                "application/x-zip-compressed",
                "application/octet-stream"
            ],
            StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> AllowedExtensions =
        new(
            [
                ".png",
                ".jpg",
                ".jpeg",
                ".webp",
                ".pdf",
                ".txt",
                ".md",
                ".docx",
                ".xlsx",
                ".pptx",
                ".csv",
                ".json",
                ".xml",
                ".yaml",
                ".yml",
                ".sql",
                ".cs",
                ".js",
                ".ts",
                ".py",
                ".html",
                ".css"
            ],
            StringComparer.OrdinalIgnoreCase);

    private readonly object _gate =
        new();

    private readonly Dictionary<Guid, StoredChatAttachment> _items =
        [];

    public async Task<ChatAttachmentReference> AddAsync(
        IFormFile file,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            file);

        if (file.Length <= 0)
            throw new ChatValidationException(
                "Tệp đính kèm đang trống.");

        if (file.Length > MaximumFileSize)
            throw new ChatValidationException(
                "Mỗi tệp đính kèm không được lớn hơn 10 MB.");

        var extension =
            Path.GetExtension(
                file.FileName);

        var mimeType =
            string.IsNullOrWhiteSpace(
                file.ContentType)
                ? GuessMimeType(
                    extension)
                : file.ContentType.Trim();

        if (!AllowedExtensions.Contains(
                extension) ||
            !AllowedMimeTypes.Contains(
                mimeType))
        {
            throw new ChatValidationException(
                "Chat hỗ trợ ảnh, PDF, Word, Excel, PowerPoint, TXT/Markdown, CSV/JSON và các tệp mã nguồn phổ biến.");
        }

        var kind =
            mimeType.StartsWith(
                "image/",
                StringComparison.OrdinalIgnoreCase)
                ? "image"
                : IsTextExtension(
                    extension)
                    ? "text"
                    : "document";

        var route =
            SelectRoute(
                extension,
                kind,
                file.Length);

        var workspaceId =
            storagePaths.CurrentWorkspaceId;

        var directory =
            GetAttachmentDirectory(
                workspaceId);

        Directory.CreateDirectory(
            directory);

        var id =
            Guid.NewGuid();

        var safeName =
            Path.GetFileName(
                file.FileName);

        var storedPath =
            Path.Combine(
                directory,
                $"{id:N}{extension.ToLowerInvariant()}");

        await using (var stream =
                     new FileStream(
                         storedPath,
                         FileMode.CreateNew,
                         FileAccess.Write,
                         FileShare.None,
                         81920,
                         useAsync: true))
        {
            await file.CopyToAsync(
                stream,
                cancellationToken);
        }

        var item =
            new StoredChatAttachment(
                id,
                workspaceId,
                safeName,
                mimeType,
                file.Length,
                kind,
                route,
                storedPath,
                DateTimeOffset.UtcNow);

        var metadataPath =
            GetMetadataPath(
                directory,
                id);

        await File.WriteAllTextAsync(
            metadataPath,
            JsonSerializer.Serialize(
                item,
                JsonOptions),
            cancellationToken);

        lock (_gate)
        {
            _items[id] =
                item;
        }

        return new ChatAttachmentReference(
            item.Id,
            item.FileName,
            item.MimeType,
            item.Size,
            item.Kind,
            item.Route,
            item.KnowledgeDocumentId,
            item.KnowledgeChunkCount);
    }

    public StoredChatAttachment GetRequired(
        Guid id)
    {
        StoredChatAttachment? item;

        lock (_gate)
        {
            _items.TryGetValue(
                id,
                out item);
        }

        if (item is null)
        {
            var directory =
                GetAttachmentDirectory(
                    storagePaths.CurrentWorkspaceId);

            var metadataPath =
                GetMetadataPath(
                    directory,
                    id);

            if (File.Exists(
                    metadataPath))
            {
                try
                {
                    item =
                        JsonSerializer.Deserialize<StoredChatAttachment>(
                            File.ReadAllText(
                                metadataPath),
                            JsonOptions);

                    if (item is not null)
                    {
                        lock (_gate)
                        {
                            _items[id] =
                                item;
                        }
                    }
                }
                catch (Exception exception) when (
                    exception is IOException or
                    UnauthorizedAccessException or
                    JsonException)
                {
                    item =
                        null;
                }
            }
        }

        if (item is null ||
            !item.WorkspaceId.Equals(
                storagePaths.CurrentWorkspaceId,
                StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(
                item.Path))
        {
            throw new ChatValidationException(
                "Tệp đính kèm không còn khả dụng. Hãy đính kèm lại tệp.");
        }

        return item;
    }

    public byte[] ReadAllBytes(
        Guid id)
    {
        var item =
            GetRequired(
                id);

        return File.ReadAllBytes(
            item.Path);
    }

    public ChatAttachmentReference MarkKnowledgeDocument(
        Guid id,
        Guid documentId,
        int chunkCount)
    {
        var current =
            GetRequired(
                id);

        var updated =
            current with
            {
                Route =
                    "knowledge",
                KnowledgeDocumentId =
                    documentId,
                KnowledgeChunkCount =
                    Math.Max(
                        0,
                        chunkCount)
            };

        var directory =
            GetAttachmentDirectory(
                updated.WorkspaceId);

        Directory.CreateDirectory(
            directory);

        File.WriteAllText(
            GetMetadataPath(
                directory,
                id),
            JsonSerializer.Serialize(
                updated,
                JsonOptions));

        lock (_gate)
        {
            _items[id] =
                updated;
        }

        return new ChatAttachmentReference(
            updated.Id,
            updated.FileName,
            updated.MimeType,
            updated.Size,
            updated.Kind,
            updated.Route,
            updated.KnowledgeDocumentId,
            updated.KnowledgeChunkCount);
    }

    internal static string SelectRouteForAcceptance(
        string extension,
        string kind,
        long size) =>
        SelectRoute(
            extension,
            kind,
            size);

    private static string SelectRoute(
        string extension,
        string kind,
        long size)
    {
        if (kind.Equals(
                "image",
                StringComparison.OrdinalIgnoreCase))
        {
            return "direct";
        }

        if (extension.Equals(
                ".docx",
                StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(
                ".xlsx",
                StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(
                ".pptx",
                StringComparison.OrdinalIgnoreCase))
        {
            return "knowledge";
        }

        if (extension.Equals(
                ".pdf",
                StringComparison.OrdinalIgnoreCase))
        {
            return size > 4 * 1024 * 1024
                ? "knowledge"
                : "direct";
        }

        return size > 1024 * 1024
            ? "knowledge"
            : "direct";
    }

    private static string GetAttachmentDirectory(
        string workspaceId)
    {
        var localData =
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData);

        if (string.IsNullOrWhiteSpace(
                localData))
        {
            localData =
                Path.Combine(
                    Environment.GetFolderPath(
                        Environment.SpecialFolder.UserProfile),
                    ".personalai");
        }

        return Path.Combine(
            localData,
            "PersonalAI",
            "Workspaces",
            workspaceId,
            "ChatAttachments");
    }

    private static string GetMetadataPath(
        string directory,
        Guid id) =>
        Path.Combine(
            directory,
            $"{id:N}.json");

    private static string GuessMimeType(
        string extension) =>
        extension.ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".webp" => "image/webp",
            ".pdf" => "application/pdf",
            ".md" => "text/markdown",
            ".txt" => "text/plain",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            ".pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
            ".csv" => "text/csv",
            ".json" => "application/json",
            ".xml" => "application/xml",
            ".yaml" or ".yml" => "text/yaml",
            ".js" => "text/javascript",
            ".css" => "text/css",
            ".html" => "text/html",
            ".py" => "text/x-python",
            ".sql" => "text/x-sql",
            ".cs" or ".ts" => "text/plain",
            _ => "application/octet-stream"
        };

    private static bool IsTextExtension(
        string extension) =>
        extension.Equals(".txt", StringComparison.OrdinalIgnoreCase) ||
        extension.Equals(".md", StringComparison.OrdinalIgnoreCase) ||
        extension.Equals(".csv", StringComparison.OrdinalIgnoreCase) ||
        extension.Equals(".json", StringComparison.OrdinalIgnoreCase) ||
        extension.Equals(".xml", StringComparison.OrdinalIgnoreCase) ||
        extension.Equals(".yaml", StringComparison.OrdinalIgnoreCase) ||
        extension.Equals(".yml", StringComparison.OrdinalIgnoreCase) ||
        extension.Equals(".sql", StringComparison.OrdinalIgnoreCase) ||
        extension.Equals(".cs", StringComparison.OrdinalIgnoreCase) ||
        extension.Equals(".js", StringComparison.OrdinalIgnoreCase) ||
        extension.Equals(".ts", StringComparison.OrdinalIgnoreCase) ||
        extension.Equals(".py", StringComparison.OrdinalIgnoreCase) ||
        extension.Equals(".html", StringComparison.OrdinalIgnoreCase) ||
        extension.Equals(".css", StringComparison.OrdinalIgnoreCase);
}
