using System.Collections.Concurrent;
using System.IO.Compression;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public static class HardeningLimits
{
    public const int MaximumApiRequestsPerMinute = 600;
    public const int MaximumConcurrentApiRequests = 16;
    public const long MaximumApiRequestBytes = 12L * 1024 * 1024;
    public const int MaximumBackupCount = 5;
    public const int MaximumBackupFiles = 20_000;
    public const long MaximumBackupSourceBytes = 512L * 1024 * 1024;
    public const int HeartbeatSeconds = 30;
}

public sealed class HardeningBusyException : Exception
{
    public HardeningBusyException(string message) : base(message) { }

    public HardeningBusyException(string message, Exception innerException)
        : base(message, innerException) { }
}
public sealed class HardeningValidationException(string message) : Exception(message);

public interface IHardeningRuntimeState
{
    HardeningRuntimeStatus GetStatus();
    void MarkStarted();
    void Heartbeat();
    void MarkCleanShutdown();
    void RecordRestore(string status);
}

public sealed class HardeningRuntimeState : IHardeningRuntimeState
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly object _gate = new();
    private readonly string _statePath;
    private readonly string _restoreResultPath;
    private RuntimeStateFile? _previous;
    private DateTimeOffset _currentStartedAt = DateTimeOffset.UtcNow;
    private DateTimeOffset _lastHeartbeatAt = DateTimeOffset.UtcNow;
    private string? _lastRestoreStatus;
    private DateTimeOffset? _lastRestoreAt;
    private bool _running;

    public HardeningRuntimeState()
    {
        var hardeningDirectory = Path.Combine(
            HardeningPaths.ResolveRoot(),
            "Hardening");
        Directory.CreateDirectory(hardeningDirectory);
        _statePath = Path.Combine(hardeningDirectory, "runtime-state.json");
        _restoreResultPath = Path.Combine(hardeningDirectory, "last-restore.json");

        _previous = ReadJson<RuntimeStateFile>(_statePath);
        var restore = ReadJson<RestoreResultFile>(_restoreResultPath);
        _lastRestoreStatus = restore?.Status;
        _lastRestoreAt = restore?.At;
    }

    public HardeningRuntimeStatus GetStatus()
    {
        lock (_gate)
        {
            return new HardeningRuntimeStatus(
                _running,
                _previous is { CleanShutdown: false },
                _previous?.StartedAt,
                _previous?.HeartbeatAt,
                _currentStartedAt,
                _lastHeartbeatAt,
                _lastRestoreStatus,
                _lastRestoreAt);
        }
    }

    public void MarkStarted()
    {
        lock (_gate)
        {
            var now = DateTimeOffset.UtcNow;
            _currentStartedAt = now;
            _lastHeartbeatAt = now;
            _running = true;
            Persist(new RuntimeStateFile(
                Environment.ProcessId,
                _currentStartedAt,
                _lastHeartbeatAt,
                CleanShutdown: false));
        }
    }

    public void Heartbeat()
    {
        lock (_gate)
        {
            if (!_running)
            {
                return;
            }

            _lastHeartbeatAt = DateTimeOffset.UtcNow;
            Persist(new RuntimeStateFile(
                Environment.ProcessId,
                _currentStartedAt,
                _lastHeartbeatAt,
                CleanShutdown: false));
        }
    }

    public void MarkCleanShutdown()
    {
        lock (_gate)
        {
            if (!_running)
            {
                return;
            }

            _lastHeartbeatAt = DateTimeOffset.UtcNow;
            Persist(new RuntimeStateFile(
                Environment.ProcessId,
                _currentStartedAt,
                _lastHeartbeatAt,
                CleanShutdown: true));
            _running = false;
        }
    }

    public void RecordRestore(string status)
    {
        lock (_gate)
        {
            _lastRestoreStatus = status;
            _lastRestoreAt = DateTimeOffset.UtcNow;
            WriteJson(
                _restoreResultPath,
                new RestoreResultFile(
                    _lastRestoreStatus,
                    _lastRestoreAt.Value));
        }
    }

    private void Persist(RuntimeStateFile state) =>
        WriteJson(_statePath, state);

    private static T? ReadJson<T>(string path)
    {
        if (!File.Exists(path))
        {
            return default;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(
                File.ReadAllText(path),
                JsonOptions);
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or JsonException)
        {
            return default;
        }
    }

    private static void WriteJson<T>(string path, T value)
    {
        var temporary = path + ".tmp";
        File.WriteAllText(
            temporary,
            JsonSerializer.Serialize(value, JsonOptions));
        File.Move(temporary, path, true);
    }

    private sealed record RuntimeStateFile(
        int ProcessId,
        DateTimeOffset StartedAt,
        DateTimeOffset HeartbeatAt,
        bool CleanShutdown);

    private sealed record RestoreResultFile(
        string Status,
        DateTimeOffset At);
}

