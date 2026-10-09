using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public interface IComputerOperatorVisualEvidenceRecorder
{
    string BeginTask(string goal);

    void RecordFrame(
        string runId,
        int cycle,
        string phase,
        DesktopScreenshotFrame frame,
        string action = "",
        string target = "",
        string expectedEffect = "",
        ComputerWindowInfo? foreground = null);

    void RecordOutcome(
        string runId,
        int cycle,
        string action,
        string target,
        string expectedEffect,
        string status,
        double confidence,
        string detail,
        ComputerWindowInfo? foreground = null);

    void CompleteTask(
        string runId,
        string status,
        string summary);
}

/// <summary>
/// Best-effort local diagnostic recorder. It never participates in planning,
/// execution or verification authority. A recorder failure must never stop
/// Computer Operator.
/// </summary>
public sealed class ComputerOperatorVisualEvidenceRecorder(
    ILogger<ComputerOperatorVisualEvidenceRecorder> logger)
    : IComputerOperatorVisualEvidenceRecorder
{
    private const int MaximumRetainedRuns = 8;
    private readonly object sync = new();

    private static readonly JsonSerializerOptions JsonOptions =
        new()
        {
            WriteIndented = true
        };

    public string BeginTask(string goal)
    {
        var runId =
            $"{DateTimeOffset.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}"[..32];

        TryWrite(() =>
        {
            var runDirectory =
                GetRunDirectory(runId);

            Directory.CreateDirectory(runDirectory);
            PruneOldRuns();

            WriteJson(
                Path.Combine(runDirectory, "manifest.json"),
                new
                {
                    runId,
                    startedAtUtc = DateTimeOffset.UtcNow,
                    status = "running",
                    goalSha256 = Hash(goal),
                    rawGoalStored = false,
                    format = "computer-operator-visual-evidence-v1"
                });
        });

        return runId;
    }

    public void RecordFrame(
        string runId,
        int cycle,
        string phase,
        DesktopScreenshotFrame frame,
        string action = "",
        string target = "",
        string expectedEffect = "",
        ComputerWindowInfo? foreground = null)
    {
        if (frame is null ||
            frame.Jpeg is null ||
            frame.Jpeg.Length < 24)
        {
            return;
        }

        TryWrite(() =>
        {
            var safePhase =
                SanitizeToken(phase);

            var stem =
                $"cycle-{Math.Max(0, cycle):000}-{safePhase}";

            var directory =
                GetRunDirectory(runId);

            Directory.CreateDirectory(directory);

            File.WriteAllBytes(
                Path.Combine(directory, stem + ".jpg"),
                frame.Jpeg);

            WriteJson(
                Path.Combine(directory, stem + ".json"),
                new
                {
                    runId,
                    cycle,
                    phase = safePhase,
                    capturedAtUtc = frame.CapturedAtUtc,
                    action = Limit(action, 80),
                    target = Limit(target, 160),
                    expectedEffect = Limit(expectedEffect, 320),
                    frame = new
                    {
                        frame.Left,
                        frame.Top,
                        frame.Width,
                        frame.Height,
                        frame.CaptureScope,
                        frame.WindowId,
                        frame.WindowTitle,
                        frame.WindowWasForeground,
                        frame.CaptureBackend,
                        frame.MonitorDevice,
                        frame.MonitorDpiX,
                        frame.MonitorDpiY,
                        frame.WindowVisibleRatio,
                        frame.WindowLikelyOccluded
                    },
                    foreground = foreground is null
                        ? null
                        : new
                        {
                            foreground.WindowId,
                            foreground.Title,
                            foreground.ProcessName,
                            foreground.ProcessId,
                            foreground.Left,
                            foreground.Top,
                            foreground.Width,
                            foreground.Height
                        }
                });
        });
    }

    public void RecordOutcome(
        string runId,
        int cycle,
        string action,
        string target,
        string expectedEffect,
        string status,
        double confidence,
        string detail,
        ComputerWindowInfo? foreground = null)
    {
        TryWrite(() =>
        {
            var directory =
                GetRunDirectory(runId);

            Directory.CreateDirectory(directory);

            WriteJson(
                Path.Combine(
                    directory,
                    $"cycle-{Math.Max(0, cycle):000}-outcome.json"),
                new
                {
                    runId,
                    cycle,
                    recordedAtUtc = DateTimeOffset.UtcNow,
                    action = Limit(action, 80),
                    target = Limit(target, 160),
                    expectedEffect = Limit(expectedEffect, 320),
                    status = SanitizeToken(status),
                    confidence = Math.Clamp(confidence, 0, 1),
                    detail = Limit(detail, 600),
                    foreground = foreground is null
                        ? null
                        : new
                        {
                            foreground.WindowId,
                            foreground.Title,
                            foreground.ProcessName,
                            foreground.ProcessId
                        }
                });
        });
    }

    public void CompleteTask(
        string runId,
        string status,
        string summary)
    {
        TryWrite(() =>
        {
            var directory =
                GetRunDirectory(runId);

            Directory.CreateDirectory(directory);

            WriteJson(
                Path.Combine(directory, "completion.json"),
                new
                {
                    runId,
                    completedAtUtc = DateTimeOffset.UtcNow,
                    status = SanitizeToken(status),
                    summary = Limit(summary, 800)
                });
        });
    }

    internal static string GetRelativeRootForAcceptance() =>
        Path.Combine(
            "benchmark-results",
            "operator-diagnostics");

    private void TryWrite(Action write)
    {
        try
        {
            lock (sync)
                write();
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            ArgumentException or
            NotSupportedException)
        {
            logger.LogWarning(
                exception,
                "Không ghi được Visual Evidence Recorder; Operator vẫn tiếp tục.");
        }
    }

    private static string GetRootDirectory()
    {
        // dotnet run, IDE launches and service hosting can use different
        // working directories. Anchor evidence to the repository when present.
        foreach (var candidate in new[]
        {
            Environment.CurrentDirectory,
            AppContext.BaseDirectory
        })
        {
            var directory = new DirectoryInfo(candidate);
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(
                        directory.FullName,
                        "scripts",
                        "run_computer_operator_external_benchmark.ps1")))
                {
                    return Path.Combine(
                        directory.FullName,
                        GetRelativeRootForAcceptance());
                }

                directory = directory.Parent;
            }
        }

        // Non-repository deployments still retain a valid local location.
        return Path.Combine(
            Environment.CurrentDirectory,
            GetRelativeRootForAcceptance());
    }

    private static string GetRunDirectory(
        string runId) =>
        Path.Combine(
            GetRootDirectory(),
            SanitizeToken(runId));

    private static void PruneOldRuns()
    {
        var root =
            GetRootDirectory();

        if (!Directory.Exists(root))
            return;

        var runs =
            new DirectoryInfo(root)
                .GetDirectories()
                .OrderByDescending(item => item.CreationTimeUtc)
                .ToArray();

        foreach (var stale in runs.Skip(MaximumRetainedRuns))
        {
            try
            {
                stale.Delete(recursive: true);
            }
            catch
            {
                // Retention cleanup is best-effort too.
            }
        }
    }

    private static void WriteJson(
        string path,
        object payload) =>
        File.WriteAllText(
            path,
            JsonSerializer.Serialize(
                payload,
                JsonOptions),
            Encoding.UTF8);

    private static string Hash(
        string value)
    {
        var bytes =
            SHA256.HashData(
                Encoding.UTF8.GetBytes(
                    value ?? string.Empty));

        return Convert.ToHexString(bytes);
    }

    private static string SanitizeToken(
        string? value)
    {
        var normalized =
            string.IsNullOrWhiteSpace(value)
                ? "unknown"
                : value.Trim().ToLowerInvariant();

        var safe =
            new string(
                normalized
                    .Select(character =>
                        char.IsLetterOrDigit(character) ||
                        character is '-' or '_'
                            ? character
                            : '-')
                    .ToArray());

        while (safe.Contains("--", StringComparison.Ordinal))
            safe = safe.Replace("--", "-", StringComparison.Ordinal);

        return safe.Trim('-') is { Length: > 0 } result
            ? Limit(result, 80)
            : "unknown";
    }

    private static string Limit(
        string? value,
        int maximum)
    {
        var normalized =
            string.Join(
                " ",
                (value ?? string.Empty)
                    .Split(
                        [' ', '\t', '\r', '\n'],
                        StringSplitOptions.RemoveEmptyEntries));

        return normalized.Length <= maximum
            ? normalized
            : normalized[..Math.Max(1, maximum - 1)] + "…";
    }
}
