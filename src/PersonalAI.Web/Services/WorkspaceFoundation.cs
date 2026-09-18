using System.Text.Json;
using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public interface IPersonalWorkspaceStore
{
    IReadOnlyList<PersonalWorkspace> GetAll();
    PersonalWorkspace? Get(string workspaceId);
    bool Exists(string workspaceId);
    PersonalWorkspace Create(CreatePersonalWorkspaceRequest request);
    PersonalWorkspace? Update(string workspaceId, UpdatePersonalWorkspaceRequest request);
}

public interface IWorkspaceContextAccessor
{
    string CurrentWorkspaceId { get; }
    PersonalWorkspace CurrentWorkspace { get; }
}

public interface IWorkspaceStoragePathResolver
{
    string CurrentWorkspaceId { get; }
    string KnowledgeDirectory { get; }
    string KnowledgeDatabasePath { get; }
    string KnowledgeFilesDirectory { get; }
}

public sealed class WorkspaceValidationException(string message) : Exception(message);

public sealed class PersonalWorkspaceStore : IPersonalWorkspaceStore
{
    public const int MaximumWorkspaces = 20;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private static readonly string[] ActiveModules =
    [
        "memory",
        "documents",
        "tasks",
        "files",
        "conversations"
    ];

    private static readonly string[] ReservedModules =
    [
        "agents",
        "policies"
    ];

    private readonly object _gate = new();
    private readonly string _storagePath;
    private List<StoredWorkspace> _workspaces;

    public PersonalWorkspaceStore(IHostEnvironment hostEnvironment)
    {
        var localData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localData))
        {
            localData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".personalai");
        }

        var directory = Path.Combine(localData, "PersonalAI", "Workspaces");
        Directory.CreateDirectory(directory);
        _storagePath = Path.Combine(directory, "workspaces.json");
        _workspaces = Load();
        EnsureBuiltIns();
        Persist();
    }

    public IReadOnlyList<PersonalWorkspace> GetAll()
    {
        lock (_gate)
        {
            return _workspaces
                .OrderBy(workspace => workspace.SortOrder)
                .ThenBy(workspace => workspace.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(ToPublic)
                .ToArray();
        }
    }

    public PersonalWorkspace? Get(string workspaceId)
    {
        var normalized = NormalizeId(workspaceId);
        lock (_gate)
        {
            var workspace = _workspaces.FirstOrDefault(
                item => string.Equals(
                    item.Id,
                    normalized,
                    StringComparison.OrdinalIgnoreCase));
            return workspace is null ? null : ToPublic(workspace);
        }
    }

    public bool Exists(string workspaceId)
    {
        var normalized = NormalizeId(workspaceId);
        lock (_gate)
        {
            return _workspaces.Any(
                item => string.Equals(
                    item.Id,
                    normalized,
                    StringComparison.OrdinalIgnoreCase));
        }
    }

    public PersonalWorkspace Create(CreatePersonalWorkspaceRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var name = ValidateName(request.Name);
        var description = ValidateDescription(request.Description);
        var now = DateTimeOffset.UtcNow;

        lock (_gate)
        {
            if (_workspaces.Count >= MaximumWorkspaces)
            {
                throw new WorkspaceValidationException(
                    $"PersonalAI hỗ trợ tối đa {MaximumWorkspaces} không gian làm việc.");
            }

            if (_workspaces.Any(item =>
                string.Equals(item.Name, name, StringComparison.CurrentCultureIgnoreCase)))
            {
                throw new WorkspaceValidationException(
                    "Đã có một không gian làm việc cùng tên.");
            }

            var workspace = new StoredWorkspace
            {
                Id = "ws-" + Guid.NewGuid().ToString("N")[..12],
                Name = name,
                Description = description,
                IsBuiltIn = false,
                CreatedAt = now,
                UpdatedAt = now,
                SortOrder = 100 + _workspaces.Count
            };
            _workspaces.Add(workspace);
            Persist();
            return ToPublic(workspace);
        }
    }

    public PersonalWorkspace? Update(
        string workspaceId,
        UpdatePersonalWorkspaceRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var normalized = NormalizeId(workspaceId);
        var name = ValidateName(request.Name);
        var description = ValidateDescription(request.Description);

        lock (_gate)
        {
            var workspace = _workspaces.FirstOrDefault(
                item => string.Equals(
                    item.Id,
                    normalized,
                    StringComparison.OrdinalIgnoreCase));
            if (workspace is null)
            {
                return null;
            }

            if (_workspaces.Any(item =>
                !string.Equals(item.Id, workspace.Id, StringComparison.OrdinalIgnoreCase)
                && string.Equals(item.Name, name, StringComparison.CurrentCultureIgnoreCase)))
            {
                throw new WorkspaceValidationException(
                    "Đã có một không gian làm việc cùng tên.");
            }

            workspace.Name = name;
            workspace.Description = description;
            workspace.UpdatedAt = DateTimeOffset.UtcNow;
            Persist();
            return ToPublic(workspace);
        }
    }

    private List<StoredWorkspace> Load()
    {
        if (!File.Exists(_storagePath))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<StoredWorkspace>>(
                    File.ReadAllText(_storagePath),
                    JsonOptions)
                ?? [];
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or JsonException)
        {
            return [];
        }
    }

    private void EnsureBuiltIns()
    {
        var now = DateTimeOffset.UtcNow;
        var builtIns = new[]
        {
            new StoredWorkspace
            {
                Id = PersonalWorkspaceIds.Personal,
                Name = "Cá nhân",
                Description = "Trí nhớ, tài liệu, tác vụ và tệp cá nhân.",
                IsBuiltIn = true,
                CreatedAt = now,
                UpdatedAt = now,
                SortOrder = 0
            },
            new StoredWorkspace
            {
                Id = PersonalWorkspaceIds.Work,
                Name = "Công việc",
                Description = "Không gian dành cho công việc và dự án nghề nghiệp.",
                IsBuiltIn = true,
                CreatedAt = now,
                UpdatedAt = now,
                SortOrder = 10
            },
            new StoredWorkspace
            {
                Id = PersonalWorkspaceIds.Study,
                Name = "Học tập",
                Description = "Không gian dành cho học tập, nghiên cứu và bài tập.",
                IsBuiltIn = true,
                CreatedAt = now,
                UpdatedAt = now,
                SortOrder = 20
            },
            new StoredWorkspace
            {
                Id = PersonalWorkspaceIds.PersonalAi,
                Name = "AI Cá Nhân",
                Description = "Không gian dành cho chính dự án PersonalAI.",
                IsBuiltIn = true,
                CreatedAt = now,
                UpdatedAt = now,
                SortOrder = 30
            },
            new StoredWorkspace
            {
                Id = PersonalWorkspaceIds.Travel,
                Name = "Du lịch",
                Description = "Không gian dành cho kế hoạch và tài liệu chuyến đi.",
                IsBuiltIn = true,
                CreatedAt = now,
                UpdatedAt = now,
                SortOrder = 40
            }
        };

        foreach (var builtIn in builtIns)
        {
            var existing = _workspaces.FirstOrDefault(item =>
                string.Equals(item.Id, builtIn.Id, StringComparison.OrdinalIgnoreCase));
            if (existing is null)
            {
                _workspaces.Add(builtIn);
                continue;
            }

            existing.IsBuiltIn = true;
            existing.SortOrder = builtIn.SortOrder;
            if (string.IsNullOrWhiteSpace(existing.Name))
            {
                existing.Name = builtIn.Name;
            }
            if (string.IsNullOrWhiteSpace(existing.Description))
            {
                existing.Description = builtIn.Description;
            }
        }
    }

    private void Persist()
    {
        var json = JsonSerializer.Serialize(_workspaces, JsonOptions);
        var temporaryPath = _storagePath + ".tmp";
        File.WriteAllText(temporaryPath, json);
        File.Move(temporaryPath, _storagePath, true);
    }

    private static PersonalWorkspace ToPublic(StoredWorkspace workspace) =>
        new(
            workspace.Id,
            workspace.Name,
            workspace.Description,
            workspace.IsBuiltIn,
            workspace.CreatedAt,
            workspace.UpdatedAt,
            ActiveModules,
            ReservedModules);

    private static string NormalizeId(string? value)
    {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized.Length is < 1 or > 80)
        {
            return string.Empty;
        }

        return normalized;
    }

    private static string ValidateName(string? value)
    {
        var name = (value ?? string.Empty).Trim();
        if (name.Length is < 2 or > 60)
        {
            throw new WorkspaceValidationException(
                "Tên không gian làm việc phải có từ 2 đến 60 ký tự.");
        }

        return name;
    }

    private static string ValidateDescription(string? value)
    {
        var description = (value ?? string.Empty).Trim();
        if (description.Length > 300)
        {
            throw new WorkspaceValidationException(
                "Mô tả không gian làm việc không được dài hơn 300 ký tự.");
        }

        return description;
    }

    private sealed class StoredWorkspace
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public bool IsBuiltIn { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
        public int SortOrder { get; set; }
    }
}

