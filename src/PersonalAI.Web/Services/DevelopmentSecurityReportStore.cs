using System.Text.Json;
using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public interface IDevelopmentSecurityReportStore
{
    IReadOnlyList<DevelopmentSecurityReport> GetAll();
    DevelopmentSecurityReport? Get(Guid id);
    void Save(DevelopmentSecurityReport report);
}

public sealed class DevelopmentSecurityReportStore(
    IWorkspaceContextAccessor workspace,
    IConfiguration configuration) : IDevelopmentSecurityReportStore
{
    private readonly object _gate = new();
    private readonly string _root = ResolveRoot(configuration);
    private static readonly JsonSerializerOptions Options =
        new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public IReadOnlyList<DevelopmentSecurityReport> GetAll()
    {
        lock (_gate)
            return Load()
                .OrderByDescending(x => x.CreatedAt)
                .ToArray();
    }

    public DevelopmentSecurityReport? Get(Guid id)
    {
        lock (_gate)
            return Load().FirstOrDefault(x => x.Id == id);
    }

    public void Save(DevelopmentSecurityReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        lock (_gate)
        {
            var all = Load();
            var index = all.FindIndex(x => x.Id == report.Id);
            if (index >= 0) all[index] = report;
            else all.Add(report);

            Directory.CreateDirectory(_root);
            var path = PathForWorkspace();
            var temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(all, Options));
            File.Move(temp, path, true);
        }
    }

    private List<DevelopmentSecurityReport> Load()
    {
        var path = PathForWorkspace();
        if (!File.Exists(path)) return [];

        try
        {
            return JsonSerializer.Deserialize<List<DevelopmentSecurityReport>>(
                File.ReadAllText(path),
                Options) ?? [];
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
        return Path.Combine(_root, $"development-security-reports-{safe}.json");
    }

    private static string ResolveRoot(IConfiguration configuration)
    {
        var root = configuration["Development:SecurityReportRoot"];
        if (string.IsNullOrWhiteSpace(root))
        {
            root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PersonalAI",
                "Development",
                "SecurityReports");
        }

        root = Path.GetFullPath(Environment.ExpandEnvironmentVariables(root));
        Directory.CreateDirectory(root);
        return root;
    }
}
