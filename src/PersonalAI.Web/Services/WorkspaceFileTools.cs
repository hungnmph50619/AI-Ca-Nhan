using System.Security.Cryptography;
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

    Task<WorkspaceWriteResult> WriteTextAsync(
        string relativePath,
        string content,
        string mode,
        string? expectedSha256,
        CancellationToken cancellationToken = default);

    WorkspaceDirectoryWriteResult CreateDirectory(string relativePath);

    Task<WorkspaceMoveResult> MoveAsync(
        string sourcePath,
        string destinationPath,
        string? expectedSha256,
        CancellationToken cancellationToken = default);

    Task<WorkspaceDeleteResult> DeleteAsync(
        string relativePath,
        string? expectedSha256,
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

public sealed record WorkspaceWriteResult(
    string Path,
    string Mode,
    bool Created,
    int BytesWritten,
    long SizeBytes,
    string? PreviousSha256,
    string Sha256,
    DateTimeOffset LastModifiedAt);

public sealed record WorkspaceDirectoryWriteResult(
    string Path,
    bool Created,
    DateTimeOffset LastModifiedAt);

public sealed record WorkspaceMoveResult(
    string SourcePath,
    string DestinationPath,
    string Type,
    long? SizeBytes,
    string? Sha256,
    DateTimeOffset LastModifiedAt);

public sealed record WorkspaceDeleteResult(
    string Path,
    string Type,
    long? SizeBytes,
    string? Sha256,
    bool Deleted);

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
                "Không tìm thấy thư mục trong thư mục làm việc.");
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
                "Không có quyền đọc thư mục này trong thư mục làm việc.");
        }
        catch (IOException)
        {
            throw new ToolExecutionInputException(
                "Không thể đọc thư mục này trong thư mục làm việc.");
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
                "Không tìm thấy tệp trong thư mục làm việc.");
        }

        var info = new FileInfo(fullPath);
        if (IsSymlink(info))
        {
            throw new ToolExecutionInputException(
                "Không đọc liên kết tượng trưng hoặc điểm tái phân tích trong thư mục làm việc.");
        }

        if (info.Length > MaximumFileBytes)
        {
            throw new ToolExecutionInputException(
                $"Tệp vượt giới hạn đọc {MaximumFileBytes / 1024} KB của công cụ thư mục làm việc.");
        }

        byte[] bytes;
        try
        {
            bytes = await File.ReadAllBytesAsync(fullPath, cancellationToken);
        }
        catch (UnauthorizedAccessException)
        {
            throw new ToolExecutionInputException(
                "Không có quyền đọc tệp này trong thư mục làm việc.");
        }
        catch (IOException)
        {
            throw new ToolExecutionInputException(
                "Không thể đọc tệp này trong thư mục làm việc.");
        }

        string content;
        try
        {
            content = StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            throw new ToolExecutionInputException(
                "Công cụ đọc tệp chỉ hỗ trợ văn bản UTF-8 hợp lệ.");
        }

        if (content.Length > 0 && content[0] == '\uFEFF')
        {
            content = content[1..];
        }

        if (content.IndexOf('\0') >= 0)
        {
            throw new ToolExecutionInputException(
                "Tệp có dấu hiệu là dữ liệu nhị phân; công cụ đọc tệp chỉ hỗ trợ văn bản.");
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


    public async Task<WorkspaceWriteResult> WriteTextAsync(
        string relativePath,
        string content,
        string mode,
        string? expectedSha256,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var normalizedMode = (mode ?? string.Empty).Trim().ToLowerInvariant();
        if (normalizedMode is not ("create" or "overwrite" or "append"))
        {
            throw new ToolExecutionInputException(
                "Cách ghi phải là tạo mới, ghi đè hoặc nối thêm.");
        }

        if (content.Length > MaximumReturnedCharacters)
        {
            throw new ToolExecutionInputException(
                $"Nội dung ghi vượt giới hạn {MaximumReturnedCharacters:N0} ký tự.");
        }

        if (!string.IsNullOrWhiteSpace(expectedSha256))
        {
            expectedSha256 = expectedSha256.Trim().ToLowerInvariant();
            if (expectedSha256.Length != 64
                || expectedSha256.Any(character => !Uri.IsHexDigit(character)))
            {
                throw new ToolExecutionInputException(
                    "Mã băm SHA-256 kỳ vọng phải gồm đúng 64 ký tự hệ mười sáu.");
            }
        }
        else
        {
            expectedSha256 = null;
        }

        byte[] contentBytes;
        try
        {
            contentBytes = StrictUtf8.GetBytes(content);
        }
        catch (EncoderFallbackException)
        {
            throw new ToolExecutionInputException(
                "Nội dung chứa ký tự Unicode không hợp lệ.");
        }

        var fullPath = ResolvePath(relativePath, allowRoot: false);
        var parentPath = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrWhiteSpace(parentPath))
        {
            throw new ToolExecutionInputException(
                "Không xác định được thư mục cha của tệp.");
        }

        EnsureNoSymlinkTraversal(parentPath);
        if (!Directory.Exists(parentPath))
        {
            throw new ToolExecutionInputException(
                "Thư mục cha chưa tồn tại. Hãy tạo thư mục trước khi ghi tệp.");
        }

        EnsureNoSymlinkTraversal(fullPath);
        if (Directory.Exists(fullPath))
        {
            throw new ToolExecutionInputException(
                "Đường dẫn này đang là một thư mục, không phải tệp.");
        }

        var existed = File.Exists(fullPath);
        if (normalizedMode == "create" && existed)
        {
            throw new ToolExecutionInputException(
                "Tệp đã tồn tại. Hãy chọn ghi đè hoặc nối thêm nếu muốn thay đổi tệp.");
        }

        if (normalizedMode is "overwrite" or "append" && !existed)
        {
            throw new ToolExecutionInputException(
                $"Tệp chưa tồn tại nên không thể dùng cách ghi {LocalizeWriteMode(normalizedMode)}.");
        }

        byte[] existingBytes = [];
        string? previousSha256 = null;
        if (existed)
        {
            var info = new FileInfo(fullPath);
            if (IsSymlink(info))
            {
                throw new ToolExecutionInputException(
                    "Không ghi vào liên kết tượng trưng hoặc điểm tái phân tích trong thư mục làm việc.");
            }

            if (info.Length > MaximumFileBytes)
            {
                throw new ToolExecutionInputException(
                    $"Tệp hiện tại vượt giới hạn {MaximumFileBytes / 1024} KB của công cụ thư mục làm việc.");
            }

            try
            {
                existingBytes = await File.ReadAllBytesAsync(fullPath, cancellationToken);
            }
            catch (UnauthorizedAccessException)
            {
                throw new ToolExecutionInputException(
                    "Không có quyền đọc tệp hiện tại trước khi ghi.");
            }
            catch (IOException)
            {
                throw new ToolExecutionInputException(
                    "Không thể đọc tệp hiện tại trước khi ghi.");
            }

            ValidateTextBytes(existingBytes);
            previousSha256 = ComputeSha256(existingBytes);
            if (expectedSha256 is not null
                && !string.Equals(previousSha256, expectedSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new ToolExecutionInputException(
                    "Tệp đã thay đổi so với mã băm SHA-256 kỳ vọng; từ chối ghi để tránh ghi đè dữ liệu mới hơn.");
            }
        }
        else if (expectedSha256 is not null)
        {
            throw new ToolExecutionInputException(
                "Mã băm SHA-256 kỳ vọng chỉ dùng khi tệp đã tồn tại.");
        }

        byte[] finalBytes;
        if (normalizedMode == "append")
        {
            finalBytes = new byte[existingBytes.Length + contentBytes.Length];
            Buffer.BlockCopy(existingBytes, 0, finalBytes, 0, existingBytes.Length);
            Buffer.BlockCopy(contentBytes, 0, finalBytes, existingBytes.Length, contentBytes.Length);
        }
        else
        {
            finalBytes = contentBytes;
        }

        if (finalBytes.Length > MaximumFileBytes)
        {
            throw new ToolExecutionInputException(
                $"Tệp sau khi ghi sẽ vượt giới hạn {MaximumFileBytes / 1024} KB.");
        }

        try
        {
            if (normalizedMode == "create")
            {
                await using var stream = new FileStream(
                    fullPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: 81920,
                    useAsync: true);
                await stream.WriteAsync(finalBytes, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }
            else
            {
                var temporaryPath = Path.Combine(
                    parentPath,
                    $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
                try
                {
                    await File.WriteAllBytesAsync(temporaryPath, finalBytes, cancellationToken);
                    File.Move(temporaryPath, fullPath, overwrite: true);
                }
                finally
                {
                    if (File.Exists(temporaryPath))
                    {
                        File.Delete(temporaryPath);
                    }
                }
            }
        }
        catch (UnauthorizedAccessException)
        {
            throw new ToolExecutionInputException(
                "Không có quyền ghi tệp này trong thư mục làm việc.");
        }
        catch (IOException)
        {
            throw new ToolExecutionInputException(
                "Không thể ghi tệp này trong thư mục làm việc.");
        }

        var writtenInfo = new FileInfo(fullPath);
        return new WorkspaceWriteResult(
            ToRelativePath(fullPath),
            normalizedMode,
            !existed,
            contentBytes.Length,
            writtenInfo.Length,
            previousSha256,
            ComputeSha256(finalBytes),
            writtenInfo.LastWriteTimeUtc);
    }

    public WorkspaceDirectoryWriteResult CreateDirectory(string relativePath)
    {
        var fullPath = ResolvePath(relativePath, allowRoot: false);
        EnsureNoSymlinkTraversal(fullPath);

        if (File.Exists(fullPath))
        {
            throw new ToolExecutionInputException(
                "Đường dẫn này đang là một tệp, không thể tạo thư mục.");
        }

        var existed = Directory.Exists(fullPath);
        try
        {
            Directory.CreateDirectory(fullPath);
        }
        catch (UnauthorizedAccessException)
        {
            throw new ToolExecutionInputException(
                "Không có quyền tạo thư mục này trong thư mục làm việc.");
        }
        catch (IOException)
        {
            throw new ToolExecutionInputException(
                "Không thể tạo thư mục này trong thư mục làm việc.");
        }

        EnsureNoSymlinkTraversal(fullPath);
        var info = new DirectoryInfo(fullPath);
        return new WorkspaceDirectoryWriteResult(
            ToRelativePath(fullPath),
            !existed,
            info.LastWriteTimeUtc);
    }


    public async Task<WorkspaceMoveResult> MoveAsync(
        string sourcePath,
        string destinationPath,
        string? expectedSha256,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        expectedSha256 = NormalizeExpectedSha256(expectedSha256);

        var sourceFullPath = ResolvePath(sourcePath, allowRoot: false);
        var destinationFullPath = ResolvePath(destinationPath, allowRoot: false);
        EnsureNoSymlinkTraversal(sourceFullPath);

        if (sourceFullPath.Equals(destinationFullPath, _pathComparison))
        {
            throw new ToolExecutionInputException(
                "Đường dẫn nguồn và đích phải khác nhau.");
        }

        if (File.Exists(destinationFullPath) || Directory.Exists(destinationFullPath))
        {
            throw new ToolExecutionInputException(
                "Đường dẫn đích đã tồn tại; công cụ di chuyển không ghi đè.");
        }

        var destinationParent = Path.GetDirectoryName(destinationFullPath);
        if (string.IsNullOrWhiteSpace(destinationParent) || !Directory.Exists(destinationParent))
        {
            throw new ToolExecutionInputException(
                "Thư mục cha của đường dẫn đích chưa tồn tại.");
        }

        EnsureNoSymlinkTraversal(destinationParent);

        if (File.Exists(sourceFullPath))
        {
            var sourceInfo = new FileInfo(sourceFullPath);
            if (IsSymlink(sourceInfo))
            {
                throw new ToolExecutionInputException(
                    "Không di chuyển liên kết tượng trưng hoặc điểm tái phân tích trong thư mục làm việc.");
            }

            var sha256 = await ComputeSha256FileAsync(sourceFullPath, cancellationToken);
            EnsureExpectedSha256Matches(expectedSha256, sha256);

            try
            {
                File.Move(sourceFullPath, destinationFullPath, overwrite: false);
            }
            catch (UnauthorizedAccessException)
            {
                throw new ToolExecutionInputException(
                    "Không có quyền di chuyển tệp này trong thư mục làm việc.");
            }
            catch (IOException)
            {
                throw new ToolExecutionInputException(
                    "Không thể di chuyển tệp này trong thư mục làm việc.");
            }

            var movedInfo = new FileInfo(destinationFullPath);
            return new WorkspaceMoveResult(
                ToRelativePath(sourceFullPath),
                ToRelativePath(destinationFullPath),
                "file",
                movedInfo.Length,
                sha256,
                movedInfo.LastWriteTimeUtc);
        }

        if (Directory.Exists(sourceFullPath))
        {
            var sourceInfo = new DirectoryInfo(sourceFullPath);
            if (IsSymlink(sourceInfo))
            {
                throw new ToolExecutionInputException(
                    "Không di chuyển liên kết tượng trưng hoặc điểm tái phân tích trong thư mục làm việc.");
            }

            if (expectedSha256 is not null)
            {
                throw new ToolExecutionInputException(
                    "Mã băm SHA-256 kỳ vọng chỉ áp dụng khi di chuyển tệp.");
            }

            var sourcePrefix = sourceFullPath.EndsWith(Path.DirectorySeparatorChar)
                ? sourceFullPath
                : sourceFullPath + Path.DirectorySeparatorChar;
            if (destinationFullPath.StartsWith(sourcePrefix, _pathComparison))
            {
                throw new ToolExecutionInputException(
                    "Không thể di chuyển thư mục vào bên trong chính nó.");
            }

            try
            {
                Directory.Move(sourceFullPath, destinationFullPath);
            }
            catch (UnauthorizedAccessException)
            {
                throw new ToolExecutionInputException(
                    "Không có quyền di chuyển thư mục này trong thư mục làm việc.");
            }
            catch (IOException)
            {
                throw new ToolExecutionInputException(
                    "Không thể di chuyển thư mục này trong thư mục làm việc.");
            }

            var movedInfo = new DirectoryInfo(destinationFullPath);
            return new WorkspaceMoveResult(
                ToRelativePath(sourceFullPath),
                ToRelativePath(destinationFullPath),
                "directory",
                null,
                null,
                movedInfo.LastWriteTimeUtc);
        }

        throw new ToolExecutionInputException(
            "Không tìm thấy tệp hoặc thư mục nguồn trong thư mục làm việc.");
    }

    public async Task<WorkspaceDeleteResult> DeleteAsync(
        string relativePath,
        string? expectedSha256,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        expectedSha256 = NormalizeExpectedSha256(expectedSha256);

        var fullPath = ResolvePath(relativePath, allowRoot: false);
        EnsureNoSymlinkTraversal(fullPath);

        if (File.Exists(fullPath))
        {
            var info = new FileInfo(fullPath);
            if (IsSymlink(info))
            {
                throw new ToolExecutionInputException(
                    "Không xóa liên kết tượng trưng hoặc điểm tái phân tích trong thư mục làm việc.");
            }

            var sizeBytes = info.Length;
            var sha256 = await ComputeSha256FileAsync(fullPath, cancellationToken);
            EnsureExpectedSha256Matches(expectedSha256, sha256);

            try
            {
                File.Delete(fullPath);
            }
            catch (UnauthorizedAccessException)
            {
                throw new ToolExecutionInputException(
                    "Không có quyền xóa tệp này trong thư mục làm việc.");
            }
            catch (IOException)
            {
                throw new ToolExecutionInputException(
                    "Không thể xóa tệp này trong thư mục làm việc.");
            }

            return new WorkspaceDeleteResult(
                ToRelativePath(fullPath),
                "file",
                sizeBytes,
                sha256,
                true);
        }

        if (Directory.Exists(fullPath))
        {
            var info = new DirectoryInfo(fullPath);
            if (IsSymlink(info))
            {
                throw new ToolExecutionInputException(
                    "Không xóa liên kết tượng trưng hoặc điểm tái phân tích trong thư mục làm việc.");
            }

            if (expectedSha256 is not null)
            {
                throw new ToolExecutionInputException(
                    "Mã băm SHA-256 kỳ vọng chỉ áp dụng khi xóa tệp.");
            }

            bool hasEntries;
            try
            {
                hasEntries = Directory.EnumerateFileSystemEntries(fullPath).Any();
            }
            catch (UnauthorizedAccessException)
            {
                throw new ToolExecutionInputException(
                    "Không có quyền kiểm tra thư mục trước khi xóa.");
            }
            catch (IOException)
            {
                throw new ToolExecutionInputException(
                    "Không thể kiểm tra thư mục trước khi xóa.");
            }

            if (hasEntries)
            {
                throw new ToolExecutionInputException(
                    "Công cụ xóa chỉ xóa thư mục rỗng; không hỗ trợ xóa đệ quy.");
            }

            try
            {
                Directory.Delete(fullPath, recursive: false);
            }
            catch (UnauthorizedAccessException)
            {
                throw new ToolExecutionInputException(
                    "Không có quyền xóa thư mục này trong thư mục làm việc.");
            }
            catch (IOException)
            {
                throw new ToolExecutionInputException(
                    "Không thể xóa thư mục này trong thư mục làm việc.");
            }

            return new WorkspaceDeleteResult(
                ToRelativePath(fullPath),
                "directory",
                null,
                null,
                true);
        }

        throw new ToolExecutionInputException(
            "Không tìm thấy tệp hoặc thư mục cần xóa trong thư mục làm việc.");
    }

    private static string LocalizeWriteMode(string mode) =>
        mode switch
        {
            "create" => "tạo mới",
            "overwrite" => "ghi đè",
            "append" => "nối thêm",
            _ => mode
        };

    private static string? NormalizeExpectedSha256(string? expectedSha256)
    {
        if (string.IsNullOrWhiteSpace(expectedSha256))
        {
            return null;
        }

        var normalized = expectedSha256.Trim().ToLowerInvariant();
        if (normalized.Length != 64 || normalized.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new ToolExecutionInputException(
                "Mã băm SHA-256 kỳ vọng phải gồm đúng 64 ký tự hệ mười sáu.");
        }

        return normalized;
    }

    private static void EnsureExpectedSha256Matches(string? expectedSha256, string actualSha256)
    {
        if (expectedSha256 is not null
            && !string.Equals(expectedSha256, actualSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new ToolExecutionInputException(
                "Tệp đã thay đổi so với mã băm SHA-256 kỳ vọng; từ chối thay đổi để tránh tác động nhầm phiên bản.");
        }
    }

    private static async Task<string> ComputeSha256FileAsync(
        string fullPath,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = new FileStream(
                fullPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 81920,
                useAsync: true);
            using var sha256 = SHA256.Create();
            var hash = await sha256.ComputeHashAsync(stream, cancellationToken);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }
        catch (UnauthorizedAccessException)
        {
            throw new ToolExecutionInputException(
                "Không có quyền đọc tệp để kiểm tra mã kiểm tra trước khi thay đổi.");
        }
        catch (IOException)
        {
            throw new ToolExecutionInputException(
                "Không thể đọc tệp để kiểm tra mã kiểm tra trước khi thay đổi.");
        }
    }

    private static void ValidateTextBytes(byte[] bytes)
    {
        string text;
        try
        {
            text = StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            throw new ToolExecutionInputException(
                "Tệp hiện tại không phải UTF-8 hợp lệ; công cụ ghi tệp chỉ sửa tệp văn bản.");
        }

        if (text.IndexOf('\0') >= 0)
        {
            throw new ToolExecutionInputException(
                "Tệp hiện tại có dấu hiệu là dữ liệu nhị phân; công cụ ghi tệp từ chối sửa.");
        }
    }

    private static string ComputeSha256(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private string ResolvePath(string? relativePath, bool allowRoot)
    {
        if (!Directory.Exists(_root))
        {
            throw new ToolExecutionInputException(
                "Thư mục làm việc chưa tồn tại. Hãy tạo thư mục đã cấu hình trước khi dùng công cụ tệp.");
        }

        var value = string.IsNullOrWhiteSpace(relativePath)
            ? "."
            : relativePath.Trim();

        if (value.IndexOf('\0') >= 0 || Path.IsPathRooted(value))
        {
            throw new ToolExecutionInputException(
                "Đường dẫn trong thư mục làm việc phải là đường dẫn tương đối.");
        }

        var segments = value
            .Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Any(segment => segment == ".."))
        {
            throw new ToolExecutionInputException(
                "Không cho phép '..' trong đường dẫn của thư mục làm việc.");
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
                "Đường dẫn trong thư mục làm việc không hợp lệ.");
        }

        var withinRoot = fullPath.Equals(_root, _pathComparison)
            || fullPath.StartsWith(_rootWithSeparator, _pathComparison);
        if (!withinRoot)
        {
            throw new ToolExecutionInputException(
                "Đường dẫn nằm ngoài thư mục làm việc đã cấp quyền.");
        }

        if (!allowRoot && fullPath.Equals(_root, _pathComparison))
        {
            throw new ToolExecutionInputException(
                "Cần chỉ định một tệp bên trong thư mục làm việc.");
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
                    "Không cho phép đi qua liên kết tượng trưng hoặc điểm tái phân tích trong thư mục làm việc.");
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
        "Liệt kê trực tiếp tệp và thư mục trong thư mục làm việc trên máy đã cấp quyền; chỉ trả đường dẫn tương đối và không đi qua liên kết tượng trưng.",
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
        "Đọc tệp văn bản UTF-8 trong thư mục làm việc trên máy đã cấp quyền, có giới hạn kích thước, giới hạn nội dung và chặn đường dẫn vượt phạm vi hoặc liên kết tượng trưng.",
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


public sealed class WorkspaceWriteTextTool(IWorkspaceFileService workspace) : IPersonalAiTool
{
    private static readonly JsonElement Schema = ToolSchema.Parse(
        """
        {
          "type": "object",
          "properties": {
            "path": { "type": "string", "minLength": 1, "maxLength": 500 },
            "content": { "type": "string", "maxLength": 200000 },
            "mode": { "type": "string", "enum": ["create", "overwrite", "append"] },
            "expectedSha256": { "type": "string", "minLength": 64, "maxLength": 64 }
          },
          "required": ["path", "content", "mode"],
          "additionalProperties": false
        }
        """);

    public ToolDefinition Definition { get; } = new(
        "workspace.write_text",
        "Tạo, ghi đè hoặc nối thêm vào tệp văn bản UTF-8 trong thư mục làm việc trên máy đã cấp quyền. Chặn đường dẫn vượt phạm vi và liên kết tượng trưng, giới hạn kích thước và hỗ trợ mã băm SHA-256 kỳ vọng để tránh ghi đè phiên bản mới hơn.",
        "1.0.0",
        [ToolPermissions.Write],
        5_000,
        Schema,
        LocalOnly: true,
        RequiresConfirmation: true);

    public async Task<JsonElement> ExecuteAsync(
        JsonElement arguments,
        CancellationToken cancellationToken = default)
    {
        var path = arguments.GetProperty("path").GetString()?.Trim() ?? string.Empty;
        var content = arguments.GetProperty("content").GetString() ?? string.Empty;
        var mode = arguments.GetProperty("mode").GetString()?.Trim() ?? string.Empty;
        var expectedSha256 = arguments.TryGetProperty("expectedSha256", out var hashElement)
            ? hashElement.GetString()
            : null;

        var result = await workspace.WriteTextAsync(
            path,
            content,
            mode,
            expectedSha256,
            cancellationToken);
        return JsonSerializer.SerializeToElement(result);
    }
}

public sealed class WorkspaceCreateDirectoryTool(IWorkspaceFileService workspace) : IPersonalAiTool
{
    private static readonly JsonElement Schema = ToolSchema.Parse(
        """
        {
          "type": "object",
          "properties": {
            "path": { "type": "string", "minLength": 1, "maxLength": 500 }
          },
          "required": ["path"],
          "additionalProperties": false
        }
        """);

    public ToolDefinition Definition { get; } = new(
        "workspace.create_directory",
        "Tạo thư mục bên trong thư mục làm việc trên máy đã cấp quyền. Chỉ nhận đường dẫn tương đối và chặn đường dẫn vượt phạm vi hoặc liên kết tượng trưng.",
        "1.0.0",
        [ToolPermissions.Write],
        3_000,
        Schema,
        LocalOnly: true,
        RequiresConfirmation: true);

    public Task<JsonElement> ExecuteAsync(
        JsonElement arguments,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = arguments.GetProperty("path").GetString()?.Trim() ?? string.Empty;
        var result = workspace.CreateDirectory(path);
        return Task.FromResult(JsonSerializer.SerializeToElement(result));
    }
}


public sealed class WorkspaceMoveTool(IWorkspaceFileService workspace) : IPersonalAiTool
{
    private static readonly JsonElement Schema = ToolSchema.Parse(
        """
        {
          "type": "object",
          "properties": {
            "sourcePath": { "type": "string", "minLength": 1, "maxLength": 500 },
            "destinationPath": { "type": "string", "minLength": 1, "maxLength": 500 },
            "expectedSha256": { "type": "string", "minLength": 64, "maxLength": 64 }
          },
          "required": ["sourcePath", "destinationPath"],
          "additionalProperties": false
        }
        """);

    public ToolDefinition Definition { get; } = new(
        "workspace.move",
        "Đổi tên hoặc di chuyển tệp/thư mục bên trong thư mục làm việc. Không ghi đè đích, không đi qua liên kết tượng trưng và có thể kiểm tra mã băm SHA-256 kỳ vọng cho tệp.",
        "1.0.0",
        [ToolPermissions.Write],
        5_000,
        Schema,
        LocalOnly: true,
        RequiresConfirmation: true);

    public async Task<JsonElement> ExecuteAsync(
        JsonElement arguments,
        CancellationToken cancellationToken = default)
    {
        var sourcePath = arguments.GetProperty("sourcePath").GetString()?.Trim() ?? string.Empty;
        var destinationPath = arguments.GetProperty("destinationPath").GetString()?.Trim() ?? string.Empty;
        var expectedSha256 = arguments.TryGetProperty("expectedSha256", out var hashElement)
            ? hashElement.GetString()
            : null;

        var result = await workspace.MoveAsync(
            sourcePath,
            destinationPath,
            expectedSha256,
            cancellationToken);
        return JsonSerializer.SerializeToElement(result);
    }
}

public sealed class WorkspaceDeleteTool(IWorkspaceFileService workspace) : IPersonalAiTool
{
    private static readonly JsonElement Schema = ToolSchema.Parse(
        """
        {
          "type": "object",
          "properties": {
            "path": { "type": "string", "minLength": 1, "maxLength": 500 },
            "expectedSha256": { "type": "string", "minLength": 64, "maxLength": 64 }
          },
          "required": ["path"],
          "additionalProperties": false
        }
        """);

    public ToolDefinition Definition { get; } = new(
        "workspace.delete",
        "Xóa tệp hoặc thư mục rỗng bên trong thư mục làm việc. Không xóa đệ quy, chặn liên kết tượng trưng hoặc đường dẫn vượt phạm vi và có thể kiểm tra mã băm SHA-256 kỳ vọng trước khi xóa tệp.",
        "1.0.0",
        [ToolPermissions.Delete],
        5_000,
        Schema,
        LocalOnly: true,
        RequiresConfirmation: true);

    public async Task<JsonElement> ExecuteAsync(
        JsonElement arguments,
        CancellationToken cancellationToken = default)
    {
        var path = arguments.GetProperty("path").GetString()?.Trim() ?? string.Empty;
        var expectedSha256 = arguments.TryGetProperty("expectedSha256", out var hashElement)
            ? hashElement.GetString()
            : null;

        var result = await workspace.DeleteAsync(
            path,
            expectedSha256,
            cancellationToken);
        return JsonSerializer.SerializeToElement(result);
    }
}
