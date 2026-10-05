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
    DateTimeOffset CreatedAt);

public interface IChatAttachmentStore
{
    Task<ChatAttachmentReference> AddAsync(
        IFormFile file,
        CancellationToken cancellationToken = default);

    StoredChatAttachment GetRequired(
        Guid id);

    byte[] ReadAllBytes(
        Guid id);
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
                "text/markdown"
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
                ".md"
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
                "Hiện tại chat hỗ trợ PNG, JPG, WEBP, PDF, TXT và Markdown.");
        }

        var kind =
            mimeType.StartsWith(
                "image/",
                StringComparison.OrdinalIgnoreCase)
                ? "image"
                : mimeType.Equals(
                    "application/pdf",
                    StringComparison.OrdinalIgnoreCase)
                    ? "document"
                    : "text";

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
                "direct",
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
            item.Route);
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
            _ => "application/octet-stream"
        };
}
