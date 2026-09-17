using System.Text;
using System.Text.Json;
using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public interface IWorkspaceFileService
{
    WorkspaceDirectoryListing List(string? relativePath, int maxEntries);

    Task<WorkspaceTextFile> ReadTextAsync(
        string relativePath,
        int maxCharacters,
        CancellationToken cancellationToken = default);
}

public sealed record WorkspaceDirectoryEntry(
    string Name,
    string Path,
    string Type,
    long? SizeBytes,
    DateTimeOffset LastModifiedAt);

public sealed record WorkspaceDirectoryListing(
    string Path,
    IReadOnlyList<WorkspaceDirectoryEntry> Entries,
    bool Truncated,
    int BlockedSymlinks);

public sealed record WorkspaceTextFile(
    string Path,
    string FileName,
    long SizeBytes,
    DateTimeOffset LastModifiedAt,
    string Content,
    int CharacterCount,
    int ReturnedCharacterCount,
    bool Truncated);

public sealed class WorkspaceFileService : IWorkspaceFileService
{
    public const int MaximumFileBytes = 512 * 1024;
    public const int MaximumReturnedCharacters = 200_000;
    public const int MaximumDirectoryEntries = 200;

    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    private readonly string _root;
    private readonly string _rootWithSeparator;
    private readonly StringComparison _pathComparison;

    public WorkspaceFileService(IConfiguration configuration, IHostEnvironment hostEnvironment)
    {
        var configuredRoot = configuration["Workspace:Root"]?.Trim();
        if (!string.IsNullOrWhiteSpace(configuredRoot))
        {
            var expanded = Environment.ExpandEnvironmentVariables(configuredRoot);
            _root = Path.GetFullPath(
                Path.IsPathRooted(expanded)
                    ? expanded
                    : Path.Combine(hostEnvironment.ContentRootPath, expanded));
        }
        else
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(localAppData))
            {
                localAppData = hostEnvironment.ContentRootPath;
            }

