using System.Security.Cryptography;
using System.Text;
using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public interface IComputerOperatorSceneIdentityService
{
    string BuildFingerprint(
        ComputerOperatorDesktopState state,
        DesktopPerceptualHash? visualHash = null);
}

public sealed class ComputerOperatorSceneIdentityService
    : IComputerOperatorSceneIdentityService
{
    public string BuildFingerprint(
        ComputerOperatorDesktopState state,
        DesktopPerceptualHash? visualHash = null) =>
        BuildFingerprintCore(
            state,
            visualHash);

    internal static string BuildFingerprintForAcceptance(
        ComputerOperatorDesktopState state,
        DesktopPerceptualHash? visualHash = null) =>
        BuildFingerprintCore(
            state,
            visualHash);

    private static string BuildFingerprintCore(
        ComputerOperatorDesktopState state,
        DesktopPerceptualHash? visualHash)
    {
        var foreground = state.ForegroundWindow is null
            ? "foreground:none"
            : $"foreground:{NormalizeSceneToken(state.ForegroundWindow.ProcessName)}|{NormalizeSceneToken(state.ForegroundWindow.Title)}|{Quantize(state.ForegroundWindow.Left, 32)},{Quantize(state.ForegroundWindow.Top, 32)},{Quantize(state.ForegroundWindow.Width, 32)},{Quantize(state.ForegroundWindow.Height, 32)}";

        var visible = state.Windows
            .Where(window => window.Width > 0 && window.Height > 0)
            .OrderBy(
                window => window.ProcessName ?? string.Empty,
                StringComparer.OrdinalIgnoreCase)
            .ThenBy(
                window => window.Title,
                StringComparer.OrdinalIgnoreCase)
            .Take(16)
            .Select(window =>
                $"{NormalizeSceneToken(window.ProcessName)}:{NormalizeSceneToken(window.Title)}:{Quantize(window.Left, 64)},{Quantize(window.Top, 64)},{Quantize(window.Width, 64)},{Quantize(window.Height, 64)}");

        var structured = state.StructuredScene is null
            ? "structured:none"
            : $"structured:{string.Join(";", state.StructuredScene.Nodes
                .Where(node => !node.IsOffscreen)
                .Take(24)
                .Select(node =>
                {
                    var valueFingerprint =
                        node.IsSensitive
                            ? "sensitive"
                            : node.Value is null
                                ? "-"
                                : BuildDiagnosticId(
                                    node.Value);

                    return
                        $"{NormalizeSceneToken(node.Role)}:{NormalizeSceneToken(node.Name)}:{NormalizeSceneToken(node.AutomationId)}:{Quantize(node.Left, 32)},{Quantize(node.Top, 32)},{Quantize(node.Width, 32)},{Quantize(node.Height, 32)}:toggle={NormalizeSceneToken(node.ToggleState)}:expand={NormalizeSceneToken(node.ExpandCollapseState)}:selected={node.IsSelected?.ToString() ?? "?"}:value={valueFingerprint}:patterns={string.Join(",", node.Patterns.OrderBy(pattern => pattern, StringComparer.OrdinalIgnoreCase))}";
                }))}";

        var material =
            string.Join(
                "|",
                new[]
                {
                    foreground,
                    $"frame:{state.CaptureScope}:{Quantize(state.FrameLeft, 32)},{Quantize(state.FrameTop, 32)},{Quantize(state.FrameWidth, 32)},{Quantize(state.FrameHeight, 32)}",
                    $"windows:{string.Join(";", visible)}",
                    structured,
                    visualHash is null
                        ? "visual:none"
                        : $"visual:{visualHash.Algorithm}:{visualHash.Hex}"
                });

        return Convert.ToHexString(
            SHA256.HashData(
                Encoding.UTF8.GetBytes(material)));
    }

    private static string BuildDiagnosticId(
        string value)
    {
        var bytes = SHA256.HashData(
            Encoding.UTF8.GetBytes(
                value ?? string.Empty));

        return Convert.ToHexString(bytes)[..12];
    }

    private static string NormalizeSceneToken(
        string? value)
    {
        var normalized = string.Join(
            " ",
            (value ?? string.Empty)
                .Trim()
                .ToLowerInvariant()
                .Split(
                    [' ', '\t', '\r', '\n'],
                    StringSplitOptions.RemoveEmptyEntries));

        return normalized.Length <= 80
            ? normalized
            : normalized[..80];
    }

    private static int Quantize(
        int value,
        int quantum) =>
        quantum <= 1
            ? value
            : (int)Math.Round(
                value / (double)quantum,
                MidpointRounding.AwayFromZero) * quantum;
}
