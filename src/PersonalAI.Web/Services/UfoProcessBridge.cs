using System.Diagnostics;
using System.Text.Json;

namespace PersonalAI.Web.Services;

/// <summary>
/// Opt-in boundary to the upstream Microsoft UFO CLI. This does not claim task success.
/// Registered independently of the legacy Computer Operator until real desktop acceptance passes.
/// </summary>
public sealed class UfoProcessBridge
{
    private readonly IConfiguration configuration;
    private readonly ComputerOperatorExecutionControl ownership;
    private readonly SemaphoreSlim desktopGate = new(1, 1);

    public UfoProcessBridge(
        IConfiguration configuration,
        ComputerOperatorExecutionControl ownership)
    {
        this.configuration = configuration;
        this.ownership = ownership;
    }

    public async Task<UfoBridgeResult> RunAsync(
        string task,
        string request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(task) || string.IsNullOrWhiteSpace(request))
            return new("invalid-request", "INCONCLUSIVE", false, null);

        if (!configuration.GetValue<bool>("Ufo:Enabled"))
            return new("disabled", "INCONCLUSIVE", false, null);

        var repositoryRoot = configuration["Ufo:RepositoryRoot"];
        var pythonPath = configuration["Ufo:PythonPath"];
        if (string.IsNullOrWhiteSpace(repositoryRoot) ||
            string.IsNullOrWhiteSpace(pythonPath))
            return new("not-configured", "INCONCLUSIVE", false, null);

        // No parallel UFO desktop owners through this process boundary.
        if (!await desktopGate.WaitAsync(0, cancellationToken))
            return new("desktop-busy", "INCONCLUSIVE", false, null);

        if (!ownership.TryAcquireUfo())
        {
            desktopGate.Release();
            return new("desktop-owned-by-legacy-or-ufo", "INCONCLUSIVE", false, null);
        }

        try
        {
            var script = Path.GetFullPath(Path.Combine(
                repositoryRoot, "integration", "ufo_bridge.py"));
            var ufoRoot = Path.GetFullPath(Path.Combine(
                repositoryRoot, "external", "UFO"));
            if (!File.Exists(script) ||
                !File.Exists(Path.Combine(ufoRoot, "ufo", "__main__.py")) ||
                !File.Exists(pythonPath))
                return new("missing-files", "INCONCLUSIVE", false, null);

            var maxSeconds = Math.Clamp(
                configuration.GetValue("Ufo:TimeoutSeconds", 600), 1, 3600);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(maxSeconds + 15));

            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = pythonPath,
                WorkingDirectory = repositoryRoot,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            process.StartInfo.ArgumentList.Add(script);
            process.StartInfo.ArgumentList.Add("--ufo-root");
            process.StartInfo.ArgumentList.Add(ufoRoot);
            process.StartInfo.ArgumentList.Add("--python");
            process.StartInfo.ArgumentList.Add(pythonPath);
            process.StartInfo.ArgumentList.Add("--task");
            process.StartInfo.ArgumentList.Add(task);
            process.StartInfo.ArgumentList.Add("--request");
            process.StartInfo.ArgumentList.Add(request);
            process.StartInfo.ArgumentList.Add("--timeout");
            process.StartInfo.ArgumentList.Add(maxSeconds.ToString(
                System.Globalization.CultureInfo.InvariantCulture));
            process.StartInfo.ArgumentList.Add("--execute");

            try
            {
                process.Start();
                var stdoutTask = process.StandardOutput.ReadToEndAsync();
                var stderrTask = process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync(timeout.Token);
                var stdout = await stdoutTask;
                _ = await stderrTask; // Never expose logs or potentially sensitive requests.
                const string resultMarker = "PERSONALAI_UFO_BRIDGE_RESULT=";
                var markerAt = stdout.LastIndexOf(resultMarker, StringComparison.Ordinal);
                if (markerAt < 0)
                    return new("missing-result", "INCONCLUSIVE", true, process.ExitCode);
                var resultLine = stdout[(markerAt + resultMarker.Length)..]
                    .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                    .FirstOrDefault();
                if (string.IsNullOrWhiteSpace(resultLine))
                    return new("empty-result", "INCONCLUSIVE", true, process.ExitCode);
                using var document = JsonDocument.Parse(resultLine);
                var root = document.RootElement;
                var status = root.TryGetProperty("process_status", out var p)
                    ? p.GetString() ?? "unknown" : "unknown";
                // UFO process exit is not independent acceptance evidence.
                return new(status, "INCONCLUSIVE",
                    root.TryGetProperty("executed", out var e) && e.GetBoolean(),
                    process.ExitCode);
            }
            catch (OperationCanceledException)
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                return new("cancelled-or-timeout", "INCONCLUSIVE", true, null);
            }
            catch (Exception)
            {
                if (process.StartInfo is not null && process.Id > 0 && !process.HasExited)
                    process.Kill(entireProcessTree: true);
                return new("launch-or-parse-error", "INCONCLUSIVE", false, null);
            }
        }
        finally
        {
            ownership.ReleaseUfo();
            desktopGate.Release();
        }
    }
}

public sealed record UfoBridgeResult(
    string ProcessStatus,
    string TaskVerdict,
    bool Executed,
    int? ExitCode);
