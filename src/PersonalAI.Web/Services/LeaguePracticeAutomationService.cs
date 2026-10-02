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
        "LeagueClient",
        "RiotClientServices"
    ];

    private static readonly (string Phase, string Target, int DelayMs)[] Steps =
    [
        ("play",
            "nút Chơi hoặc Play dùng để bắt đầu chọn chế độ chơi trong Riot/League client",
            1800),
        ("training",
            "mục Luyện tập hoặc Training trong màn hình chọn chế độ",
            1400),
        ("practice-tool",
            "mục Công cụ luyện tập hoặc Practice Tool",
            1200),
        ("confirm",
            "nút Xác nhận hoặc Confirm để tạo phòng Practice Tool",
            2200),
        ("start-game",
            "nút Bắt đầu hoặc Start Game trong phòng Practice Tool",
            2500)
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
            maximumSeconds: 240);

        var completed = 0;
        try
        {
            for (var attempt = 0; attempt < 12; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var window = FindClientWindow();
                if (window is null)
                {
                    await Task.Delay(1000, cancellationToken);
                    continue;
                }

                if (!window.IsForeground)
                {
                    computer.FocusWindow(window.WindowId);
                    await Task.Delay(500, cancellationToken);
                    window = FindClientWindow();
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

                if (completed >= Steps.Length)
                {
                    return Result(
                        "completed",
                        "practice-tool",
                        completed,
                        "Đã hoàn tất chuỗi thao tác mở Practice Tool.",
                        startedAt);
                }

                var step = Steps[completed];
                using var frame = CaptureDisposable();
                var target = await vision.LocateAsync(
                    frame.Value,
                    step.Target,
                    cancellationToken);

                if (!target.Found || target.Confidence < 0.86)
                {
                    return Result(
                        "blocked",
                        step.Phase,
                        completed,
                        target.Found
                            ? $"Vision chưa đủ chắc chắn ({target.Confidence:0.00}): {target.Reason}"
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
                "blocked",
                completed < Steps.Length
                    ? Steps[completed].Phase
                    : "verify",
                completed,
                "Đã chạm giới hạn vòng lặp trước khi xác minh hoàn tất.",
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
        if (FindClientWindow() is not null ||
            Process.GetProcesses()
                .Any(process =>
                {
                    try
                    {
                        return ClientProcesses.Contains(
                            process.ProcessName,
                            StringComparer.OrdinalIgnoreCase);
                    }
                    catch
                    {
                        return false;
                    }
                }))
        {
            return;
        }

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

        if (executable is null)
        {
            var riotClient = new[]
            {
                @"C:\Riot Games\Riot Client\RiotClientServices.exe",
                Path.Combine(
                    Environment.GetFolderPath(
                        Environment.SpecialFolder.ProgramFiles),
                    "Riot Games",
                    "Riot Client",
                    "RiotClientServices.exe")
            }.FirstOrDefault(File.Exists);

            if (riotClient is null)
                throw new ToolExecutionInputException(
                    "Không tìm thấy LeagueClient.exe hoặc RiotClientServices.exe ở các thư mục Riot mặc định.");

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
        else
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = executable,
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(executable)
                    ?? Environment.CurrentDirectory
            });
        }

        for (var i = 0; i < 45; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (FindClientWindow() is not null)
                return;
            await Task.Delay(1000, cancellationToken);
        }

        throw new ToolExecutionInputException(
            "Đã mở Riot/League nhưng không thấy cửa sổ client trong 45 giây.");
    }

    private ComputerWindowInfo? FindClientWindow()
    {
        var windows = computer.GetWindows(50).Windows;
        return windows
            .Where(window =>
                window.ProcessName is not null &&
                ClientProcesses.Contains(
                    window.ProcessName,
                    StringComparer.OrdinalIgnoreCase))
            .OrderByDescending(window => window.IsForeground)
            .ThenByDescending(window => window.Width * window.Height)
            .FirstOrDefault();
    }

    private FrameLease CaptureDisposable() =>
        new(screenshots.CaptureVirtualScreen());

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