public interface IHardeningPermissionAuditService
{
    HardeningPermissionAudit Audit();
}

public sealed class HardeningPermissionAuditService(
    IToolRegistry registry,
    IToolPolicy policy) : IHardeningPermissionAuditService
{
    public HardeningPermissionAudit Audit()
    {
        var definitions = registry.GetAll();
        var findings = new List<HardeningPermissionFinding>();
        var violations = 0;

        foreach (var definition in definitions)
        {
            var riskyPermissions = definition.RequiredPermissions
                .Where(ToolPermissions.RequiresExplicitConfirmation)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.Ordinal)
                .ToArray();

            if (riskyPermissions.Length == 0
                && !definition.RequiresConfirmation)
            {
                continue;
            }

            var decision = policy.Evaluate(
                definition,
                definition.RequiredPermissions,
                confirmed: false);
            var blocked = !decision.Allowed;
            if (!blocked)
            {
                violations++;
            }

            findings.Add(new HardeningPermissionFinding(
                definition.Name,
                riskyPermissions,
                blocked,
                blocked
                    ? "Policy chặn thực thi khi chưa có xác nhận của người dùng."
                    : "CẢNH BÁO: công cụ rủi ro vẫn có thể chạy khi chưa xác nhận."));
        }

        return new HardeningPermissionAudit(
            violations == 0
                ? HardeningStatuses.Healthy
                : HardeningStatuses.Failed,
            definitions.Count,
            findings.Count,
            violations,
            DateTimeOffset.UtcNow,
            findings);
    }
}

public interface IHardeningBackupService
{
    IReadOnlyList<HardeningBackupInfo> GetBackups();
    bool HasPendingRestore { get; }

    Task<HardeningBackupInfo> CreateAsync(
        CancellationToken cancellationToken = default);

    Task<HardeningRestoreResponse> QueueRestoreAsync(
        string backupId,
        CancellationToken cancellationToken = default);

    Task<string?> ApplyPendingRestoreAsync(
        CancellationToken cancellationToken = default);
}

