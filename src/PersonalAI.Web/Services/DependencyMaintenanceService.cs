using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public interface IDependencyMaintenanceService
{
    DependencyMaintenanceStatus GetStatus();
    IReadOnlyList<DependencyMaintenancePlan> GetAll();
    DependencyMaintenancePlan? Get(Guid id);
    Task<DependencyMaintenancePlan> PlanAsync(
        PlanDependencyMaintenanceRequest request,
        CancellationToken cancellationToken = default);
    Task<DependencyMaintenancePlan> ApplyAsync(
        ApplyDependencyMaintenanceRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class DependencyMaintenanceService(
    IDevelopmentRunService runs,
    IDevelopmentRunWorktreeService runWorktrees,
    IWorkspaceFileService files,
    IWorkspaceContextAccessor workspace,
    IConfiguration configuration,
    IAuditRecorder audit) : IDependencyMaintenanceService
{
    private static readonly IReadOnlyList<string> RequiredGates =
    [
        DevelopmentRunStages.Testing,
        DevelopmentRunStages.Review,
        DevelopmentRunStages.Security,
        DevelopmentRunStages.Benchmark
    ];

    private readonly object _gate = new();
    private readonly string _root = ResolveRoot(configuration);
    private static readonly JsonSerializerOptions Options =
        new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public DependencyMaintenanceStatus GetStatus()
    {
        var all = GetAll();
        return new(
            PersonalAiRelease.Version,
            workspace.CurrentWorkspaceId,
            all.Count,
            all.Count(x => x.State == DependencyMaintenanceStates.Applied),
            all.Count(x =>
                x.UpgradeKind == DependencyUpgradeKinds.Major &&
                x.State != DependencyMaintenanceStates.Applied),
            MajorAutoUpgradeAllowed: false,
            WorktreeOnly: true,
            ShaGuardEnabled: true,
            RequiredGates);
    }

    public IReadOnlyList<DependencyMaintenancePlan> GetAll()
    {
        lock (_gate)
            return Load().OrderByDescending(x => x.UpdatedAt).ToArray();
    }

    public DependencyMaintenancePlan? Get(Guid id)
    {
        lock (_gate)
            return Load().FirstOrDefault(x => x.Id == id);
    }

    public async Task<DependencyMaintenancePlan> PlanAsync(
        PlanDependencyMaintenanceRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var run = runs.Get(request.DevelopmentRunId)
            ?? throw new KeyNotFoundException("Không tìm thấy DevelopmentRun.");

        if (run.WorkspaceId != workspace.CurrentWorkspaceId)
            throw new DependencyMaintenanceValidationException(
                "DevelopmentRun không thuộc workspace hiện tại.");

        if (run.Status != "active" || run.Stage != DevelopmentRunStages.Coding)
            throw new DependencyMaintenanceValidationException(
                "Dependency maintenance chỉ được lập/applied khi DevelopmentRun đang ở stage coding.");

        var projectPath = NormalizeProjectPath(request.ProjectPath);
        var packageId = NormalizePackageId(request.PackageId);
        var target = ParseVersion(request.TargetVersion);

        var binding = await runWorktrees.EnsureAsync(
            new EnsureDevelopmentRunWorktreeRequest(
                run.Id,
                request.StartPoint,
                request.ConfirmCreateWorktree),
            cancellationToken);

        var workspacePath = Prefix(binding.WorktreePath, projectPath);
        var file = await files.ReadTextAsync(
            workspacePath,
            WorkspaceFileService.MaximumReturnedCharacters,
            cancellationToken);

        if (file.Truncated)
            throw new DependencyMaintenanceValidationException(
                "Project file quá lớn để cập nhật dependency an toàn.");

        var document = ParseProject(file.Content);
        var package = FindPackageReference(document, packageId)
            ?? throw new DependencyMaintenanceValidationException(
                $"Không tìm thấy PackageReference '{packageId}'.");

        var currentText = ReadPackageVersion(package)
            ?? throw new DependencyMaintenanceValidationException(
                $"PackageReference '{packageId}' không có Version tĩnh.");

        var current = ParseVersion(currentText);
        var kind = Classify(current, target);

        if (CompareVersions(target, current) < 0)
            throw new DependencyMaintenanceValidationException(
                "TargetVersion thấp hơn CurrentVersion; dependency maintenance không tự downgrade.");

        var major = kind == DependencyUpgradeKinds.Major;
        var now = DateTimeOffset.UtcNow;
        var plan = new DependencyMaintenancePlan(
            Guid.NewGuid(),
            workspace.CurrentWorkspaceId,
            run.Id,
            binding.WorktreePath,
            projectPath,
            packageId,
            current.Original,
            target.Original,
            kind,
            major ? "high" : kind == DependencyUpgradeKinds.Minor ? "medium" : "low",
            AutomaticEligible: !major && kind != DependencyUpgradeKinds.Same,
            RequiresUserApproval: major,
            State: major
                ? DependencyMaintenanceStates.BlockedMajor
                : DependencyMaintenanceStates.Planned,
            ResultSha256: null,
            RequiredGates,
            now,
            now);

        lock (_gate)
        {
            var all = Load();
            all.Add(plan);
            Save(all);
        }

        audit.Record(
            AuditAgents.System,
            "development.dependency.plan",
            $"development-run:{run.Id:D}",
            $"plan:{plan.Id:D};package:{packageId};from:{current.Original};to:{target.Original};kind:{kind};auto:{plan.AutomaticEligible}",
            AuditResults.Prepared);

        return plan;
    }

    public async Task<DependencyMaintenancePlan> ApplyAsync(
        ApplyDependencyMaintenanceRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!request.ConfirmApply)
            throw new DependencyMaintenanceValidationException(
                "Cần ConfirmApply=true để sửa dependency.");

        DependencyMaintenancePlan plan;
        lock (_gate)
        {
            plan = Load().FirstOrDefault(x => x.Id == request.PlanId)
                ?? throw new KeyNotFoundException("Không tìm thấy dependency maintenance plan.");
        }

        if (plan.State == DependencyMaintenanceStates.Applied)
            return plan;

        var run = runs.Get(plan.DevelopmentRunId)
            ?? throw new KeyNotFoundException("Không tìm thấy DevelopmentRun.");

        if (run.Status != "active" || run.Stage != DevelopmentRunStages.Coding)
            throw new DependencyMaintenanceValidationException(
                "Chỉ được apply dependency khi DevelopmentRun vẫn ở stage coding.");

        if (plan.UpgradeKind == DependencyUpgradeKinds.Same)
            throw new DependencyMaintenanceValidationException(
                "Dependency đã ở đúng target version; không có thay đổi để apply.");

        if (plan.UpgradeKind == DependencyUpgradeKinds.Major &&
            !request.ConfirmMajorUpgrade)
        {
            throw new DependencyMaintenanceValidationException(
                "Major upgrade rủi ro cao không được auto-apply; cần ConfirmMajorUpgrade=true từ người dùng.");
        }

        var binding = runWorktrees.GetByRun(run.Id)
            ?? throw new DependencyMaintenanceValidationException(
                "DevelopmentRun chưa có persisted worktree binding.");

        if (!binding.WorktreePath.Equals(
                plan.WorktreePath,
                StringComparison.OrdinalIgnoreCase) ||
            binding.State == DevelopmentRunWorktreeStates.Removed)
        {
            throw new DependencyMaintenanceValidationException(
                "Worktree binding không còn khớp dependency plan.");
        }

        var workspacePath = Prefix(plan.WorktreePath, plan.ProjectPath);
        var file = await files.ReadTextAsync(
            workspacePath,
            WorkspaceFileService.MaximumReturnedCharacters,
            cancellationToken);

        if (file.Truncated)
            throw new DependencyMaintenanceValidationException(
                "Project file quá lớn để cập nhật an toàn.");

        var document = ParseProject(file.Content);
        var package = FindPackageReference(document, plan.PackageId)
            ?? throw new DependencyMaintenanceValidationException(
                "PackageReference không còn tồn tại.");

        var current = ReadPackageVersion(package)
            ?? throw new DependencyMaintenanceValidationException(
                "PackageReference không còn Version tĩnh.");

        if (!current.Equals(plan.CurrentVersion, StringComparison.Ordinal))
            throw new DependencyMaintenanceValidationException(
                "Project file đã thay đổi từ lúc lập plan; từ chối ghi đè dependency.");

        WritePackageVersion(package, plan.TargetVersion);
        var updated = SerializeProject(document, file.Content);

        var expectedSha = Sha256(file.Content);
        var result = await files.WriteTextAsync(
            workspacePath,
            updated,
            "overwrite",
            expectedSha,
            cancellationToken);

        var applied = plan with
        {
            State = DependencyMaintenanceStates.Applied,
            ResultSha256 = result.Sha256,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        lock (_gate)
        {
            var all = Load();
            var index = all.FindIndex(x => x.Id == plan.Id);
            if (index < 0)
                throw new KeyNotFoundException("Dependency maintenance plan đã biến mất.");
            all[index] = applied;
            Save(all);
        }

        audit.Record(
            AuditAgents.System,
            "development.dependency.apply",
            $"development-run:{run.Id:D}",
            $"plan:{plan.Id:D};package:{plan.PackageId};from:{plan.CurrentVersion};to:{plan.TargetVersion};kind:{plan.UpgradeKind};sha256:{result.Sha256}",
            AuditResults.Succeeded);

        return applied;
    }

    private static XDocument ParseProject(string content)
    {
        try
        {
            return XDocument.Parse(
                content,
                LoadOptions.PreserveWhitespace);
        }
        catch (Exception exception) when (
            exception is System.Xml.XmlException or InvalidOperationException)
        {
            throw new DependencyMaintenanceValidationException(
                "Project file XML không hợp lệ.");
        }
    }

    private static XElement? FindPackageReference(
        XDocument document,
        string packageId) =>
        document.Descendants()
            .FirstOrDefault(x =>
                x.Name.LocalName == "PackageReference" &&
                string.Equals(
                    x.Attribute("Include")?.Value,
                    packageId,
                    StringComparison.OrdinalIgnoreCase));

    private static string? ReadPackageVersion(XElement package)
    {
        var attribute = package.Attribute("Version")?.Value?.Trim();
        if (!string.IsNullOrWhiteSpace(attribute))
            return attribute;

        return package.Elements()
            .FirstOrDefault(x => x.Name.LocalName == "Version")
            ?.Value
            .Trim();
    }

    private static void WritePackageVersion(
        XElement package,
        string targetVersion)
    {
        var attribute = package.Attribute("Version");
        if (attribute is not null)
        {
            attribute.Value = targetVersion;
            return;
        }

        var element = package.Elements()
            .FirstOrDefault(x => x.Name.LocalName == "Version");

        if (element is null)
            throw new DependencyMaintenanceValidationException(
                "PackageReference không có Version có thể cập nhật.");

        element.Value = targetVersion;
    }

    private static string SerializeProject(
        XDocument document,
        string original)
    {
        using var writer = new Utf8StringWriter();
        document.Save(
            writer,
            SaveOptions.DisableFormatting);

        var value = writer.ToString();
        return original.EndsWith("\n", StringComparison.Ordinal)
            ? value.TrimEnd('\r', '\n') + Environment.NewLine
            : value.TrimEnd('\r', '\n');
    }

    private static VersionParts ParseVersion(string? value)
    {
        var original = (value ?? string.Empty).Trim();
        var core = original.Split('-', 2)[0].Split('+', 2)[0];
        var parts = core.Split('.');

        if (parts.Length < 2 || parts.Length > 4 ||
            parts.Any(x => !int.TryParse(x, out _)))
        {
            throw new DependencyMaintenanceValidationException(
                $"Version '{original}' không phải SemVer số tĩnh được hỗ trợ.");
        }

        var values = parts.Select(int.Parse).ToArray();
        return new(
            values.ElementAtOrDefault(0),
            values.ElementAtOrDefault(1),
            values.ElementAtOrDefault(2),
            values.ElementAtOrDefault(3),
            original);
    }

    private static int CompareVersions(VersionParts left, VersionParts right)
    {
        var a = new[] { left.Major, left.Minor, left.Patch, left.Revision };
        var b = new[] { right.Major, right.Minor, right.Patch, right.Revision };

        for (var i = 0; i < a.Length; i++)
        {
            var comparison = a[i].CompareTo(b[i]);
            if (comparison != 0) return comparison;
        }

        return 0;
    }

    private static string Classify(VersionParts current, VersionParts target)
    {
        if (CompareVersions(current, target) == 0)
            return DependencyUpgradeKinds.Same;
        if (current.Major != target.Major)
            return DependencyUpgradeKinds.Major;
        if (current.Minor != target.Minor)
            return DependencyUpgradeKinds.Minor;
        return DependencyUpgradeKinds.Patch;
    }

    private static string NormalizeProjectPath(string? value)
    {
        var path = (value ?? string.Empty).Trim().Replace('\\', '/').Trim('/');
        if (path.Length is < 1 or > 400 ||
            Path.IsPathRooted(path) ||
            path.StartsWith("../", StringComparison.Ordinal) ||
            path.Contains("/../", StringComparison.Ordinal) ||
            !path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
        {
            throw new DependencyMaintenanceValidationException(
                "ProjectPath phải là .csproj tương đối an toàn.");
        }
        return path;
    }

    private static string NormalizePackageId(string? value)
    {
        var id = (value ?? string.Empty).Trim();
        if (id.Length is < 1 or > 200 ||
            id.Any(c =>
                !(char.IsLetterOrDigit(c) ||
                  c is '.' or '-' or '_')))
        {
            throw new DependencyMaintenanceValidationException(
                "PackageId không hợp lệ.");
        }
        return id;
    }

    private static string Prefix(string worktreePath, string relativePath) =>
        $"{worktreePath.Trim().Replace('\\', '/').Trim('/')}/{relativePath}";

    private static string Sha256(string value) =>
        Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(value)))
        .ToLowerInvariant();

    private List<DependencyMaintenancePlan> Load()
    {
        var path = PathForWorkspace();
        if (!File.Exists(path)) return [];

        try
        {
            return JsonSerializer.Deserialize<List<DependencyMaintenancePlan>>(
                File.ReadAllText(path),
                Options) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private void Save(List<DependencyMaintenancePlan> plans)
    {
        Directory.CreateDirectory(_root);
        var path = PathForWorkspace();
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(plans, Options));
        File.Move(temp, path, true);
    }

    private string PathForWorkspace()
    {
        var safe = string.Concat(workspace.CurrentWorkspaceId.Select(c =>
            char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_'));
        return Path.Combine(
            _root,
            $"dependency-maintenance-{safe}.json");
    }

    private static string ResolveRoot(IConfiguration configuration)
    {
        var root = configuration["Development:DependencyMaintenanceRoot"];
        if (string.IsNullOrWhiteSpace(root))
        {
            root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PersonalAI",
                "Development",
                "DependencyMaintenance");
        }

        root = Path.GetFullPath(
            Environment.ExpandEnvironmentVariables(root));
        Directory.CreateDirectory(root);
        return root;
    }

    private sealed record VersionParts(
        int Major,
        int Minor,
        int Patch,
        int Revision,
        string Original);

    private sealed class Utf8StringWriter : StringWriter
    {
        public override Encoding Encoding => new UTF8Encoding(false);
    }
}