public sealed class WorkspaceContextAccessor(
    IHttpContextAccessor httpContextAccessor,
    IPersonalWorkspaceStore workspaceStore) : IWorkspaceContextAccessor
{
    public string CurrentWorkspaceId
    {
        get
        {
            var requested = httpContextAccessor.HttpContext?
                .Request.Headers[WorkspaceEndpoints.WorkspaceHeaderName]
                .FirstOrDefault()?
                .Trim();

            if (string.IsNullOrWhiteSpace(requested))
            {
                return PersonalWorkspaceIds.Personal;
            }

            return workspaceStore.Exists(requested)
                ? requested.ToLowerInvariant()
                : PersonalWorkspaceIds.Personal;
        }
    }

    public PersonalWorkspace CurrentWorkspace =>
        workspaceStore.Get(CurrentWorkspaceId)
        ?? throw new InvalidOperationException(
            "Không tìm thấy không gian làm việc hiện tại.");
}

public sealed class WorkspaceStoragePathResolver(
    IWorkspaceContextAccessor workspaceContext) : IWorkspaceStoragePathResolver
{
    private readonly string _personalAiRoot = ResolvePersonalAiRoot();

    public string CurrentWorkspaceId => workspaceContext.CurrentWorkspaceId;

    public string KnowledgeDirectory =>
        CurrentWorkspaceId == PersonalWorkspaceIds.Personal
            ? Path.Combine(_personalAiRoot, "Knowledge")
            : Path.Combine(
                _personalAiRoot,
                "Workspaces",
                "data",
                CurrentWorkspaceId,
                "Knowledge");

    public string KnowledgeDatabasePath =>
        Path.Combine(KnowledgeDirectory, "personal-ai.db");

    public string KnowledgeFilesDirectory =>
        Path.Combine(KnowledgeDirectory, "files");

    private static string ResolvePersonalAiRoot()
    {
        var localData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localData))
        {
            localData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".personalai");
        }

        return Path.Combine(localData, "PersonalAI");
    }
}
