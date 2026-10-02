using System.Text.Json;
using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public interface IDevelopmentLocalSyncReportStore
{
    IReadOnlyList<DevelopmentLocalSyncReport> GetAll();
    DevelopmentLocalSyncReport? Get(Guid id);
    void Save(DevelopmentLocalSyncReport report);
}

public sealed class DevelopmentLocalSyncReportStore(
    IWorkspaceContextAccessor workspace,
    IConfiguration configuration) : IDevelopmentLocalSyncReportStore
{
    private readonly object _gate = new();
    private readonly string _root = ResolveRoot(configuration);
    private static readonly JsonSerializerOptions Options =
        new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public IReadOnlyList<DevelopmentLocalSyncReport> GetAll()
    {
        lock (_gate)
            return Load().OrderByDescending(x => x.CreatedAt).ToArray();
    }

    public DevelopmentLocalSyncReport? Get(Guid id)
    {
        lock (_gate)
            return Load().FirstOrDefault(x => x.Id == id);
    }

    public void Save(DevelopmentLocalSyncReport report)
    {
        lock (_gate)
        {
            var all = Load();
            all.Add(report);
            Directory.CreateDirectory(_root);
            var path = PathForWorkspace();
            var temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(all, Options));
            File.Move(temp, path, true);
        }
    }

    private List<DevelopmentLocalSyncReport> Load()
    {
        var path = PathForWorkspace();
        if (!File.Exists(path)) return [];
        try
        {
            return JsonSerializer.Deserialize<List<DevelopmentLocalSyncReport>>(
                File.ReadAllText(path), Options) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private string PathForWorkspace()
    {
        var safe = string.Concat(workspace.CurrentWorkspaceId.Select(c =>
            char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_'));
        return Path.Combine(_root, $"development-local-sync-{safe}.json");
    }

    private static string ResolveRoot(IConfiguration configuration)
    {
        var root = configuration["Development:LocalSyncRoot"];
        if (string.IsNullOrWhiteSpace(root))
        {
            root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PersonalAI",
                "Development",
                "LocalSync");
        }

        root = Path.GetFullPath(Environment.ExpandEnvironmentVariables(root));
        Directory.CreateDirectory(root);
        return root;
    }
}