            _root = Path.GetFullPath(Path.Combine(localAppData, "PersonalAI", "Workspace"));
            Directory.CreateDirectory(_root);
        }

        _rootWithSeparator = _root.EndsWith(Path.DirectorySeparatorChar)
            ? _root
            : _root + Path.DirectorySeparatorChar;
        _pathComparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
    }

    public WorkspaceDirectoryListing List(string? relativePath, int maxEntries)
    {
        maxEntries = Math.Clamp(maxEntries, 1, MaximumDirectoryEntries);
        var fullPath = ResolvePath(relativePath, allowRoot: true);
        EnsureNoSymlinkTraversal(fullPath);

        if (!Directory.Exists(fullPath))
        {
            throw new ToolExecutionInputException(
                "Không tìm thấy thư mục trong workspace.");
        }

        string[] candidates;
        try
        {
            candidates = Directory
                .EnumerateFileSystemEntries(fullPath)
                .Take(maxEntries + 1)
                .ToArray();
        }
        catch (UnauthorizedAccessException)
        {
            throw new ToolExecutionInputException(
                "Không có quyền đọc thư mục này trong workspace.");
        }
        catch (IOException)
        {
            throw new ToolExecutionInputException(
                "Không thể đọc thư mục này trong workspace.");
        }

        var truncated = candidates.Length > maxEntries;
        var blockedSymlinks = 0;
        var entries = new List<WorkspaceDirectoryEntry>(maxEntries);

        foreach (var candidate in candidates.Take(maxEntries))
        {
            FileSystemInfo info = Directory.Exists(candidate)
                ? new DirectoryInfo(candidate)
                : new FileInfo(candidate);

            try
            {
                if (IsSymlink(info))
                {
                    blockedSymlinks++;
                    continue;
                }

                var isDirectory = info is DirectoryInfo;
                entries.Add(new WorkspaceDirectoryEntry(
                    info.Name,
                    ToRelativePath(info.FullName),
                    isDirectory ? "directory" : "file",
                    isDirectory ? null : ((FileInfo)info).Length,
                    info.LastWriteTimeUtc));
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }
            catch (IOException)
            {
                continue;
            }
        }

        return new WorkspaceDirectoryListing(
            ToRelativePath(fullPath),
            entries
                .OrderBy(entry => entry.Type == "directory" ? 0 : 1)
                .ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            truncated,
            blockedSymlinks);
    }

    public async Task<WorkspaceTextFile> ReadTextAsync(
        string relativePath,
        int maxCharacters,
        CancellationToken cancellationToken = default)
    {
        maxCharacters = Math.Clamp(maxCharacters, 1, MaximumReturnedCharacters);
        var fullPath = ResolvePath(relativePath, allowRoot: false);
        EnsureNoSymlinkTraversal(fullPath);

        if (!File.Exists(fullPath))
        {
            throw new ToolExecutionInputException(
                "Không tìm thấy tệp trong workspace.");
        }

        var info = new FileInfo(fullPath);
        if (IsSymlink(info))
        {
            throw new ToolExecutionInputException(
                "Không đọc symbolic link hoặc reparse point trong workspace.");
        }

        if (info.Length > MaximumFileBytes)
        {
            throw new ToolExecutionInputException(
                $"Tệp vượt giới hạn đọc {MaximumFileBytes / 1024} KB của workspace tool.");
        }

        byte[] bytes;
        try
        {
            bytes = await File.ReadAllBytesAsync(fullPath, cancellationToken);
        }
        catch (UnauthorizedAccessException)
        {
            throw new ToolExecutionInputException(
                "Không có quyền đọc tệp này trong workspace.");
        }
        catch (IOException)
        {
            throw new ToolExecutionInputException(
                "Không thể đọc tệp này trong workspace.");
        }

        string content;
        try
        {
            content = StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            throw new ToolExecutionInputException(
                "workspace.read_text chỉ đọc tệp văn bản UTF-8 hợp lệ.");
        }

        if (content.Length > 0 && content[0] == '\uFEFF')
        {
            content = content[1..];
        }

        if (content.IndexOf('\0') >= 0)
        {
            throw new ToolExecutionInputException(
                "Tệp có dấu hiệu là dữ liệu nhị phân; workspace.read_text chỉ đọc văn bản.");
        }

        var characterCount = content.Length;
        var truncated = characterCount > maxCharacters;
        if (truncated)
        {
            content = content[..maxCharacters];
        }

        return new WorkspaceTextFile(
            ToRelativePath(fullPath),
            info.Name,
            info.Length,
            info.LastWriteTimeUtc,
            content,
            characterCount,
            content.Length,
            truncated);
    }

    private string ResolvePath(string? relativePath, bool allowRoot)
    {
        if (!Directory.Exists(_root))
        {
            throw new ToolExecutionInputException(
                "Workspace chưa tồn tại. Hãy tạo thư mục đã cấu hình trước khi dùng file tool.");
        }

        var value = string.IsNullOrWhiteSpace(relativePath)
            ? "."
            : relativePath.Trim();

        if (value.IndexOf('\0') >= 0 || Path.IsPathRooted(value))
        {
            throw new ToolExecutionInputException(
                "Đường dẫn workspace phải là đường dẫn tương đối.");
        }

        var segments = value
            .Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Any(segment => segment == ".."))
        {
            throw new ToolExecutionInputException(
                "Không cho phép '..' trong đường dẫn workspace.");
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(Path.Combine(_root, value));
        }
        catch (Exception exception) when (
            exception is ArgumentException
                or NotSupportedException
                or PathTooLongException)
        {
            throw new ToolExecutionInputException(
                "Đường dẫn workspace không hợp lệ.");
        }

        var withinRoot = fullPath.Equals(_root, _pathComparison)
            || fullPath.StartsWith(_rootWithSeparator, _pathComparison);
        if (!withinRoot)
        {
            throw new ToolExecutionInputException(
                "Đường dẫn nằm ngoài workspace đã cấp quyền.");
        }

        if (!allowRoot && fullPath.Equals(_root, _pathComparison))
        {
            throw new ToolExecutionInputException(
                "Cần chỉ định một tệp bên trong workspace.");
        }

        return fullPath;
    }

    private void EnsureNoSymlinkTraversal(string fullPath)
    {
        var relative = Path.GetRelativePath(_root, fullPath);
        if (relative == ".")
        {
            return;
        }

        var current = _root;
        foreach (var segment in relative.Split(
                     Path.DirectorySeparatorChar,
                     StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            FileSystemInfo? info = Directory.Exists(current)
                ? new DirectoryInfo(current)
                : File.Exists(current)
                    ? new FileInfo(current)
                    : null;

            if (info is not null && IsSymlink(info))
            {
                throw new ToolExecutionInputException(
                    "Không cho phép đi qua symbolic link hoặc reparse point trong workspace.");
            }
        }
    }

    private static bool IsSymlink(FileSystemInfo info) =>
        info.LinkTarget is not null
        || (info.Attributes & FileAttributes.ReparsePoint) != 0;

    private string ToRelativePath(string fullPath)
    {
        var relative = Path.GetRelativePath(_root, fullPath);
        return relative == "."
            ? "."
            : relative.Replace('\\', '/');
    }
}