public sealed class HardeningBackupService : IHardeningBackupService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private const string ManifestName = "personalai-backup.json";
    private readonly string _root;
    private readonly string _backupDirectory;
    private readonly string _hardeningDirectory;
    private readonly string _maintenanceLockPath;
    private readonly string _pendingRestorePath;
    private readonly string _restoreRequestPath;
    private readonly ILogger<HardeningBackupService> _logger;

    public HardeningBackupService(
        ILogger<HardeningBackupService> logger)
    {
        _logger = logger;
        _root = HardeningPaths.ResolveRoot();
        _backupDirectory = Path.Combine(_root, "Backups");
        _hardeningDirectory = Path.Combine(_root, "Hardening");
        _maintenanceLockPath = Path.Combine(
            _hardeningDirectory,
            "maintenance.lock");
        _pendingRestorePath = Path.Combine(
            _hardeningDirectory,
            "pending-restore.zip");
        _restoreRequestPath = Path.Combine(
            _hardeningDirectory,
            "restore-request.json");

        Directory.CreateDirectory(_root);
        Directory.CreateDirectory(_backupDirectory);
        Directory.CreateDirectory(_hardeningDirectory);
    }

    public bool HasPendingRestore =>
        File.Exists(_pendingRestorePath)
        && File.Exists(_restoreRequestPath);

    public IReadOnlyList<HardeningBackupInfo> GetBackups()
    {
        if (!Directory.Exists(_backupDirectory))
        {
            return [];
        }

        var items = new List<HardeningBackupInfo>();
        foreach (var path in Directory
            .EnumerateFiles(_backupDirectory, "*.zip", SearchOption.TopDirectoryOnly)
            .OrderByDescending(File.GetLastWriteTimeUtc))
        {
            try
            {
                var info = ReadBackupInfo(path);
                if (info is not null)
                {
                    items.Add(info);
                }
            }
            catch (Exception exception) when (
                exception is IOException
                    or InvalidDataException
                    or JsonException)
            {
                _logger.LogWarning(
                    exception,
                    "Ignoring invalid backup archive {BackupPath}.",
                    path);
            }
        }

        return items;
    }

    public async Task<HardeningBackupInfo> CreateAsync(
        CancellationToken cancellationToken = default)
    {
        await using var maintenance = AcquireMaintenanceLease();
        return await CreateCoreAsync(
            "manual",
            cancellationToken);
    }

    public async Task<HardeningRestoreResponse> QueueRestoreAsync(
        string backupId,
        CancellationToken cancellationToken = default)
    {
        var normalized = Path.GetFileName(backupId?.Trim() ?? string.Empty);
        if (string.IsNullOrWhiteSpace(normalized)
            || !string.Equals(normalized, backupId?.Trim(), StringComparison.Ordinal))
        {
            throw new HardeningValidationException(
                "Mã backup không hợp lệ.");
        }

        var source = Path.Combine(_backupDirectory, normalized);
        if (!File.Exists(source))
        {
            throw new HardeningValidationException(
                "Không tìm thấy backup đã chọn.");
        }

        _ = ReadBackupInfo(source)
            ?? throw new HardeningValidationException(
                "Backup không có manifest hợp lệ.");

        await using var maintenance = AcquireMaintenanceLease();

        await CopyFileAsync(
            source,
            _pendingRestorePath,
            cancellationToken);

        WriteJson(
            _restoreRequestPath,
            new RestoreRequestFile(
                normalized,
                DateTimeOffset.UtcNow,
                PersonalAiRelease.Version));

        return new HardeningRestoreResponse(
            normalized,
            HardeningStatuses.PendingRestart,
            RestartRequired: true,
            "Restore đã được xếp hàng. Dữ liệu chỉ được thay thế ở lần khởi động tiếp theo, trước khi các background service chạy.");
    }

    public async Task<string?> ApplyPendingRestoreAsync(
        CancellationToken cancellationToken = default)
    {
        if (!HasPendingRestore)
        {
            return null;
        }

        await using var maintenance = AcquireMaintenanceLease();
        var request = ReadJson<RestoreRequestFile>(_restoreRequestPath)
            ?? throw new HardeningValidationException(
                "Restore request bị hỏng.");

        var staging = Path.Combine(
            Path.GetTempPath(),
            "personalai-restore-" + Guid.NewGuid().ToString("N"));
        var rollback = Path.Combine(
            _hardeningDirectory,
            "rollback-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        Directory.CreateDirectory(rollback);

        var movedCurrentData = false;
        try
        {
            await ExtractBackupAsync(
                _pendingRestorePath,
                staging,
                cancellationToken);

            _ = await CreateCoreAsync(
                "pre-restore",
                cancellationToken);

            MoveCurrentDataToRollback(rollback);
            movedCurrentData = true;

            CopyRestoredData(staging, _root);

            Directory.Delete(rollback, recursive: true);
            File.Delete(_pendingRestorePath);
            File.Delete(_restoreRequestPath);

            _logger.LogWarning(
                "Applied pending restore from backup {BackupId}.",
                request.BackupId);
            return "restored:" + request.BackupId;
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Pending restore from {BackupId} failed.",
                request.BackupId);

            if (movedCurrentData)
            {
                try
                {
                    RemoveCurrentData();
                    RestoreRollbackData(rollback);
                }
                catch (Exception rollbackException)
                {
                    _logger.LogCritical(
                        rollbackException,
                        "Rollback after failed restore also failed.");
                }
            }

            var failedPath = Path.Combine(
                _hardeningDirectory,
                "restore-failed.json");
            WriteJson(
                failedPath,
                new
                {
                    backupId = request.BackupId,
                    failedAt = DateTimeOffset.UtcNow,
                    error = exception.Message
                });

            File.Delete(_restoreRequestPath);
            if (File.Exists(_pendingRestorePath))
            {
                var failedZip = Path.Combine(
                    _hardeningDirectory,
                    "failed-restore.zip");
                File.Move(
                    _pendingRestorePath,
                    failedZip,
                    true);
            }

            return "failed:" + request.BackupId;
        }
        finally
        {
            TryDeleteDirectory(staging);
            TryDeleteDirectory(rollback);
        }
    }

    private async Task<HardeningBackupInfo> CreateCoreAsync(
        string reason,
        CancellationToken cancellationToken)
    {
        var files = EnumerateDataFiles().ToArray();
        if (files.Length > HardeningLimits.MaximumBackupFiles)
        {
            throw new HardeningValidationException(
                $"Backup vượt quá giới hạn {HardeningLimits.MaximumBackupFiles:N0} tệp.");
        }

        var totalBytes = files.Sum(file => new FileInfo(file).Length);
        if (totalBytes > HardeningLimits.MaximumBackupSourceBytes)
        {
            throw new HardeningValidationException(
                $"Backup vượt quá giới hạn {HardeningLimits.MaximumBackupSourceBytes / (1024 * 1024):N0} MB dữ liệu nguồn.");
        }

        var staging = Path.Combine(
            Path.GetTempPath(),
            "personalai-backup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);

        try
        {
            foreach (var source in files)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var relative = Path.GetRelativePath(_root, source);
                var destination = Path.Combine(staging, relative);
                Directory.CreateDirectory(
                    Path.GetDirectoryName(destination)
                    ?? staging);

                if (source.EndsWith(
                    ".db",
                    StringComparison.OrdinalIgnoreCase))
                {
                    SnapshotSqliteDatabase(
                        source,
                        destination);
                }
                else
                {
                    await CopyFileAsync(
                        source,
                        destination,
                        cancellationToken);
                }
            }

            var now = DateTimeOffset.UtcNow;
            var backupId =
                $"personalai-{now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.zip";
            var temporaryArchive = Path.Combine(
                _backupDirectory,
                backupId + ".tmp");
            var archivePath = Path.Combine(
                _backupDirectory,
                backupId);

            var manifest = new BackupManifest(
                SchemaVersion: 1,
                PersonalAiVersion: PersonalAiRelease.Version,
                CreatedAt: now,
                Reason: reason,
                FileCount: files.Length,
                SourceBytes: totalBytes);

            await CreateArchiveAsync(
                staging,
                temporaryArchive,
                manifest,
                cancellationToken);
            File.Move(
                temporaryArchive,
                archivePath,
                true);

            PruneBackups();

            var fileInfo = new FileInfo(archivePath);
            return new HardeningBackupInfo(
                backupId,
                now,
                fileInfo.Length,
                files.Length,
                PersonalAiRelease.Version);
        }
        finally
        {
            TryDeleteDirectory(staging);
        }
    }

    private IEnumerable<string> EnumerateDataFiles()
    {
        if (!Directory.Exists(_root))
        {
            yield break;
        }

        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = false,
            AttributesToSkip = FileAttributes.ReparsePoint
        };

        foreach (var path in Directory.EnumerateFiles(
            _root,
            "*",
            options))
        {
            var relative = Path.GetRelativePath(_root, path);
            var firstSegment = relative
                .Split(
                    [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                    StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault();

            if (firstSegment is null
                || firstSegment.Equals("Backups", StringComparison.OrdinalIgnoreCase)
                || firstSegment.Equals("Hardening", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".db-wal", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".db-shm", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            yield return path;
        }
    }

    private static void SnapshotSqliteDatabase(
        string sourcePath,
        string destinationPath)
    {
        var sourceBuilder = new SqliteConnectionStringBuilder
        {
            DataSource = sourcePath,
            Mode = SqliteOpenMode.ReadOnly
        };
        var destinationBuilder = new SqliteConnectionStringBuilder
        {
            DataSource = destinationPath,
            Mode = SqliteOpenMode.ReadWriteCreate
        };

        using var source = new SqliteConnection(
            sourceBuilder.ToString());
        using var destination = new SqliteConnection(
            destinationBuilder.ToString());
        source.Open();
        destination.Open();
        source.BackupDatabase(destination);
    }

    private static async Task CreateArchiveAsync(
        string staging,
        string archivePath,
        BackupManifest manifest,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            archivePath,
            FileMode.Create,
            FileAccess.ReadWrite,
            FileShare.None,
            64 * 1024,
            FileOptions.Asynchronous);

        using var archive = new ZipArchive(
            stream,
            ZipArchiveMode.Create,
            leaveOpen: true);

        var manifestEntry = archive.CreateEntry(
            ManifestName,
            CompressionLevel.Fastest);
        await using (var manifestStream = manifestEntry.Open())
        {
            await JsonSerializer.SerializeAsync(
                manifestStream,
                manifest,
                JsonOptions,
                cancellationToken);
        }

        foreach (var source in Directory.EnumerateFiles(
            staging,
            "*",
            SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var relative = Path.GetRelativePath(
                    staging,
                    source)
                .Replace(
                    Path.DirectorySeparatorChar,
                    '/');
            var entry = archive.CreateEntry(
                relative,
                CompressionLevel.Fastest);

            await using var input = new FileStream(
                source,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            await using var output = entry.Open();
            await input.CopyToAsync(
                output,
                cancellationToken);
        }
    }

    private async Task ExtractBackupAsync(
        string archivePath,
        string destination,
        CancellationToken cancellationToken)
    {
        using var archive = ZipFile.OpenRead(archivePath);
        var manifestEntry = archive.GetEntry(ManifestName)
            ?? throw new HardeningValidationException(
                "Backup không có manifest.");
        await using (var stream = manifestEntry.Open())
        {
            var manifest = await JsonSerializer.DeserializeAsync<BackupManifest>(
                stream,
                JsonOptions,
                cancellationToken)
                ?? throw new HardeningValidationException(
                    "Manifest backup không hợp lệ.");

            if (manifest.SchemaVersion != 1)
            {
                throw new HardeningValidationException(
                    "Schema backup chưa được hỗ trợ.");
            }
        }

        var fileCount = 0;
        long extractedBytes = 0;
        var destinationRoot = Path.GetFullPath(destination)
            .TrimEnd(Path.DirectorySeparatorChar)
            + Path.DirectorySeparatorChar;

        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (entry.FullName.Equals(
                ManifestName,
                StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (string.IsNullOrEmpty(entry.Name))
            {
                continue;
            }

            fileCount++;
            extractedBytes += entry.Length;
            if (fileCount > HardeningLimits.MaximumBackupFiles
                || extractedBytes > HardeningLimits.MaximumBackupSourceBytes)
            {
                throw new HardeningValidationException(
                    "Backup vượt quá giới hạn restore.");
            }

            var normalizedName = entry.FullName
                .Replace('/', Path.DirectorySeparatorChar);
            var target = Path.GetFullPath(
                Path.Combine(
                    destination,
                    normalizedName));

            if (!target.StartsWith(
                destinationRoot,
                StringComparison.Ordinal))
            {
                throw new HardeningValidationException(
                    "Backup chứa đường dẫn không an toàn.");
            }

            Directory.CreateDirectory(
                Path.GetDirectoryName(target)
                ?? destination);

            await using var input = entry.Open();
            await using var output = new FileStream(
                target,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                64 * 1024,
                FileOptions.Asynchronous);
            await input.CopyToAsync(
                output,
                cancellationToken);
        }
    }

    private HardeningBackupInfo? ReadBackupInfo(string archivePath)
    {
        using var archive = ZipFile.OpenRead(archivePath);
        var entry = archive.GetEntry(ManifestName);
        if (entry is null)
        {
            return null;
        }

        using var stream = entry.Open();
        var manifest = JsonSerializer.Deserialize<BackupManifest>(
            stream,
            JsonOptions);
        if (manifest is null
            || manifest.SchemaVersion != 1)
        {
            return null;
        }

        var info = new FileInfo(archivePath);
        return new HardeningBackupInfo(
            info.Name,
            manifest.CreatedAt,
            info.Length,
            manifest.FileCount,
            manifest.PersonalAiVersion);
    }

    private void MoveCurrentDataToRollback(
        string rollbackDirectory)
    {
        foreach (var entry in Directory.EnumerateFileSystemEntries(
            _root,
            "*",
            SearchOption.TopDirectoryOnly))
        {
            var name = Path.GetFileName(entry);
            if (name.Equals("Backups", StringComparison.OrdinalIgnoreCase)
                || name.Equals("Hardening", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var destination = Path.Combine(
                rollbackDirectory,
                name);
            if (Directory.Exists(entry))
            {
                Directory.Move(entry, destination);
            }
            else
            {
                File.Move(entry, destination, true);
            }
        }
    }

    private void RemoveCurrentData()
    {
        foreach (var entry in Directory.EnumerateFileSystemEntries(
            _root,
            "*",
            SearchOption.TopDirectoryOnly))
        {
            var name = Path.GetFileName(entry);
            if (name.Equals("Backups", StringComparison.OrdinalIgnoreCase)
                || name.Equals("Hardening", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (Directory.Exists(entry))
            {
                Directory.Delete(entry, recursive: true);
            }
            else
            {
                File.Delete(entry);
            }
        }
    }

    private static void CopyRestoredData(
        string sourceRoot,
        string destinationRoot)
    {
        foreach (var directory in Directory.EnumerateDirectories(
            sourceRoot,
            "*",
            SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(
                sourceRoot,
                directory);
            Directory.CreateDirectory(
                Path.Combine(
                    destinationRoot,
                    relative));
        }

        foreach (var file in Directory.EnumerateFiles(
            sourceRoot,
            "*",
            SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(
                sourceRoot,
                file);
            var destination = Path.Combine(
                destinationRoot,
                relative);
            Directory.CreateDirectory(
                Path.GetDirectoryName(destination)
                ?? destinationRoot);
            File.Copy(
                file,
                destination,
                overwrite: true);
        }
    }

    private static void RestoreRollbackData(
        string rollbackDirectory)
    {
        if (!Directory.Exists(rollbackDirectory))
        {
            return;
        }

        var destinationRoot = HardeningPaths.ResolveRoot();
        foreach (var entry in Directory.EnumerateFileSystemEntries(
            rollbackDirectory,
            "*",
            SearchOption.TopDirectoryOnly))
        {
            var destination = Path.Combine(
                destinationRoot,
                Path.GetFileName(entry));
            if (Directory.Exists(entry))
            {
                Directory.Move(entry, destination);
            }
            else
            {
                File.Move(entry, destination, true);
            }
        }
    }

    private void PruneBackups()
    {
        var archives = Directory
            .EnumerateFiles(
                _backupDirectory,
                "*.zip",
                SearchOption.TopDirectoryOnly)
            .OrderByDescending(
                File.GetLastWriteTimeUtc)
            .ToArray();

        foreach (var path in archives
            .Skip(HardeningLimits.MaximumBackupCount))
        {
            try
            {
                File.Delete(path);
            }
            catch (IOException exception)
            {
                _logger.LogWarning(
                    exception,
                    "Could not prune backup {BackupPath}.",
                    path);
            }
        }
    }

    private FileStream AcquireMaintenanceLease()
    {
        try
        {
            return new FileStream(
                _maintenanceLockPath,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None,
                1,
                FileOptions.DeleteOnClose);
        }
        catch (IOException exception)
        {
            throw new HardeningBusyException(
                "Một thao tác bảo trì khác đang chạy. Hãy thử lại sau.",
                exception);
        }
    }

    private static async Task CopyFileAsync(
        string source,
        string destination,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(
            Path.GetDirectoryName(destination)
            ?? ".");

        await using var input = new FileStream(
            source,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var output = new FileStream(
            destination,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            64 * 1024,
            FileOptions.Asynchronous);
        await input.CopyToAsync(
            output,
            cancellationToken);
    }

    private static T? ReadJson<T>(string path)
    {
        if (!File.Exists(path))
        {
            return default;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(
                File.ReadAllText(path),
                JsonOptions);
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or JsonException)
        {
            return default;
        }
    }

    private static void WriteJson<T>(
        string path,
        T value)
    {
        var temporary = path + ".tmp";
        File.WriteAllText(
            temporary,
            JsonSerializer.Serialize(
                value,
                JsonOptions));
        File.Move(
            temporary,
            path,
            true);
    }

    private static void TryDeleteDirectory(
        string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(
                    path,
                    recursive: true);
            }
        }
        catch
        {
        }
    }

    private sealed record BackupManifest(
        int SchemaVersion,
        string PersonalAiVersion,
        DateTimeOffset CreatedAt,
        string Reason,
        int FileCount,
        long SourceBytes);

    private sealed record RestoreRequestFile(
        string BackupId,
        DateTimeOffset RequestedAt,
        string RequestedFromVersion);
}

public interface IHardeningStatusService
{
    HardeningStatusResponse GetStatus();
}

public sealed class HardeningStatusService(
    IHardeningRuntimeState runtime,
    IHardeningPermissionAuditService permissionAudit,
    IHardeningBackupService backups) : IHardeningStatusService
{
    public HardeningStatusResponse GetStatus()
    {
        var runtimeStatus = runtime.GetStatus();
        var audit = permissionAudit.Audit();
        var backupList = backups.GetBackups();

        var status = backups.HasPendingRestore
            ? HardeningStatuses.PendingRestart
            : audit.ViolationCount > 0
                || runtimeStatus.PreviousUncleanShutdown
                || runtimeStatus.LastRestoreStatus?.StartsWith(
                    "failed:",
                    StringComparison.OrdinalIgnoreCase) == true
                ? HardeningStatuses.Degraded
                : HardeningStatuses.Healthy;

        return new HardeningStatusResponse(
            PersonalAiRelease.Version,
            status,
            DateTimeOffset.UtcNow,
            runtimeStatus,
            audit,
            backupList,
            HardeningLimits.MaximumApiRequestsPerMinute,
            HardeningLimits.MaximumConcurrentApiRequests,
            HardeningLimits.MaximumApiRequestBytes,
            HardeningLimits.MaximumBackupCount,
            HardeningLimits.MaximumBackupSourceBytes,
            backups.HasPendingRestore,
            CrossProcessMaintenanceLock: true);
    }
}

public sealed class HardeningRuntimeHostedService(
    IHardeningRuntimeState runtime,
    IHardeningBackupService backups,
    ILogger<HardeningRuntimeHostedService> logger) : IHostedService, IDisposable
{
    private Timer? _timer;

    public async Task StartAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            var restore = await backups.ApplyPendingRestoreAsync(
                cancellationToken);
            if (!string.IsNullOrWhiteSpace(restore))
            {
                runtime.RecordRestore(restore);
                logger.LogWarning(
                    "Hardening startup restore result: {RestoreStatus}.",
                    restore);
            }
        }
        catch (Exception exception) when (
            exception is not OperationCanceledException)
        {
            logger.LogError(
                exception,
                "Pending restore could not be applied during startup.");
            runtime.RecordRestore("failed:startup-validation");
        }

        runtime.MarkStarted();
        _timer = new Timer(
            _ =>
            {
                try
                {
                    runtime.Heartbeat();
                }
                catch (Exception exception)
                {
                    logger.LogWarning(
                        exception,
                        "Hardening heartbeat could not be persisted.");
                }
            },
            null,
            TimeSpan.FromSeconds(
                HardeningLimits.HeartbeatSeconds),
            TimeSpan.FromSeconds(
                HardeningLimits.HeartbeatSeconds));
    }

    public Task StopAsync(
        CancellationToken cancellationToken)
    {
        _timer?.Change(
            Timeout.InfiniteTimeSpan,
            Timeout.InfiniteTimeSpan);
        try
        {
            runtime.MarkCleanShutdown();
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Hardening clean-shutdown marker could not be persisted.");
        }

        return Task.CompletedTask;
    }

    public void Dispose() =>
        _timer?.Dispose();
}

public sealed class HardeningApiGuardMiddleware(
    RequestDelegate next,
    ILogger<HardeningApiGuardMiddleware> logger)
{
    private static readonly SemaphoreSlim ConcurrentRequests =
        new(
            HardeningLimits.MaximumConcurrentApiRequests,
            HardeningLimits.MaximumConcurrentApiRequests);

    private static readonly ConcurrentDictionary<string, RequestWindow>
        Windows = new(StringComparer.Ordinal);

    public async Task InvokeAsync(
        HttpContext context)
    {
        if (!context.Request.Path.StartsWithSegments("/api"))
        {
            await next(context);
            return;
        }

        if (context.Request.ContentLength
            is long contentLength
            && contentLength > HardeningLimits.MaximumApiRequestBytes)
        {
            context.Response.StatusCode =
                StatusCodes.Status413PayloadTooLarge;
            await context.Response.WriteAsJsonAsync(
                new ApiError(
                    "Yêu cầu vượt quá giới hạn kích thước của hệ thống."));
            return;
        }

        var rate = CheckRate(context);
        context.Response.Headers["X-PersonalAI-RateLimit-Limit"] =
            HardeningLimits.MaximumApiRequestsPerMinute.ToString();
        context.Response.Headers["X-PersonalAI-RateLimit-Remaining"] =
            Math.Max(0, rate.Remaining).ToString();

        if (!rate.Allowed)
        {
            context.Response.StatusCode =
                StatusCodes.Status429TooManyRequests;
            context.Response.Headers["Retry-After"] =
                Math.Max(1, rate.RetryAfterSeconds).ToString();
            await context.Response.WriteAsJsonAsync(
                new ApiError(
                    "Có quá nhiều yêu cầu trong thời gian ngắn. Hãy thử lại sau."));
            return;
        }

        if (!await ConcurrentRequests.WaitAsync(
            0,
            context.RequestAborted))
        {
            logger.LogWarning(
                "API concurrency guard rejected {Path}.",
                context.Request.Path.Value);
            context.Response.StatusCode =
                StatusCodes.Status503ServiceUnavailable;
            context.Response.Headers["Retry-After"] = "1";
            await context.Response.WriteAsJsonAsync(
                new ApiError(
                    "Hệ thống đang xử lý quá nhiều yêu cầu đồng thời. Hãy thử lại."));
            return;
        }

        try
        {
            await next(context);
        }
        finally
        {
            ConcurrentRequests.Release();
        }
    }

    private static RateDecision CheckRate(
        HttpContext context)
    {
        var now = DateTimeOffset.UtcNow;
        var remote =
            context.Connection.RemoteIpAddress?.ToString()
            ?? "local";
        var workspace = context.Request.Headers[
                WorkspaceEndpoints.WorkspaceHeaderName]
            .FirstOrDefault()?
            .Trim()
            .ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(workspace)
            || workspace.Length > 80)
        {
            workspace = PersonalWorkspaceIds.Personal;
        }

        var key = remote + "|" + workspace;
        var window = Windows.GetOrAdd(
            key,
            _ => new RequestWindow(now));

        lock (window.Gate)
        {
            if (now - window.StartedAt
                >= TimeSpan.FromMinutes(1))
            {
                window.StartedAt = now;
                window.Count = 0;
            }

            if (window.Count
                >= HardeningLimits.MaximumApiRequestsPerMinute)
            {
                var retry = (int)Math.Ceiling(
                    (window.StartedAt
                        .AddMinutes(1)
                        - now)
                    .TotalSeconds);
                return new RateDecision(
                    false,
                    0,
                    retry);
            }

            window.Count++;
            var remaining =
                HardeningLimits.MaximumApiRequestsPerMinute
                - window.Count;

            if (Windows.Count > 4_096)
            {
                CleanupWindows(now);
            }

            return new RateDecision(
                true,
                remaining,
                0);
        }
    }

    private static void CleanupWindows(
        DateTimeOffset now)
    {
        foreach (var pair in Windows)
        {
            if (now - pair.Value.StartedAt
                > TimeSpan.FromMinutes(3))
            {
                Windows.TryRemove(
                    pair.Key,
                    out _);
            }
        }
    }

    private sealed class RequestWindow(
        DateTimeOffset startedAt)
    {
        public object Gate { get; } = new();
        public DateTimeOffset StartedAt { get; set; } = startedAt;
        public int Count { get; set; }
    }

    private sealed record RateDecision(
        bool Allowed,
        int Remaining,
        int RetryAfterSeconds);
}

internal static class HardeningPaths
{
    public static string ResolveRoot()
    {
        var localData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localData))
        {
            localData = Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.UserProfile),
                ".personalai");
        }

        return Path.Combine(
            localData,
            "PersonalAI");
    }
}
