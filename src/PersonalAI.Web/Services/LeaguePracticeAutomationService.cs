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
    ComputerControlGate control,
    LeagueVisualProgressStore progress)
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
        progress.Start("Đang khởi động Visual Agent cho Riot/League.");
        if (!OperatingSystem.IsWindows() || !Environment.UserInteractive)
        {
            progress.Block("Không thể chạy: chỉ hỗ trợ phiên Windows đang tương tác.");
            return Result(
                "blocked",
                "platform",
                0,
                "Chỉ hỗ trợ phiên Windows đang tương tác.",
                startedAt);
        }

        if (!vision.Ready)
        {
            progress.Block("Không thể chạy: Desktop Vision chưa sẵn sàng.");
            return Result(
                "blocked",
                "vision",
                0,
                "Cần cấu hình Gemini để Desktop Vision xác minh từng trạng thái UI.",
                startedAt);
        }

        try
        {
            await MinimizeForegroundBrowserIfNeededAsync(
                cancellationToken);

            progress.Add("launch", "Đang kiểm tra và mở Riot/League client.");
            await EnsureLeagueClientAsync(cancellationToken);

            var stableClient = await WaitForStableClientWindowAsync(
                TimeSpan.FromSeconds(20),
                cancellationToken);
            if (stableClient is null)
                throw new ToolExecutionInputException(
                    "League đã mở nhưng giao diện chưa ổn định để quan sát.");

            progress.Add(
                "stabilize",
                $"Đã thấy giao diện League ổn định tại {stableClient.Width}x{stableClient.Height}.");
        }
        catch (Exception ex) when (
            ex is ToolExecutionInputException or
            InvalidOperationException or
            System.ComponentModel.Win32Exception)
        {
            progress.Block($"Dừng ở bước mở client: {ex.Message}");
            return Result(
                "blocked",
                "launch",
                0,
                ex.Message,
                startedAt);
        }

        control.EnableScopedAutomation(
            maximumActions: 16,
            maximumSeconds: 170);

        var clicks = 0;
        var consecutiveWaits = 0;
        var repeatedDecisionCount = 0;
        var consecutiveVisionBlocks = 0;
        var consecutiveLowConfidence = 0;
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
                        progress.Complete("Đã nhấn Bắt đầu và client đã chuyển khỏi menu.");
                        return Result(
                            "completed",
                            "start-game",
                            clicks,
                            "Đã nhấn Bắt đầu và League client đã chuyển khỏi trạng thái menu hiển thị.",
                            startedAt);
                    }

                    progress.Block(
                        "Không còn thấy cửa sổ Riot/League để tiếp tục quan sát.");
                    return Result(
                        "blocked",
                        lastAction ?? "observe",
                        clicks,
                        "Không còn thấy cửa sổ Riot/League để tiếp tục quan sát.",
                        startedAt);
                }

                var activeClient = GetActiveClientWindow();
                if (activeClient is null ||
                    !string.Equals(
                        activeClient.WindowId,
                        window.WindowId,
                        StringComparison.OrdinalIgnoreCase))
                {
                    progress.Add(
                        "focus",
                        "Đang đưa đúng cửa sổ Riot/League lên foreground trước khi chụp.");

                    computer.FocusWindow(window.WindowId);
                    await Task.Delay(700, cancellationToken);

                    activeClient = GetActiveClientWindow();
                    if (activeClient is null)
                    {
                        progress.Block(
                            "Không xác minh được Riot/League là cửa sổ foreground; đã hủy chụp để tránh nhìn nhầm ứng dụng.");
                        return Result(
                            "blocked",
                            "focus",
                            clicks,
                            "Không xác minh được Riot/League ở foreground.",
                            startedAt);
                    }
                }

                window = activeClient;

                progress.Add(
                    "stabilize",
                    "Đang chờ cửa sổ League ổn định trước khi chụp.");

                var stableWindow = await WaitForStableClientWindowAsync(
                    TimeSpan.FromSeconds(8),
                    cancellationToken);
                if (stableWindow is null)
                {
                    progress.Block(
                        "Giao diện League tiếp tục thay đổi nên chưa chụp an toàn được.");
                    return Result(
                        "blocked",
                        "stabilize",
                        clicks,
                        "Cửa sổ League chưa ổn định trước khi chụp.",
                        startedAt);
                }

                window = stableWindow;

                var verifiedBeforeCapture = GetActiveClientWindow();
                if (verifiedBeforeCapture is null ||
                    !string.Equals(
                        verifiedBeforeCapture.WindowId,
                        window.WindowId,
                        StringComparison.OrdinalIgnoreCase))
                {
                    progress.Block(
                        "Foreground đã đổi ngay trước lúc chụp; đã hủy để không gửi ảnh ứng dụng khác cho Vision.");
                    return Result(
                        "blocked",
                        "focus",
                        clicks,
                        "Cửa sổ foreground đã thay đổi trước lúc chụp.",
                        startedAt);
                }

                using var frame = CaptureDisposable(verifiedBeforeCapture);
                progress.Add(
                    "capture",
                    $"Đã chụp vùng cửa sổ League {frame.Value.Width}x{frame.Value.Height} để phân tích.",
                    observation: true);

                var decision = await vision.DecideLeaguePracticeActionAsync(
                    frame.Value,
                    cancellationToken);

                progress.Add(
                    "vision",
                    $"Vision nhận định trạng thái: {decision.State}. Hành động tiếp theo: {decision.Action}. {decision.Reason}",
                    decision.Action,
                    decision.Confidence,
                    decision.ImageX,
                    decision.ImageY);

                if (decision.Action == "complete")
                {
                    progress.Complete($"Hoàn tất: {decision.Reason}");
                    return Result(
                        "completed",
                        "practice-tool",
                        clicks,
                        $"Visual Agent xác nhận mục tiêu đã hoàn tất: {decision.Reason}",
                        startedAt);
                }

                if (decision.Action == "blocked")
                {
                    consecutiveVisionBlocks++;
                    progress.Add(
                        "vision-retry",
                        $"Vision chưa xác định được giao diện ({consecutiveVisionBlocks}/3): {decision.Reason}",
                        "blocked",
                        decision.Confidence);

                    if (consecutiveVisionBlocks >= 3)
                    {
                        progress.Block(
                            $"Vision không xác định được giao diện sau 3 lần: {decision.Reason}");
                        return Result(
                            "blocked",
                            "vision",
                            clicks,
                            $"Visual Agent dừng sau 3 lần không xác định được giao diện: {decision.Reason}",
                            startedAt);
                    }

                    await Task.Delay(900, cancellationToken);
                    continue;
                }

                consecutiveVisionBlocks = 0;

                if (decision.Action == "wait")
                {
                    consecutiveWaits++;
                    lastAction = "wait";
                    if (consecutiveWaits >= 5)
                    {
                        progress.Block(
                            $"Visual Agent chờ quá lâu mà trạng thái không tiến triển: {decision.Reason}");
                        return Result(
                            "blocked",
                            "wait",
                            clicks,
                            $"Visual Agent chờ quá lâu mà trạng thái không tiến triển: {decision.Reason}",
                            startedAt);
                    }

                    progress.Add(
                        "wait",
                        $"Đang chờ giao diện thay đổi ({consecutiveWaits}/5): {decision.Reason}",
                        "wait",
                        decision.Confidence);
                    await Task.Delay(1200, cancellationToken);
                    continue;
                }

                consecutiveWaits = 0;
                var minimumConfidence =
                    decision.Action == "play" ? 0.72 : 0.80;
                if (decision.Confidence < minimumConfidence)
                {
                    consecutiveLowConfidence++;
                    progress.Add(
                        "vision-retry",
                        $"Độ tin cậy thấp ({consecutiveLowConfidence}/3): {decision.Action} {decision.Confidence:0.00} < {minimumConfidence:0.00}. {decision.Reason}",
                        decision.Action,
                        decision.Confidence,
                        decision.ImageX,
                        decision.ImageY);

                    if (consecutiveLowConfidence >= 3)
                    {
                        progress.Block(
                            $"Dừng sau 3 lần Vision chưa đủ chắc chắn: {decision.Action} {decision.Confidence:0.00}.");
                        return Result(
                            "blocked",
                            decision.Action,
                            clicks,
                            $"Vision chưa đủ chắc chắn sau 3 lần ({decision.Confidence:0.00}, yêu cầu {minimumConfidence:0.00}): {decision.Reason}",
                            startedAt);
                    }

                    await Task.Delay(900, cancellationToken);
                    continue;
                }

                consecutiveLowConfidence = 0;

                if (!IsSafeClick(frame.Value, decision))
                {
                    progress.Block("Dừng vì tọa độ click không an toàn.");
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
                    progress.Block("Dừng vì Visual Agent lặp lại cùng thao tác mà giao diện không thay đổi.");
                    return Result(
                        "blocked",
                        decision.Action,
                        clicks,
                        "Visual Agent lặp lại cùng một thao tác mà giao diện không thay đổi; đã dừng để tránh click vòng lặp.",
                        startedAt);
                }

                var screenX = checked(frame.Value.Left + decision.ImageX);
                var screenY = checked(frame.Value.Top + decision.ImageY);

                progress.Add(
                    "pointer",
                    $"Đang đưa con trỏ tới mục tiêu {decision.Label} tại ({screenX}, {screenY}).",
                    decision.Action,
                    decision.Confidence,
                    screenX,
                    screenY);

                computer.SmoothMoveCursor(
                    screenX,
                    screenY,
                    durationMs: 420);

                progress.Add(
                    "click",
                    $"Đang nhấp {decision.Label}.",
                    decision.Action,
                    decision.Confidence,
                    screenX,
                    screenY,
                    click: true);

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

            progress.Block("Visual Agent đã chạm giới hạn 16 lần quan sát.");
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
            progress.Block($"Visual Agent gặp lỗi: {ex.Message}");
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

    private async Task MinimizeForegroundBrowserIfNeededAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var active = computer.GetActiveWindow();
        if (active is null || IsLeagueClientWindow(active))
            return;

        var processName = active.ProcessName ?? string.Empty;
        var isBrowser =
            processName.Equals("msedge", StringComparison.OrdinalIgnoreCase) ||
            processName.Equals("chrome", StringComparison.OrdinalIgnoreCase) ||
            processName.Equals("firefox", StringComparison.OrdinalIgnoreCase) ||
            processName.Equals("brave", StringComparison.OrdinalIgnoreCase) ||
            processName.Equals("opera", StringComparison.OrdinalIgnoreCase);

        if (!isBrowser)
            return;

        progress.Add(
            "window",
            $"Đang thu nhỏ trình duyệt {active.Title} để chuyển sang League.");

        computer.MinimizeWindow(active.WindowId);
        await Task.Delay(500, cancellationToken);
    }

    private async Task<ComputerWindowInfo?> WaitForStableClientWindowAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        ComputerWindowInfo? previous = null;
        var stableSamples = 0;

        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var current = GetActiveClientWindow() ?? FindClientWindow();
            if (current is null)
            {
                stableSamples = 0;
                previous = null;
                await Task.Delay(500, cancellationToken);
                continue;
            }

            if (previous is not null &&
                string.Equals(
                    current.WindowId,
                    previous.WindowId,
                    StringComparison.OrdinalIgnoreCase) &&
                Math.Abs(current.Left - previous.Left) <= 2 &&
                Math.Abs(current.Top - previous.Top) <= 2 &&
                Math.Abs(current.Width - previous.Width) <= 2 &&
                Math.Abs(current.Height - previous.Height) <= 2)
            {
                stableSamples++;
            }
            else
            {
                stableSamples = 0;
            }

            if (stableSamples >= 2)
                return current;

            previous = current;
            await Task.Delay(500, cancellationToken);
        }

        return null;
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
            .Where(IsLeagueClientWindow)
            .OrderByDescending(window => window.IsForeground)
            .ThenByDescending(window => window.Width * window.Height)
            .FirstOrDefault();
    }

    private ComputerWindowInfo? GetActiveClientWindow()
    {
        var active = computer.GetActiveWindow();
        return active is not null && IsLeagueClientWindow(active)
            ? active
            : null;
    }

    private static bool IsLeagueClientWindow(
        ComputerWindowInfo window)
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