public sealed class WorkspaceListTool(IWorkspaceFileService workspace) : IPersonalAiTool
{
    private static readonly JsonElement Schema = ToolSchema.Parse(
        """
        {
          "type": "object",
          "properties": {
            "path": { "type": "string", "maxLength": 500 },
            "maxEntries": { "type": "integer", "minimum": 1, "maximum": 200 }
          },
          "additionalProperties": false
        }
        """);

    public ToolDefinition Definition { get; } = new(
        "workspace.list",
        "Liệt kê trực tiếp tệp và thư mục trong workspace local đã cấp quyền; chỉ trả đường dẫn tương đối và không đi qua symlink.",
        "1.0.0",
        [ToolPermissions.Read],
        3_000,
        Schema,
        LocalOnly: true,
        RequiresConfirmation: false);

    public Task<JsonElement> ExecuteAsync(
        JsonElement arguments,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = arguments.TryGetProperty("path", out var pathElement)
            ? pathElement.GetString()
            : ".";
        var maxEntries = arguments.TryGetProperty("maxEntries", out var maxEntriesElement)
            ? maxEntriesElement.GetInt32()
            : 100;

        var result = workspace.List(path, maxEntries);
        return Task.FromResult(JsonSerializer.SerializeToElement(result));
    }
}

public sealed class WorkspaceReadTextTool(IWorkspaceFileService workspace) : IPersonalAiTool
{
    private static readonly JsonElement Schema = ToolSchema.Parse(
        """
        {
          "type": "object",
          "properties": {
            "path": { "type": "string", "minLength": 1, "maxLength": 500 },
            "maxCharacters": { "type": "integer", "minimum": 1, "maximum": 200000 }
          },
          "required": ["path"],
          "additionalProperties": false
        }
        """);

    public ToolDefinition Definition { get; } = new(
        "workspace.read_text",
        "Đọc tệp văn bản UTF-8 trong workspace local đã cấp quyền, có giới hạn kích thước, giới hạn nội dung và chặn path traversal/symlink.",
        "1.0.0",
        [ToolPermissions.Read],
        5_000,
        Schema,
        LocalOnly: true,
        RequiresConfirmation: false);

    public async Task<JsonElement> ExecuteAsync(
        JsonElement arguments,
        CancellationToken cancellationToken = default)
    {
        var path = arguments.GetProperty("path").GetString()?.Trim() ?? string.Empty;
        var maxCharacters = arguments.TryGetProperty("maxCharacters", out var maxCharactersElement)
            ? maxCharactersElement.GetInt32()
            : 100_000;

        var result = await workspace.ReadTextAsync(path, maxCharacters, cancellationToken);
        return JsonSerializer.SerializeToElement(result);
    }
}
