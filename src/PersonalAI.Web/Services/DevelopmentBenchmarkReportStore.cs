using System.Text.Json;
using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public interface IDevelopmentBenchmarkReportStore
{
    IReadOnlyList<DevelopmentBenchmarkReport> GetAll();
    DevelopmentBenchmarkReport? Get(Guid id);
    void Save(DevelopmentBenchmarkReport report);
}

public sealed class DevelopmentBenchmarkReportStore(
    IWorkspaceContextAccessor workspace,
    IConfiguration configuration) : IDevelopmentBenchmarkReportStore
{
    private readonly object _gate = new();
    private readonly string _root = ResolveRoot(configuration);
    private static readonly JsonSerializerOptions Options =
        new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public IReadOnlyList<DevelopmentBenchmarkReport> GetAll()
    {
        lock (_gate)
            return Load().OrderByDescending(x => x.CompletedAt).ToArray();
    }

    public DevelopmentBenchmarkReport? Get(Guid id)
    {
        lock (_gate)
            return Load().FirstOrDefault(x => x.Id == id);
    }

    public void Save(DevelopmentBenchmarkReport report)
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

    private List<DevelopmentBenchmarkReport> Load()
    {
        var path = PathForWorkspace();
        if (!File.Exists(path)) return [];
        try
        {
            return JsonSerializer.Deserialize<List<DevelopmentBenchmarkReport>>(
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
        return Path.Combine(_root, $"development-benchmark-reports-{safe}.json");
    }

    private static string ResolveRoot(IConfiguration configuration)
    {
        var root = configuration["Development:BenchmarkRoot"];
        if (string.IsNullOrWhiteSpace(root))
        {
            root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PersonalAI",
                "Development",
                "Benchmarks");
        }

        root = Path.GetFullPath(Environment.ExpandEnvironmentVariables(root));
        Directory.CreateDirectory(root);
        return root;
    }
}
