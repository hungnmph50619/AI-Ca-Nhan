using System.Text.Json;
using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public interface IDevelopmentMergePolicyReportStore
{
    IReadOnlyList<DevelopmentMergePolicyReport> GetAll();
    DevelopmentMergePolicyReport? Get(Guid id);
    void Save(DevelopmentMergePolicyReport report);
}

public sealed class DevelopmentMergePolicyReportStore(
    IWorkspaceContextAccessor workspace,
    IConfiguration configuration) : IDevelopmentMergePolicyReportStore
{
    private readonly object _gate = new();
    private readonly string _root = ResolveRoot(configuration);
    private static readonly JsonSerializerOptions Options =
        new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public IReadOnlyList<DevelopmentMergePolicyReport> GetAll()
    {
        lock (_gate)
            return Load().OrderByDescending(x => x.CreatedAt).ToArray();
    }

    public DevelopmentMergePolicyReport? Get(Guid id)
    {
        lock (_gate)
            return Load().FirstOrDefault(x => x.Id == id);
    }

    public void Save(DevelopmentMergePolicyReport report)
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

    private List<DevelopmentMergePolicyReport> Load()
    {
        var path = PathForWorkspace();
        if (!File.Exists(path)) return [];
        try
        {
            return JsonSerializer.Deserialize<List<DevelopmentMergePolicyReport>>(
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
        return Path.Combine(_root, $"development-merge-policy-{safe}.json");
    }

    private static string ResolveRoot(IConfiguration configuration)
    {
        var root = configuration["Development:MergePolicyRoot"];
        if (string.IsNullOrWhiteSpace(root))
        {
            root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PersonalAI",
                "Development",
                "MergePolicy");
        }

        root = Path.GetFullPath(Environment.ExpandEnvironmentVariables(root));
        Directory.CreateDirectory(root);
        return root;
    }
}
