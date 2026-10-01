using System.Text.Json;
using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public interface IAutonomousEmergencyStopService
{
    AutonomousEmergencyStopState Get();
    AutonomousEmergencyStopState Set(SetAutonomousEmergencyStopRequest request);
}

public sealed class AutonomousEmergencyStopService(
    IWorkspaceContextAccessor workspace,
    IConfiguration configuration) : IAutonomousEmergencyStopService
{
    private readonly object _gate = new();
    private readonly string _root = Path.GetFullPath(
        configuration["Development:Autonomous:StateRoot"]
        ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PersonalAI", "Development", "Autonomous"));

    private static readonly JsonSerializerOptions Options =
        new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public AutonomousEmergencyStopState Get()
    {
        lock (_gate)
        {
            var path = PathForWorkspace();
            if (!File.Exists(path))
                return new(workspace.CurrentWorkspaceId, false, null, DateTimeOffset.UnixEpoch);

            try
            {
                return JsonSerializer.Deserialize<AutonomousEmergencyStopState>(
                    File.ReadAllText(path), Options)
                    ?? new(workspace.CurrentWorkspaceId, false, null, DateTimeOffset.UnixEpoch);
            }
            catch (JsonException)
            {
                throw new AutonomousDevelopmentValidationException(
                    "Emergency stop state bị hỏng; autonomous execution bị chặn.");
            }
        }
    }

    public AutonomousEmergencyStopState Set(SetAutonomousEmergencyStopRequest request)
    {
        if (!request.Confirm)
            throw new AutonomousDevelopmentValidationException(
                "Cần Confirm=true để thay đổi emergency stop.");

        var reason = (request.Reason ?? string.Empty).Trim();
        if (reason.Length is < 3 or > 1000)
            throw new AutonomousDevelopmentValidationException(
                "Reason phải có từ 3 đến 1000 ký tự.");

        var state = new AutonomousEmergencyStopState(
            workspace.CurrentWorkspaceId,
            request.Stop,
            request.Stop ? reason : null,
            DateTimeOffset.UtcNow);

        lock (_gate)
        {
            Directory.CreateDirectory(_root);
            var path = PathForWorkspace();
            var temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(state, Options));
            File.Move(temp, path, true);
        }

        return state;
    }

    private string PathForWorkspace()
    {
        var safe = string.Concat(workspace.CurrentWorkspaceId.Select(c =>
            char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_'));
        return Path.Combine(_root, $"autonomous-stop-{safe}.json");
    }
}
