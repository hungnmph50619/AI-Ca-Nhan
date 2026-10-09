using System.Net;

namespace PersonalAI.Web.Services;

/// <summary>
/// Read-only UFO integration readiness endpoint. No desktop commands are exposed here.
/// </summary>
public static class UfoBridgeEndpoints
{
    public static WebApplication MapUfoBridge(this WebApplication app)
    {
        app.MapGet("/api/ufo/status", (
            HttpContext context,
            IConfiguration configuration,
            ComputerOperatorExecutionControl legacy) =>
        {
            var remote = context.Connection.RemoteIpAddress;
            if (remote is null || !IPAddress.IsLoopback(remote))
                return Results.StatusCode(StatusCodes.Status403Forbidden);

            var enabled = configuration.GetValue<bool>("Ufo:Enabled");
            var repository = configuration["Ufo:RepositoryRoot"];
            var python = configuration["Ufo:PythonPath"];
            var configured = !string.IsNullOrWhiteSpace(repository)
                && !string.IsNullOrWhiteSpace(python);
            var ready = configured
                && File.Exists(Path.Combine(repository!, "integration", "ufo_bridge.py"))
                && File.Exists(Path.Combine(repository!, "external", "UFO", "ufo", "__main__.py"))
                && File.Exists(python!);

            return Results.Ok(new
            {
                engine = "microsoft-ufo",
                bridgeVersion = "0.2",
                enabled,
                configured,
                filesPresent = ready,
                legacyOperatorRunning = legacy.Running,
                executionAvailable = false,
                reason = "Desktop ownership interlock and independent verification are not connected yet."
            });
        });
        return app;
    }
}
