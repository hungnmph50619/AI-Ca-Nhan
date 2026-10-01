using System.Text.Json;
using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public interface IDevelopmentLeaseService
{
    DevelopmentLeaseStatus GetStatus();
    IReadOnlyList<DevelopmentLease> GetActive();
    DevelopmentLease Acquire(AcquireDevelopmentLeaseRequest request);
    DevelopmentLease Renew(RenewDevelopmentLeaseRequest request);
    void Release(ReleaseDevelopmentLeaseRequest request);
}

public sealed class DevelopmentLeaseService(
    IWorkspaceFileService workspaceFiles,
    IWorkspaceContextAccessor workspace,
    IConfiguration configuration,
    IAuditRecorder audit) : IDevelopmentLeaseService
{
    public const int MinimumLeaseSeconds = 30;
    public const int MaximumLeaseSeconds = 3600;

    private readonly object _gate = new();
    private readonly string _root = ResolveRoot(configuration);
    private static readonly JsonSerializerOptions Options =
        new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public DevelopmentLeaseStatus GetStatus()
    {
        lock (_gate)
        {
            var all = Load();
            var now = DateTimeOffset.UtcNow;
            var active = all.Count(x => x.ExpiresAt > now);
            var expired = all.Count - active;

            return new(
                PersonalAiRelease.Version,
                workspace.CurrentWorkspaceId,
                active,
                expired,
                MinimumLeaseSeconds,
                MaximumLeaseSeconds,
                RepositoryScopeBlocksChildren: true,
                DuplicateWriterBlocked: true,
                ExpiryEnabled: true);
        }
    }

    public IReadOnlyList<DevelopmentLease> GetActive()
    {
        lock (_gate)
        {
            var now = DateTimeOffset.UtcNow;
            var all = Load();
            var active = all
                .Where(x => x.ExpiresAt > now)
                .OrderBy(x => x.ExpiresAt)
                .ToArray();

            if (active.Length != all.Count)
                Save(active.ToList());

            return active;
        }
    }

    public DevelopmentLease Acquire(AcquireDevelopmentLeaseRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var owner = NormalizeOwner(request.OwnerId);
        var type = NormalizeResourceType(request.ResourceType);
        var repository = NormalizeRepositoryPath(request.RepositoryPath);
        var branch = type is DevelopmentLeaseResourceTypes.Branch or
            DevelopmentLeaseResourceTypes.File
                ? NormalizeBranch(request.Branch)
                : null;
        var file = type == DevelopmentLeaseResourceTypes.File
            ? NormalizeFilePath(request.FilePath)
            : null;
        var seconds = NormalizeLeaseSeconds(request.LeaseSeconds);

        lock (_gate)
        {
            var now = DateTimeOffset.UtcNow;
            var all = Load()
                .Where(x => x.ExpiresAt > now)
                .ToList();

            var conflict = all.FirstOrDefault(existing =>
                !existing.OwnerId.Equals(owner, StringComparison.Ordinal) &&
                Conflicts(existing, type, repository, branch, file));

            if (conflict is not null)
            {
                throw new DevelopmentLeaseConflictException(
                    $"Resource đang bị khóa bởi owner '{conflict.OwnerId}' đến {conflict.ExpiresAt:O}.");
            }

            var sameOwnerExisting = all.FirstOrDefault(existing =>
                existing.OwnerId.Equals(owner, StringComparison.Ordinal) &&
                SameResource(existing, type, repository, branch, file));

            if (sameOwnerExisting is not null)
            {
                var renewed = sameOwnerExisting with
                {
                    ExpiresAt = now.AddSeconds(seconds),
                    UpdatedAt = now
                };
                var index = all.FindIndex(x => x.Id == renewed.Id);
                all[index] = renewed;
                Save(all);

                audit.Record(
                    AuditAgents.System,
                    "development.lease.renew",
                    $"lease:{renewed.Id:D}",
                    $"owner:{owner};type:{type};repo:{repository}",
                    AuditResults.Succeeded);

                return renewed;
            }

            var lease = new DevelopmentLease(
                Guid.NewGuid(),
                workspace.CurrentWorkspaceId,
                owner,
                type,
                repository,
                branch,
                file,
                now,
                now.AddSeconds(seconds),
                now);

            all.Add(lease);
            Save(all);

            audit.Record(
                AuditAgents.System,
                "development.lease.acquire",
                $"lease:{lease.Id:D}",
                $"owner:{owner};type:{type};repo:{repository};branch:{branch};file:{file}",
                AuditResults.Succeeded);

            return lease;
        }
    }

    public DevelopmentLease Renew(RenewDevelopmentLeaseRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var owner = NormalizeOwner(request.OwnerId);
        var seconds = NormalizeLeaseSeconds(request.LeaseSeconds);

        lock (_gate)
        {
            var now = DateTimeOffset.UtcNow;
            var all = Load()
                .Where(x => x.ExpiresAt > now)
                .ToList();

            var index = all.FindIndex(x => x.Id == request.LeaseId);
            if (index < 0)
                throw new KeyNotFoundException("Không tìm thấy lease đang hoạt động.");

            var current = all[index];
            if (!current.OwnerId.Equals(owner, StringComparison.Ordinal))
                throw new DevelopmentLeaseConflictException(
                    "Chỉ owner của lease mới được renew.");

            var renewed = current with
            {
                ExpiresAt = now.AddSeconds(seconds),
                UpdatedAt = now
            };
            all[index] = renewed;
            Save(all);

            audit.Record(
                AuditAgents.System,
                "development.lease.renew",
                $"lease:{renewed.Id:D}",
                $"owner:{owner};expires:{renewed.ExpiresAt:O}",
                AuditResults.Succeeded);

            return renewed;
        }
    }

    public void Release(ReleaseDevelopmentLeaseRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var owner = NormalizeOwner(request.OwnerId);

        lock (_gate)
        {
            var now = DateTimeOffset.UtcNow;
            var all = Load()
                .Where(x => x.ExpiresAt > now)
                .ToList();

            var index = all.FindIndex(x => x.Id == request.LeaseId);
            if (index < 0)
                throw new KeyNotFoundException("Không tìm thấy lease đang hoạt động.");

            var current = all[index];
            if (!current.OwnerId.Equals(owner, StringComparison.Ordinal))
                throw new DevelopmentLeaseConflictException(
                    "Chỉ owner của lease mới được release.");

            all.RemoveAt(index);
            Save(all);

            audit.Record(
                AuditAgents.System,
                "development.lease.release",
                $"lease:{current.Id:D}",
                $"owner:{owner};type:{current.ResourceType};repo:{current.RepositoryPath}",
                AuditResults.Succeeded);
        }
    }

    private bool Conflicts(
        DevelopmentLease existing,
        string newType,
        string repository,
        string? branch,
        string? file)
    {
        if (!existing.RepositoryPath.Equals(
                repository,
                StringComparison.OrdinalIgnoreCase))
            return false;

        if (existing.ResourceType == DevelopmentLeaseResourceTypes.Repository ||
            newType == DevelopmentLeaseResourceTypes.Repository)
            return true;

        if (existing.ResourceType == DevelopmentLeaseResourceTypes.Branch &&
            newType == DevelopmentLeaseResourceTypes.Branch)
            return string.Equals(existing.Branch, branch, StringComparison.OrdinalIgnoreCase);

        if (existing.ResourceType == DevelopmentLeaseResourceTypes.Branch &&
            newType == DevelopmentLeaseResourceTypes.File)
            return string.Equals(existing.Branch, branch, StringComparison.OrdinalIgnoreCase);

        if (existing.ResourceType == DevelopmentLeaseResourceTypes.File &&
            newType == DevelopmentLeaseResourceTypes.Branch)
            return string.Equals(existing.Branch, branch, StringComparison.OrdinalIgnoreCase);

        return existing.ResourceType == DevelopmentLeaseResourceTypes.File &&
               newType == DevelopmentLeaseResourceTypes.File &&
               string.Equals(existing.Branch, branch, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(existing.FilePath, file, StringComparison.OrdinalIgnoreCase);
    }

    private static bool SameResource(
        DevelopmentLease existing,
        string type,
        string repository,
        string? branch,
        string? file) =>
        existing.ResourceType == type &&
        existing.RepositoryPath.Equals(repository, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(existing.Branch, branch, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(existing.FilePath, file, StringComparison.OrdinalIgnoreCase);

    private string NormalizeRepositoryPath(string? value)
    {
        var workspaceRoot = Path.GetFullPath(workspaceFiles.GetWorkspaceRoot());
        var requested = string.IsNullOrWhiteSpace(value) || value == "."
            ? workspaceRoot
            : Path.GetFullPath(Path.Combine(workspaceRoot, value.Trim()));

        EnsureInsideRoot(workspaceRoot, requested);

        var relative = Path.GetRelativePath(workspaceRoot, requested)
            .Replace(Path.DirectorySeparatorChar, '/');
        return relative == "." ? string.Empty : relative;
    }

    private static string NormalizeResourceType(string? value)
    {
        var type = (value ?? string.Empty).Trim().ToLowerInvariant();
        if (type is not (
            DevelopmentLeaseResourceTypes.Repository or
            DevelopmentLeaseResourceTypes.Branch or
            DevelopmentLeaseResourceTypes.File))
        {
            throw new DevelopmentLeaseValidationException(
                "ResourceType phải là repository, branch hoặc file.");
        }
        return type;
    }

    private static string NormalizeOwner(string? value)
    {
        var owner = (value ?? string.Empty).Trim();
        if (owner.Length is < 1 or > 120 ||
            owner.Any(char.IsControl))
        {
            throw new DevelopmentLeaseValidationException(
                "OwnerId phải có từ 1 đến 120 ký tự.");
        }
        return owner;
    }

    private static string NormalizeBranch(string? value)
    {
        var branch = (value ?? string.Empty).Trim();
        if (branch.Length is < 1 or > 160 ||
            branch.StartsWith('-') ||
            branch.Contains("..", StringComparison.Ordinal) ||
            branch.Contains("@{", StringComparison.Ordinal) ||
            branch.Any(c =>
                char.IsControl(c) ||
                char.IsWhiteSpace(c) ||
                c is '~' or '^' or ':' or '?' or '*' or '[' or '\\'))
        {
            throw new DevelopmentLeaseValidationException(
                "Branch không hợp lệ.");
        }
        return branch;
    }

    private static string NormalizeFilePath(string? value)
    {
        var path = (value ?? string.Empty).Trim().Replace('\\', '/');
        if (path.Length is < 1 or > 400 ||
            Path.IsPathRooted(path) ||
            path.StartsWith("../", StringComparison.Ordinal) ||
            path.Contains("/../", StringComparison.Ordinal) ||
            path.Equals("..", StringComparison.Ordinal) ||
            path.Split('/').Any(x => x.Equals(".git", StringComparison.OrdinalIgnoreCase)))
        {
            throw new DevelopmentLeaseValidationException(
                "FilePath không hợp lệ.");
        }
        return path;
    }

    private static int NormalizeLeaseSeconds(int value)
    {
        if (value < MinimumLeaseSeconds || value > MaximumLeaseSeconds)
            throw new DevelopmentLeaseValidationException(
                $"LeaseSeconds phải từ {MinimumLeaseSeconds} đến {MaximumLeaseSeconds}.");
        return value;
    }

    private static void EnsureInsideRoot(string root, string path)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        var normalizedRoot = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var normalizedPath = Path.GetFullPath(path);

        if (string.Equals(normalizedRoot, normalizedPath, comparison))
            return;

        var prefix = normalizedRoot + Path.DirectorySeparatorChar;
        if (!normalizedPath.StartsWith(prefix, comparison))
            throw new DevelopmentLeaseValidationException(
                "RepositoryPath phải nằm trong workspace.");
    }

    private List<DevelopmentLease> Load()
    {
        var path = PathForWorkspace();
        if (!File.Exists(path)) return [];

        try
        {
            return JsonSerializer.Deserialize<List<DevelopmentLease>>(
                File.ReadAllText(path),
                Options) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private void Save(List<DevelopmentLease> leases)
    {
        Directory.CreateDirectory(_root);
        var path = PathForWorkspace();
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(leases, Options));
        File.Move(temp, path, true);
    }

    private string PathForWorkspace()
    {
        var safe = string.Concat(workspace.CurrentWorkspaceId.Select(c =>
            char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_'));
        return Path.Combine(_root, $"development-leases-{safe}.json");
    }

    private static string ResolveRoot(IConfiguration configuration)
    {
        var root = configuration["Development:StateRoot"];
        if (string.IsNullOrWhiteSpace(root))
        {
            root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PersonalAI",
                "Development");
        }

        root = Path.GetFullPath(Environment.ExpandEnvironmentVariables(root));
        Directory.CreateDirectory(root);
        return Path.Combine(root, "Leases");
    }
}
