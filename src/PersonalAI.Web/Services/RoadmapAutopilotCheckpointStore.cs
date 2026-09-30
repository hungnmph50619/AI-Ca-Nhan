using System.Text.Json;
using PersonalAI.Web.SelfImprovement;

namespace PersonalAI.Web.Services;

public sealed record RoadmapAutopilotCheckpoint(
    string WorkspaceId,
    string TargetVersion,
    string TargetName,
    string Branch,
    string Stage,
    int Attempt,
    string? LastProvider,
    string? LastModel,
    IReadOnlyList<RoadmapAutopilotFileChange> FilesChanged,
    bool? RestorePassed,
    bool? BuildPassed,
    bool? TestsPassed,
    DateTimeOffset UpdatedAt);

public interface IRoadmapAutopilotCheckpointStore
{
    RoadmapAutopilotCheckpoint? Get();
    void Save(RoadmapAutopilotCheckpoint checkpoint);
    void Clear();
}

public sealed class RoadmapAutopilotCheckpointStore(
    IConfiguration configuration,
    IWorkspaceContextAccessor workspace) : IRoadmapAutopilotCheckpointStore
{
    private static readonly JsonSerializerOptions Options =
        new(JsonSerializerDefaults.Web)
        {
            WriteIndented = true
        };

    private readonly object _gate = new();
    private readonly string _directory = ResolveDirectory(configuration);

    public RoadmapAutopilotCheckpoint? Get()
    {
        lock (_gate)
        {
            var path = GetPath();
            if (!File.Exists(path)) return null;

            try
            {
                return JsonSerializer.Deserialize<RoadmapAutopilotCheckpoint>(
                    File.ReadAllText(path),
                    Options);
            }
            catch (JsonException)
            {
                return null;
            }
            catch (IOException)
            {
                return null;
            }
        }
    }

    public void Save(RoadmapAutopilotCheckpoint checkpoint)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        if (!string.Equals(
                checkpoint.WorkspaceId,
                workspace.CurrentWorkspaceId,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Checkpoint không thuộc workspace hiện tại.");
        }

        lock (_gate)
        {
            Directory.CreateDirectory(_directory);
            var path = GetPath();
            var temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(checkpoint, Options));
            File.Move(temp, path, overwrite: true);
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            var path = GetPath();
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    private string GetPath()
    {
        var safeWorkspace = string.Concat(
            workspace.CurrentWorkspaceId.Select(character =>
                char.IsLetterOrDigit(character) || character is '-' or '_'
                    ? character
                    : '_'));

        if (safeWorkspace.Length == 0)
            safeWorkspace = "personal";

        return Path.Combine(
            _directory,
            $"roadmap-autopilot-{safeWorkspace}.json");
    }

    private static string ResolveDirectory(IConfiguration configuration)
    {
        var configured = configuration["DevelopmentAutopilot:Root"]?.Trim();
        if (!string.IsNullOrWhiteSpace(configured))
        {
            var expanded = Environment.ExpandEnvironmentVariables(configured);
            return Path.GetFullPath(expanded);
        }

        var localData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localData))
        {
            localData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".personalai");
        }

        return Path.Combine(localData, "PersonalAI", "DevelopmentAutopilot");
    }
}
