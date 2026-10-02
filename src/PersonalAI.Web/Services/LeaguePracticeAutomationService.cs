using System.Diagnostics;
using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public interface ILeaguePracticeAutomationService
{
    Task<LeaguePracticeAutomationResult> OpenPracticeToolAsync(
        CancellationToken cancellationToken = default);
}

public sealed class LeaguePracticeAutomationService(
    IComputerUseService computer,
    IDesktopScreenshotService screenshots,
    DesktopVisionService vision,
    ComputerControlGate control)
    : ILeaguePracticeAutomationService
{
    private static readonly string[] ClientProcesses =
    [
        "LeagueClientUx",
        "LeagueClientUxRender",
        "LeagueClient",
        "RiotClientServices",
        "RiotClientUx",
        "RiotClientUxRender"
    ];

    public async Task<LeaguePracticeAutomationResult> OpenPracticeToolAsync(
        CancellationToken cancellationToken = default)
    {
        var startedAt = DateTimeOffset.UtcNow;
        if (!OperatingSystem.IsWindows() || !Environment.UserInteractive)
            return Result(
                "blocked",
                "platform",
                0,
                "Chỉ hỗ trợ phiên Windows đang tương tác.",
                startedAt);

        if (!vision.Ready)
            return Result(
                "blocked",
                "vision",
                0,
                "Cần cấu hình Gemini để Desktop Vision xác minh từng trạng thái UI.",
                startedAt);

        try
        {
            await EnsureLeagueClientAsync(cancellationToken);
        }
        catch (Exception ex) when (
            ex is ToolExecutionInputException or
            InvalidOperationException or
            System.ComponentModel.Win32Exception)
        {
            return Result(
                "blocked",
                "launch",
                0,
                ex.Message,
                startedAt);
        }

        control.EnableScopedAutomation(
            maximumActions: 14,
            maximumSeconds: 170);

        var clicks = 0;
        var consecutiveWaits = 0;
        var repeatedDecisionCount = 0;
        string? lastDecisionFingerprint = null;
        string? lastAction = null;

        try
        {
            for (var observation = 0; observation < 16; observation++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var window = await WaitForClientWindowAsync(
                    TimeSpan.FromSeconds(12),
                    cancellationToken);

                if (window is null)
                {
                    if (string.Equals(
                        lastAction,
                        "start-game",
                        StringComparison.OrdinalIgnoreCase))
                    {
                        return Result(
                            "completed",
                            "start-game",
                            clicks,
                            "Đã nhấn Bắt đầu và League client đã chuyển khỏi trạng thái menu hiển thị.",
                            startedAt);
                    }

                    return Result(
                        "blocked",
                        lastAction ?? "observe",
                        clicks,
                        "Không còn thấy cửa sổ Riot/League để tiếp tục quan sát.",
                        startedAt);
                }

                if (!window.IsForeground)
                {
                    computer.FocusWindow(window.WindowId);
                    await Task.Delay(650, cancellationToken);
                    window = FindClientWindow();
                    if (window is null || !window.IsForeground)
                    {
                        return Result(
                            "blocked",
                            "focus",
                            clicks,
                            "Không giữ được Riot/League client ở foreground.",
                            startedAt);
                    }
                }

                using var frame = CaptureDisposable(window);
                var decision = await vision.DecideLeaguePracticeActionAsync(
                    frame.Value,
                    cancellationToken);

                if (decision.Action == "complete")
                {
                    return Result(
                        "completed",
                        "practice-tool",
                        clicks,
                        $"Visual Agent xác nhận mục tiêu đã hoàn tất: {decision.Reason}",
                        startedAt);
                }

                if (decision.Action == "blocked")
                {
                    return Result(
                        "blocked",
                        "vision",
                        clicks,
                        $"Visual Agent dừng an toàn: {decision.Reason}",
                        startedAt);
                }

                if (decision.Action == "wait")
                {
                    consecutiveWaits++;
                    lastAction = "wait";
                    if (consecutiveWaits >= 5)
                    {
                        return Result(
                            "blocked",
                            "wait",
                            clicks,
                            $"Visual Agent chờ quá lâu mà trạng thái không tiến triển: {decision.Reason}",
                            startedAt);
                    }

                    await Task.Delay(1200, cancellationToken);
                    continue;
                }

                consecutiveWaits = 0;
                var minimumConfidence =
                    decision.Action == "play" ? 0.72 : 0.80;
                if (decision.Confidence < minimumConfidence)
                {
                    return Result(
                        "blocked",
                        decision.Action,
                        clicks,
                        $"Vision chưa đủ chắc chắn ({decision.Confidence:0.00}, yêu cầu {minimumConfidence:0.00}): {decision.Reason}",
                        startedAt);
                }

                if (!IsSafeClick(frame.Value, decision))
                {
                    return Result(
                        "blocked",
                        decision.Action,
                        clicks,
                        "Visual Agent trả tọa độ click không an toàn hoặc nằm sát mép cửa sổ.",
                        startedAt);
                }

                var fingerprint = BuildDecisionFingerprint(decision);
                if (string.Equals(
                    fingerprint,
                    lastDecisionFingerprint,
                    StringComparison.Ordinal))
                {
                    repeatedDecisionCount++;
                }
                else
                {
                    repeatedDecisionCount = 0;
                    lastDecisionFingerprint = fingerprint;
                }

                if (repeatedDecisionCount >= 2)
                {
                    return Result(
                        "blocked",
                        decision.Action,
                        clicks,
                        "Visual Agent lặp lại cùng một thao tác mà giao diện không thay đổi; đã dừng để tránh click vòng lặp.",
                        startedAt);
                }

                var screenX = checked(frame.Value.Left + decision.ImageX);
                var screenY = checked(frame.Value.Top + decision.ImageY);
                computer.ClickLeft(
                    window.WindowId,
                    screenX,
                    screenY);

                clicks++;
                lastAction = decision.Action;

                await Task.Delay(
                    GetPostActionDelay(decision.Action),
                    cancellationToken);
            }

            return Result(
                "blocked",
                lastAction ?? "observe",
                clicks,
                "Visual Agent đã chạm giới hạn 16 lần quan sát trước khi hoàn tất.",
                startedAt);
        }
        catch (OperationCanceledException)
        {
            control.Stop();
            throw;
        }
        catch (Exception ex) when (
            ex is ToolExecutionInputException or
            InvalidOperationException or
            HttpRequestException or
            TaskCanceledException)
        {
            return Result(
                "blocked",
                lastAction ?? "observe",
                clicks,
                ex.Message,
                startedAt);
        }
        finally
        {
            control.Stop();
        }
    }

    private async Task EnsureLeagueClientAsync(
        CancellationToken cancellationToken)
    {
        if (FindClientWindow() is not null)
            return;

        var candidates = new[]
        {
            Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.CommonApplicationData),
                "Riot Games",
                "Metadata",
                "league_of_legends.live",
                "league_of_legends.live.product_settings.yaml"),
            @"C:\Riot Games\League of Legends\LeagueClient.exe",
            Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.ProgramFiles),
                "Riot Games",
                "League of Legends",
                "LeagueClient.exe"),
            Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.ProgramFilesX86),
                "Riot Games",
                "League of Legends",
                "LeagueClient.exe")
        };

        var executable = candidates
            .Where(path =>
                path.EndsWith(
                    ".exe",
                    StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault(File.Exists);

        var riotClient = new[]
        {
            @"C:\Riot Games\Riot Client\RiotClientServices.exe",
            Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.ProgramFiles),
                "Riot Games",
                "Riot Client",
                "RiotClientServices.exe"),
            Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.ProgramFilesX86),
                "Riot Games",
                "Riot Client",
                "RiotClientServices.exe")
        }.FirstOrDefault(File.Exists);

        if (riotClient is not null)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = riotClient,
                Arguments =
                    "--launch-product=league_of_legends --launch-patchline=live",
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(riotClient)
                    ?? Environment.CurrentDirectory
            });
        }
        else if (executable is not null)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = executable,
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(executable)
                    ?? Environment.CurrentDirectory
            });
        }
        else
        {
            throw new ToolExecutionInputException(
                "Không tìm thấy LeagueClient.exe hoặc RiotClientServices.exe ở các thư mục Riot mặc định.");
        }

        var launchedWindow = await WaitForClientWindowAsync(
            TimeSpan.FromSeconds(75),
            cancellationToken);
        if (launchedWindow is not null)
            return;

        throw new ToolExecutionInputException(
            "Đã mở Riot/League nhưng không thấy cửa sổ client hiển thị trong 75 giây.");
    }

    private async Task<ComputerWindowInfo?> WaitForClientWindowAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var window = FindClientWindow();
            if (window is not null)
                return window;
            await Task.Delay(750, cancellationToken);
        }

        return null;
    }

    private ComputerWindowInfo? FindClientWindow()
    {
        var windows = computer.GetWindows(50).Windows;
        return windows
            .Where(window =>
            {
                var processName = window.ProcessName ?? string.Empty;
                var title = window.Title ?? string.Empty;

                var processMatch =
                    ClientProcesses.Contains(
                        processName,
                        StringComparer.OrdinalIgnoreCase) ||
                    processName.StartsWith(
                        "RiotClient",
                        StringComparison.OrdinalIgnoreCase) ||
                    processName.StartsWith(
                        "LeagueClient",
                        StringComparison.OrdinalIgnoreCase);

                var titleMatch =
                    title.Contains(
                        "Riot Client",
                        StringComparison.OrdinalIgnoreCase) ||
                    title.Contains(
                        "League of Legends",
                        StringComparison.OrdinalIgnoreCase);

                return window.Width >= 200 &&
                    window.Height >= 150 &&
                    (processMatch || titleMatch);
            })
            .OrderByDescending(window => window.IsForeground)
            .ThenByDescending(window => window.Width * window.Height)
            .FirstOrDefault();
    }

    private static bool IsSafeClick(
        DesktopScreenshotFrame frame,
        LeagueVisualDecision decision)
    {
        if (frame.Width <= 0 || frame.Height <= 0)
            return false;

        const int edgeMargin = 6;
        return decision.ImageX >= edgeMargin &&
            decision.ImageX < frame.Width - edgeMargin &&
            decision.ImageY >= edgeMargin &&
            decision.ImageY < frame.Height - edgeMargin;
    }

    private static string BuildDecisionFingerprint(
        LeagueVisualDecision decision)
    {
        var bucketX = decision.ImageX / 24;
        var bucketY = decision.ImageY / 24;
        return $"{decision.Action}:{bucketX}:{bucketY}";
    }

    private static int GetPostActionDelay(string action) =>
        action switch
        {
            "play" => 1800,
            "training" => 1400,
            "practice-tool" => 1300,
            "confirm" => 2200,
            "start-game" => 3000,
            _ => 1200
        };

    private FrameLease CaptureDisposable(
        ComputerWindowInfo window)
    {
        if (window.Width < 200 || window.Height < 150)
            throw new ToolExecutionInputException(
                "Cửa sổ Riot/League quá nhỏ để Desktop Vision phân tích.");

        return new FrameLease(
            screenshots.CaptureRegion(
                window.Left,
                window.Top,
                window.Width,
                window.Height));
    }

    private static LeaguePracticeAutomationResult Result(
        string status,
        string phase,
        int steps,
        string detail,
        DateTimeOffset startedAt) =>
        new(
            status,
            phase,
            steps,
            detail,
            startedAt,
            DateTimeOffset.UtcNow);

    private sealed class FrameLease(
        DesktopScreenshotFrame value) : IDisposable
    {
        public DesktopScreenshotFrame Value { get; } = value;

        public void Dispose() => Value.Clear();
    }
}
