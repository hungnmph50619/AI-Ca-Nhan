using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public interface IDisasterRecoveryService
{
    Task<DisasterRecoveryStatus> GetStatusAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DisasterRecoveryBackupVerification>> VerifyAllAsync(
        CancellationToken cancellationToken = default);

    Task<DisasterRecoveryBackupVerification> VerifyAsync(
        string backupId,
        CancellationToken cancellationToken = default);

    Task<DisasterRecoveryPlan> GetPlanAsync(
        CancellationToken cancellationToken = default);

    Task<HardeningRestoreResponse> QueueRestoreAsync(
        QueueDisasterRecoveryRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class DisasterRecoveryService(
    IHardeningBackupService backups,
    IWorkspaceContextAccessor workspace,
    IAuditRecorder audit) : IDisasterRecoveryService
{
    private const string ManifestName = "personalai-backup.json";

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true
        };

    private readonly string _backupDirectory = Path.Combine(
        HardeningPaths.ResolveRoot(),
        "Backups");

    public async Task<DisasterRecoveryStatus> GetStatusAsync(
        CancellationToken cancellationToken = default)
    {
        var reports = await VerifyAllAsync(cancellationToken);
        return new(
            PersonalAiRelease.Version,
            workspace.CurrentWorkspaceId,
            reports.Count,
            reports.Count(x => x.Restorable),
            reports.Count(x => !x.Restorable),
            ArchiveIntegrityVerificationEnabled: true,
            PathTraversalProtectionEnabled: true,
            RestoreSizeLimitsEnforced: true,
            PreRestoreBackupEnabled: true,
            StartupRestoreEnabled: true,
            RollbackOnRestoreFailureEnabled: true,
            ExplicitConfirmationRequired: true,
            backups.HasPendingRestore);
    }

    public async Task<IReadOnlyList<DisasterRecoveryBackupVerification>> VerifyAllAsync(
        CancellationToken cancellationToken = default)
    {
        var known = backups.GetBackups();
        var result = new List<DisasterRecoveryBackupVerification>();

        foreach (var backup in known
            .OrderByDescending(x => x.CreatedAt))
        {
            cancellationToken.ThrowIfCancellationRequested();
            result.Add(await VerifyAsync(
                backup.BackupId,
                cancellationToken));
        }

        return result;
    }

    public async Task<DisasterRecoveryBackupVerification> VerifyAsync(
        string backupId,
        CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeBackupId(backupId);
        var path = Path.Combine(_backupDirectory, normalized);

        if (!File.Exists(path))
        {
            throw new DisasterRecoveryValidationException(
                "Không tìm thấy backup đã chọn.");
        }

        string archiveSha256;
        await using (var hashStream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            var hash = await SHA256.HashDataAsync(
                hashStream,
                cancellationToken);
            archiveSha256 = Convert.ToHexString(hash)
                .ToLowerInvariant();
        }

        try
        {
            using var archive = ZipFile.OpenRead(path);
            var manifestEntry = archive.GetEntry(ManifestName)
                ?? throw new InvalidDataException(
                    "Backup không có manifest.");

            BackupManifest manifest;
            await using (var manifestStream = manifestEntry.Open())
            {
                manifest = await JsonSerializer.DeserializeAsync<BackupManifest>(
                    manifestStream,
                    JsonOptions,
                    cancellationToken)
                    ?? throw new InvalidDataException(
                        "Manifest backup không hợp lệ.");
            }

            if (manifest.SchemaVersion != 1)
            {
                throw new InvalidDataException(
                    "Schema backup chưa được hỗ trợ.");
            }

            var seen = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            var archiveFiles = 0;
            long archiveBytes = 0;

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
                    continue;

                archiveFiles++;
                archiveBytes += entry.Length;

                if (archiveFiles > HardeningLimits.MaximumBackupFiles)
                {
                    throw new InvalidDataException(
                        "Backup vượt giới hạn số lượng tệp.");
                }

                if (archiveBytes > HardeningLimits.MaximumBackupSourceBytes)
                {
                    throw new InvalidDataException(
                        "Backup vượt giới hạn dung lượng restore.");
                }

                var normalizedEntry = NormalizeArchiveEntry(entry.FullName);
                if (!seen.Add(normalizedEntry))
                {
                    throw new InvalidDataException(
                        "Backup chứa đường dẫn trùng lặp.");
                }

                await using var input = entry.Open();
                var buffer = new byte[64 * 1024];
                while (await input.ReadAsync(
                    buffer,
                    cancellationToken) > 0)
                {
                }
            }

            if (archiveFiles != manifest.FileCount)
            {
                throw new InvalidDataException(
                    "Số lượng tệp trong archive không khớp manifest.");
            }

            if (archiveBytes != manifest.SourceBytes)
            {
                throw new InvalidDataException(
                    "Dung lượng dữ liệu trong archive không khớp manifest.");
            }

            return new(
                normalized,
                DisasterRecoveryVerificationStatuses.Verified,
                Restorable: true,
                manifest.PersonalAiVersion,
                manifest.CreatedAt,
                manifest.FileCount,
                archiveFiles,
                manifest.SourceBytes,
                archiveBytes,
                new FileInfo(path).Length,
                archiveSha256,
                Error: null);
        }
        catch (Exception exception) when (
            exception is InvalidDataException
                or IOException
                or JsonException
                or UnauthorizedAccessException)
        {
            return new(
                normalized,
                DisasterRecoveryVerificationStatuses.Invalid,
                Restorable: false,
                PersonalAiVersion: null,
                CreatedAt: null,
                ManifestFileCount: 0,
                ArchiveFileCount: 0,
                ManifestSourceBytes: 0,
                ArchiveUncompressedBytes: 0,
                new FileInfo(path).Length,
                archiveSha256,
                exception.Message);
        }
    }

    public async Task<DisasterRecoveryPlan> GetPlanAsync(
        CancellationToken cancellationToken = default)
    {
        var reports = await VerifyAllAsync(cancellationToken);
        var recommended = reports
            .Where(x => x.Restorable)
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefault();

        var available = recommended is not null;
        return new(
            PersonalAiRelease.Version,
            workspace.CurrentWorkspaceId,
            available,
            recommended?.BackupId,
            recommended?.CreatedAt,
            RecommendedBackupVerified: available,
            backups.HasPendingRestore,
            PreRestoreBackupEnabled: true,
            StartupRestoreEnabled: true,
            RollbackOnRestoreFailureEnabled: true,
            ExplicitConfirmationRequired: true,
            available
                ? "Đã có backup được xác minh và có thể dùng cho khôi phục thảm họa."
                : "Chưa có backup nào vượt qua kiểm tra integrity.");
    }

    public async Task<HardeningRestoreResponse> QueueRestoreAsync(
        QueueDisasterRecoveryRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!request.ConfirmRestore)
        {
            throw new DisasterRecoveryValidationException(
                "Cần ConfirmRestore=true trước khi xếp hàng khôi phục thảm họa.");
        }

        var backupId = NormalizeBackupId(request.BackupId);
        var verification = await VerifyAsync(
            backupId,
            cancellationToken);

        if (!verification.Restorable)
        {
            audit.Record(
                AuditAgents.User,
                "disaster-recovery.restore",
                $"backup:{backupId}",
                $"verification-failed:{verification.Error}",
                AuditResults.Denied,
                workspaceId: workspace.CurrentWorkspaceId,
                level: SystemLogLevels.Security,
                source: "disaster-recovery");

            throw new DisasterRecoveryValidationException(
                "Backup không vượt qua integrity verification; từ chối restore.");
        }

        var result = await backups.QueueRestoreAsync(
            backupId,
            cancellationToken);

        audit.Record(
            AuditAgents.User,
            "disaster-recovery.restore",
            $"backup:{backupId}",
            $"verified-sha256:{verification.ArchiveSha256}",
            AuditResults.Prepared,
            workspaceId: workspace.CurrentWorkspaceId,
            level: SystemLogLevels.Warning,
            source: "disaster-recovery");

        return result;
    }

    private static string NormalizeBackupId(string? value)
    {
        var normalized = (value ?? string.Empty).Trim();
        if (normalized.Length is < 1 or > 240)
        {
            throw new DisasterRecoveryValidationException(
                "BackupId không hợp lệ.");
        }

        var fileName = Path.GetFileName(normalized);
        if (!string.Equals(
            fileName,
            normalized,
            StringComparison.Ordinal)
            || !normalized.EndsWith(
                ".zip",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new DisasterRecoveryValidationException(
                "BackupId không hợp lệ.");
        }

        return normalized;
    }

    private static string NormalizeArchiveEntry(string value)
    {
        var replaced = value.Replace(
            '/',
            Path.DirectorySeparatorChar);
        var root = Path.GetFullPath(
            Path.Combine(Path.GetTempPath(), "personalai-dr-root"));
        var full = Path.GetFullPath(
            Path.Combine(root, replaced));
        var prefix = root.TrimEnd(Path.DirectorySeparatorChar)
            + Path.DirectorySeparatorChar;

        if (!full.StartsWith(prefix, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "Backup chứa đường dẫn không an toàn.");
        }

        return Path.GetRelativePath(root, full)
            .Replace(
                Path.DirectorySeparatorChar,
                '/');
    }

    private sealed record BackupManifest(
        int SchemaVersion,
        string PersonalAiVersion,
        DateTimeOffset CreatedAt,
        string Reason,
        int FileCount,
        long SourceBytes);
}
