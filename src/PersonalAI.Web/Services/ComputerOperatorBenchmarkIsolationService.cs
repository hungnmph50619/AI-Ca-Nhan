using System.Runtime.InteropServices;
using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public sealed record ComputerOperatorBenchmarkIsolationResult(
    bool Ready,
    string Detail,
    int ClosedDisposableWindows,
    int ProtectedWindows);

public interface IComputerOperatorBenchmarkIsolationService
{
    Task<ComputerOperatorBenchmarkIsolationResult> PrepareAsync(
        string scenarioId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Benchmark-only desktop hygiene. This service never participates in the
/// production Operator loop. It may close disposable benchmark applications,
/// but it must not destroy an unrelated editor/document window.
/// </summary>
public sealed class ComputerOperatorBenchmarkIsolationService(
    IComputerUseService computer,
    ILogger<ComputerOperatorBenchmarkIsolationService> logger)
    : IComputerOperatorBenchmarkIsolationService
{
    private const uint WmClose = 0x0010;
    private const string BenchmarkTextMarker =
        "PersonalAI benchmark text entry";

    public async Task<ComputerOperatorBenchmarkIsolationResult> PrepareAsync(
        string scenarioId,
        CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows() ||
            !Environment.UserInteractive)
        {
            return new(
                false,
                "Benchmark isolation cần Windows interactive desktop.",
                0,
                0);
        }

        var id =
            (scenarioId ?? string.Empty)
                .Trim()
                .ToLowerInvariant();

        var requiresNotepad =
            id is
                "windows-notepad-text-entry" or
                "windows-window-focus-switch";

        var requiresCalculator =
            id is
                "windows-calculator-arithmetic" or
                "windows-search-native-launch" or
                "windows-window-focus-switch";

        var requiresSettings =
            id is
                "windows-settings-navigation" or
                "windows-delayed-native-launch";

        var windows =
            computer.GetWindows(50).Windows;

        var protectedWindows =
            windows
                .Where(window =>
                    requiresNotepad &&
                    IsNotepad(window) &&
                    !IsDisposableBenchmarkNotepad(window))
                .ToArray();

        if (protectedWindows.Length > 0)
        {
            return new(
                false,
                "Đang có Notepad không thuộc benchmark. Hệ thống không tự đóng hoặc ghi đè để tránh mất dữ liệu người dùng.",
                0,
                protectedWindows.Length);
        }

        var disposable =
            windows
                .Where(window =>
                    (requiresNotepad &&
                     IsDisposableBenchmarkNotepad(window)) ||
                    (requiresCalculator &&
                     IsCalculator(window)) ||
                    (requiresSettings &&
                     IsSettings(window)) ||
                    IsWindowsSearch(window))
                .DistinctBy(window => window.WindowId)
                .ToArray();

        var closeCount = 0;

        foreach (var window in disposable)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!TryCloseWindow(window.WindowId))
                continue;

            closeCount++;
        }

        if (closeCount > 0)
        {
            await Task.Delay(
                650,
                cancellationToken);
        }

        var remaining =
            computer.GetWindows(50).Windows;

        var unsafeRemaining =
            remaining
                .Where(window =>
                    requiresNotepad &&
                    IsNotepad(window) &&
                    !IsDisposableBenchmarkNotepad(window))
                .ToArray();

        if (unsafeRemaining.Length > 0)
        {
            return new(
                false,
                "Sau reset vẫn còn Notepad không thuộc benchmark; dừng trước khi Operator có thể ghi nhầm dữ liệu.",
                closeCount,
                unsafeRemaining.Length);
        }

        // A stale benchmark Notepad can show a Save prompt after WM_CLOSE.
        // It is disposable, but we do not force-kill the process because a
        // modern Notepad process may contain other tabs. The per-run unique
        // payload prevents stale content from satisfying the new case.
        var staleBenchmarkEditors =
            remaining.Count(window =>
                requiresNotepad &&
                IsDisposableBenchmarkNotepad(window));

        logger.LogInformation(
            "Benchmark isolation ready. Scenario={Scenario}; requestedClose={CloseCount}; staleBenchmarkEditors={StaleCount}.",
            id,
            closeCount,
            staleBenchmarkEditors);

        return new(
            true,
            staleBenchmarkEditors > 0
                ? "Precondition an toàn; còn editor benchmark cũ nhưng payload mới có run-token riêng nên không thể tạo false-positive."
                : "Precondition desktop sạch cho benchmark.",
            closeCount,
            0);
    }

    internal static bool IsProtectedNotepadForAcceptance(
        string? processName,
        string? title) =>
        IsNotepad(
            new(
                "acceptance",
                title ?? string.Empty,
                processName,
                null,
                false,
                0,
                0,
                100,
                100)) &&
        !IsDisposableBenchmarkNotepad(
            new(
                "acceptance",
                title ?? string.Empty,
                processName,
                null,
                false,
                0,
                0,
                100,
                100));

    private static bool IsDisposableBenchmarkNotepad(
        ComputerWindowInfo window) =>
        IsNotepad(window) &&
        (window.Title ?? string.Empty)
            .Contains(
                BenchmarkTextMarker,
                StringComparison.OrdinalIgnoreCase);

    private static bool IsNotepad(
        ComputerWindowInfo window) =>
        EqualsProcess(
            window,
            "notepad") ||
        (window.Title ?? string.Empty)
            .Contains(
                "Notepad",
                StringComparison.OrdinalIgnoreCase);

    private static bool IsCalculator(
        ComputerWindowInfo window) =>
        EqualsProcess(
            window,
            "calculatorapp",
            "calculator") ||
        (window.Title ?? string.Empty)
            .Equals(
                "Calculator",
                StringComparison.OrdinalIgnoreCase);

    private static bool IsSettings(
        ComputerWindowInfo window) =>
        EqualsProcess(
            window,
            "systemsettings") ||
        (window.Title ?? string.Empty)
            .Equals(
                "Settings",
                StringComparison.OrdinalIgnoreCase);

    private static bool IsWindowsSearch(
        ComputerWindowInfo window) =>
        EqualsProcess(
            window,
            "searchhost",
            "searchapp") ||
        (window.Title ?? string.Empty)
            .Equals(
                "Search",
                StringComparison.OrdinalIgnoreCase);

    private static bool EqualsProcess(
        ComputerWindowInfo window,
        params string[] names)
    {
        var process =
            (window.ProcessName ?? string.Empty)
                .Trim();

        return names.Any(name =>
            process.Equals(
                name,
                StringComparison.OrdinalIgnoreCase));
    }

    private static bool TryCloseWindow(
        string windowId)
    {
        if (!TryParseWindowHandle(
                windowId,
                out var handle))
        {
            return false;
        }

        if (!IsWindow(handle))
            return false;

        return PostMessage(
            handle,
            WmClose,
            IntPtr.Zero,
            IntPtr.Zero);
    }

    private static bool TryParseWindowHandle(
        string value,
        out IntPtr handle)
    {
        handle = IntPtr.Zero;

        var normalized =
            (value ?? string.Empty)
                .Trim();

        if (normalized.StartsWith(
                "0x",
                StringComparison.OrdinalIgnoreCase))
        {
            normalized =
                normalized[2..];
        }

        if (!long.TryParse(
                normalized,
                System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture,
                out var raw))
        {
            return false;
        }

        handle =
            new IntPtr(raw);

        return handle != IntPtr.Zero;
    }

    [DllImport(
        "user32.dll",
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(
        IntPtr hWnd,
        uint msg,
        IntPtr wParam,
        IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(
        IntPtr hWnd);
}
