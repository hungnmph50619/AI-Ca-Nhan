using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public static class DesktopCaptureScopes
{
    public const string VirtualDesktop = "virtual-desktop";
    public const string Monitor = "monitor";
    public const string Window = "window";
    public const string Region = "region";
}

public static class DesktopCaptureBackends
{
    public const string WindowsGraphicsCapture = "windows-graphics-capture";
    public const string DxgiDesktopDuplication = "dxgi-desktop-duplication";
    public const string PrintWindow = "print-window";
    public const string CopyFromScreen = "copy-from-screen";
}

public sealed record DesktopCaptureBackendStatus(
    string Name,
    int Priority,
    bool Available,
    IReadOnlyList<string> Scopes,
    string Detail);

public sealed record DesktopCaptureBackendRouterSnapshot(
    string Version,
    IReadOnlyList<DesktopCaptureBackendStatus> Backends,
    IReadOnlyDictionary<string, string> PreferredBackendByScope);

public interface IDesktopCaptureBackendRouter
{
    IReadOnlyList<string> GetOrderedCandidates(
        string scope,
        string? target = null);

    string GetPreferredAvailableBackend(
        string scope,
        string? target = null);

    DesktopCaptureBackendRouterSnapshot GetSnapshot();
}

public sealed class DesktopCaptureBackendRouter(
    IWindowsGraphicsCaptureClient? wgc = null,
    IDxgiDesktopDuplicationClient? dxgi = null,
    IDesktopCaptureHealthTracker? health = null)
    : IDesktopCaptureBackendRouter
{
    private IReadOnlyList<DesktopCaptureBackendStatus> Backends =>
    [
        new(
            DesktopCaptureBackends.WindowsGraphicsCapture,
            1,
            Available: wgc?.Available == true,
            [DesktopCaptureScopes.Window, DesktopCaptureScopes.Monitor],
            wgc?.Available == true
                ? "Windows Graphics Capture sẵn sàng cho window và monitor capture."
                : "Windows Graphics Capture chưa khả dụng; router sẽ dùng backend dự phòng."),
        new(
            DesktopCaptureBackends.DxgiDesktopDuplication,
            2,
            Available: dxgi?.Available == true,
            [DesktopCaptureScopes.VirtualDesktop, DesktopCaptureScopes.Monitor],
            dxgi?.Available == true
                ? "DXGI Desktop Duplication sẵn sàng cho virtual desktop và fallback monitor capture."
                : "DXGI Desktop Duplication chưa khả dụng; router sẽ dùng backend dự phòng."),
        new(
            DesktopCaptureBackends.PrintWindow,
            3,
            Available: true,
            [DesktopCaptureScopes.Window],
            "Backend Win32 PrintWindow hiện có; chỉ dùng khi cửa sổ trả frame hữu dụng."),
        new(
            DesktopCaptureBackends.CopyFromScreen,
            4,
            Available: true,
            [
                DesktopCaptureScopes.VirtualDesktop,
                DesktopCaptureScopes.Monitor,
                DesktopCaptureScopes.Window,
                DesktopCaptureScopes.Region
            ],
            "Backend fallback ổn định hiện tại dựa trên Graphics.CopyFromScreen.")
    ];

    public IReadOnlyList<string> GetOrderedCandidates(
        string scope,
        string? target = null)
    {
        var normalized = NormalizeScope(scope);
        var normalizedTarget =
            string.IsNullOrWhiteSpace(target)
                ? "-"
                : target.Trim();

        return Backends
            .Where(item =>
                item.Scopes.Contains(
                    normalized,
                    StringComparer.OrdinalIgnoreCase))
            .OrderBy(item =>
                item.Priority +
                GetHealthPenalty(
                    normalized,
                    normalizedTarget,
                    item.Name))
            .ThenBy(item =>
                item.Priority)
            .Select(item => item.Name)
            .ToArray();
    }

    public string GetPreferredAvailableBackend(
        string scope,
        string? target = null)
    {
        var normalized = NormalizeScope(scope);
        var normalizedTarget =
            string.IsNullOrWhiteSpace(target)
                ? "-"
                : target.Trim();

        return Backends
            .Where(item =>
                item.Available &&
                item.Scopes.Contains(
                    normalized,
                    StringComparer.OrdinalIgnoreCase))
            .OrderBy(item =>
                item.Priority +
                GetHealthPenalty(
                    normalized,
                    normalizedTarget,
                    item.Name))
            .ThenBy(item =>
                item.Priority)
            .Select(item => item.Name)
            .FirstOrDefault()
            ?? throw new InvalidOperationException(
                $"Không có backend capture khả dụng cho scope {normalized}.");
    }

    public DesktopCaptureBackendRouterSnapshot GetSnapshot()
    {
        var scopes = new[]
        {
            DesktopCaptureScopes.VirtualDesktop,
            DesktopCaptureScopes.Monitor,
            DesktopCaptureScopes.Window,
            DesktopCaptureScopes.Region
        };

        return new(
            PersonalAiRelease.Version,
            Backends,
            scopes.ToDictionary(
                scope => scope,
                GetPreferredAvailableBackend,
                StringComparer.OrdinalIgnoreCase));
    }

    private int GetHealthPenalty(
        string scope,
        string target,
        string backend) =>
        health?.GetBackendPenalty(
            scope,
            target,
            backend) ?? 0;

    private static string NormalizeScope(string scope)
    {
        var normalized = (scope ?? string.Empty)
            .Trim()
            .ToLowerInvariant();

        return normalized switch
        {
            DesktopCaptureScopes.VirtualDesktop => normalized,
            DesktopCaptureScopes.Monitor => normalized,
            DesktopCaptureScopes.Window => normalized,
            DesktopCaptureScopes.Region => normalized,
            _ => throw new ArgumentOutOfRangeException(
                nameof(scope),
                scope,
                "Capture scope không được hỗ trợ.")
        };
    }
}
