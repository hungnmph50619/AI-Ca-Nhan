using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public static class UniversalCapabilityReliability
{
    public const string Deterministic = "deterministic";
    public const string Structured = "structured";
    public const string Fallback = "fallback";
    public const string Semantic = "semantic";
}

public sealed record UniversalCapabilitySignal(
    string Key,
    string Channel,
    string DisplayName,
    bool Available,
    string Reliability,
    double ConfidenceBonus,
    int Priority,
    string Reason);

public sealed record UniversalCapabilitySnapshot(
    string Version,
    DateTimeOffset GeneratedAtUtc,
    IReadOnlyList<UniversalCapabilitySignal> Signals)
{
    public IReadOnlyList<UniversalCapabilitySignal> ForChannel(
        string channel) =>
        Signals
            .Where(item =>
                item.Channel.Equals(
                    channel,
                    StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(item =>
                item.Available)
            .ThenBy(item =>
                item.Priority)
            .ToArray();

    public double ConfidenceBonusFor(
        string channel) =>
        Math.Clamp(
            ForChannel(channel)
                .Where(item => item.Available)
                .Sum(item => item.ConfidenceBonus),
            0,
            0.08);
}

public interface IUniversalCapabilityDiscoveryService
{
    UniversalCapabilitySnapshot Discover();
}

public sealed class UniversalCapabilityDiscoveryService(
    IFlaUiAutomationClient flaUi,
    IPlaywrightBrowserAdapter playwright,
    IBrowserAgentService browser,
    IComputerUseService computer,
    DesktopVisionService vision)
    : IUniversalCapabilityDiscoveryService
{
    public UniversalCapabilitySnapshot Discover()
    {
        var computerStatus =
            computer.GetStatus();

        var browserStatus =
            browser.GetStatus();

        var signals =
            new List<UniversalCapabilitySignal>
            {
                new(
                    "desktop.flaui-uia3",
                    ExecutionAgentChannels.Computer,
                    "FlaUI / UI Automation 3",
                    flaUi.Available,
                    UniversalCapabilityReliability.Structured,
                    flaUi.Available ? 0.04 : 0,
                    Priority: 1,
                    flaUi.Available
                        ? "Có sidecar FlaUI UIA3 để đọc/ghi UI có cấu trúc trên Windows."
                        : "FlaUI UIA3 chưa khả dụng; hệ thống sẽ dùng adapter Windows khác hoặc Vision."),

                new(
                    "desktop.win32-accessibility",
                    ExecutionAgentChannels.Computer,
                    "Win32 Accessibility",
                    computerStatus.Supported &&
                    computerStatus.InteractiveSession,
                    UniversalCapabilityReliability.Deterministic,
                    computerStatus.Supported &&
                    computerStatus.InteractiveSession
                        ? 0.02
                        : 0,
                    Priority: 2,
                    computerStatus.Supported &&
                    computerStatus.InteractiveSession
                        ? "Phiên Windows tương tác cho phép kiểm tra focus/window/input bằng Win32."
                        : "Không có phiên Windows tương tác để dùng Win32 Computer Use."),

                new(
                    "desktop.gemini-vision",
                    ExecutionAgentChannels.Computer,
                    "Desktop Vision / Gemini",
                    vision.Ready,
                    UniversalCapabilityReliability.Semantic,
                    vision.Ready ? 0.01 : 0,
                    Priority: 4,
                    vision.Ready
                        ? "Desktop Vision sẵn sàng làm semantic fallback khi structured evidence chưa đủ."
                        : "Desktop Vision chưa được cấu hình."),

                new(
                    "browser.playwright-dom",
                    ExecutionAgentChannels.Browser,
                    "Playwright + Microsoft Edge",
                    playwright.PreferredRuntimeAvailable,
                    UniversalCapabilityReliability.Structured,
                    playwright.PreferredRuntimeAvailable
                        ? 0.05
                        : 0,
                    Priority: 1,
                    playwright.PreferredRuntimeAvailable
                        ? "Có thể dùng Playwright để đọc DOM sau JavaScript bằng Microsoft Edge."
                        : "Playwright/Edge chưa ở runtime ưu tiên hiện tại."),

                new(
                    "browser.http-html",
                    ExecutionAgentChannels.Browser,
                    "Browser Agent HTTP/HTML",
                    browserStatus.Supported,
                    UniversalCapabilityReliability.Fallback,
                    browserStatus.Supported ? 0.02 : 0,
                    Priority: 2,
                    browserStatus.Supported
                        ? "Browser Agent HTTP/HTML sẵn sàng làm đường structured fallback an toàn."
                        : "Browser Agent HTTP/HTML chưa khả dụng.")
            };

        return new(
            PersonalAiRelease.Version,
            DateTimeOffset.UtcNow,
            signals);
    }
}
