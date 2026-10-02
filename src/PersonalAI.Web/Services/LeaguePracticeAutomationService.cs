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

    private static readonly LeagueUiStep[] Steps =
    [
        new(
            "play",
            "nút vàng lớn có biểu tượng tam giác và chữ Chơi (Play) ở góc dưới bên trái của League client; chọn chính thân nút Chơi, không chọn nút mũi tên thả xuống bên phải",
            1800,
            0.72,
            new NormalizedRegion(0.03, 0.34, 0.68, 0.99)),
        new(
            "training",
            "mục Luyện tập hoặc Training trong màn hình chọn chế độ chơi của League of Legends",
            1400,
            0.82),
        new(
            "practice-tool",
            "mục Công cụ luyện tập hoặc Practice Tool trong nhóm Luyện tập/Training",
            1200,
            0.82),
        new(
            "confirm",
            "nút Xác nhận hoặc Confirm để tạo phòng Practice Tool",
            2200,
            0.84),
        new(
            "start-game",
            "nút Bắt đầu hoặc Start Game trong phòng Practice Tool",
            2500,
            0.84)
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
                "Cần cấu hình Gemini để Desktop Vision xác minh từng bước UI.",
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

        var completed = 0;
        try
        {
            while (completed < Steps.Length)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var window = await WaitForClientWindowAsync(
                    TimeSpan.FromSeconds(20),
                    cancellationToken);
                if (window is null)
                {
                    return Result(
                        "blocked",
                        Steps[completed].Phase,
                        completed,
                        "Riot/League đang chạy nhưng chưa có cửa sổ client hiển thị để xác minh.",
                        startedAt);
                }

                if (!window.IsForeground)
                {
                    computer.FocusWindow(window.WindowId);
                    await Task.Delay(700, cancellationToken);
                    window = await WaitForClientWindowAsync(
                        TimeSpan.FromSeconds(5),
                        cancellationToken);
                    if (window is null || !window.IsForeground)
                    {
                        return Result(
                            "blocked",
                            "focus",
                            completed,
                            "Không giữ được Riot/League client ở foreground.",
                            startedAt);
                    }
                }

                var step = Steps[completed];
                using var frame = CaptureDisposable(window);
                var target = await LocateStepTargetAsync(
                    frame.Value,
                    step,
                    cancellationToken);

                if (!target.Found ||
                    target.Confidence < step.MinimumConfidence ||
                    !IsInsideExpectedRegion(
                        frame.Value,
                        target,
                        step.ExpectedRegion))
                {
                    var regionDetail =
                        target.Found &&
                        !IsInsideExpectedRegion(
                            frame.Value,
                            target,
                            step.ExpectedRegion)
                            ? " Vision tìm thấy phần tử nhưng tọa độ nằm ngoài vùng UI dự kiến nên không click."
                            : string.Empty;

                    return Result(
                        "blocked",
                        step.Phase,
                        completed,
                        target.Found
                            ? $"Vision chưa đủ chắc chắn ({target.Confidence:0.00}, yêu cầu {step.MinimumConfidence:0.00}): {target.Reason}{regionDetail}"
                            : $"Không tìm thấy phần tử cần thiết: {target.Reason}",
                        startedAt);
                }

                var screenX = checked(frame.Value.Left + target.ImageX);
                var screenY = checked(frame.Value.Top + target.ImageY);
                computer.ClickLeft(
                    window.WindowId,
                    screenX,
                    screenY);
                completed++;

                await Task.Delay(step.DelayMs, cancellationToken);
            }

            return Result(
                "completed",
                "practice-tool",
                completed,
                "Đã hoàn tất chuỗi thao tác mở Practice Tool.",
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
            control.Stop();
            return Result(
                "blocked",
                completed < Steps.Length
                    ? Steps[completed].Phase
                    : "verify",
                completed,
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

    private async Task<DesktopVisionTarget> LocateStepTargetAsync(
        DesktopScreenshotFrame frame,
        LeagueUiStep step,
        CancellationToken cancellationToken)
    {
        DesktopVisionTarget? last = null;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            last = await vision.LocateAsync(
                frame,
                step.Target,
                cancellationToken);

            if (last.Found &&
                last.Confidence >= step.MinimumConfidence &&
                IsInsideExpectedRegion(
                    frame,
                    last,
                    step.ExpectedRegion))
            {
                return last;
            }

            if (attempt < 2)
                await Task.Delay(650, cancellationToken);
        }

        return last ?? new DesktopVisionTarget(
            false,
            step.Phase,
            0,
            0,
            0,
            "Desktop Vision không trả về mục tiêu.");
    }

    private static bool IsInsideExpectedRegion(
        DesktopScreenshotFrame frame,
        DesktopVisionTarget target,
        NormalizedRegion? region)
    {
        if (region is null)
            return true;

        if (frame.Width <= 0 || frame.Height <= 0)
            return false;

        var normalizedX = target.ImageX / (double)frame.Width;
        var normalizedY = target.ImageY / (double)frame.Height;
        return normalizedX >= region.MinimumX &&
            normalizedX <= region.MaximumX &&
            normalizedY >= region.MinimumY &&
            normalizedY <= region.MaximumY;
    }

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

    private sealed record LeagueUiStep(
        string Phase,
        string Target,
        int DelayMs,
        double MinimumConfidence,
        NormalizedRegion? ExpectedRegion = null);

    private sealed record NormalizedRegion(
        double MinimumX,
        double MaximumX,
        double MinimumY,
        double MaximumY);

    private sealed class FrameLease(
        DesktopScreenshotFrame value) : IDisposable
    {
        public DesktopScreenshotFrame Value { get; } = value;

        public void Dispose() => Value.Clear();
    }
}
