using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public interface IComputerDpiCalibrationService
{
    ComputerDpiCalibrationResponse GetStatus();
}

public sealed class ComputerDpiCalibrationService(
    IComputerUseService computer,
    IComputerDisplayTopologyService displays)
    : IComputerDpiCalibrationService
{
    public ComputerDpiCalibrationResponse GetStatus()
    {
        var awareness =
            WindowsDpiAwareness.GetStatus();
        var warnings = new List<string>();

        if (!awareness.Windows)
        {
            warnings.Add(
                "DPI calibration không áp dụng ngoài Windows.");
            return new(
                awareness,
                0,
                false,
                warnings);
        }

        ComputerScreenInfo screen;
        try
        {
            screen = computer.GetScreenInfo();
        }
        catch (ToolExecutionInputException exception)
        {
            warnings.Add(
                $"Không đọc được desktop ảo: {exception.Message}");
            return new(
                awareness,
                0,
                false,
                warnings);
        }

        var topology = displays.GetTopology();

        if (!awareness.PerMonitorAware)
        {
            warnings.Add(
                "Tiến trình chưa được xác nhận ở chế độ Per-Monitor DPI Aware; không được tự giả định screenshot và input cùng hệ pixel.");
        }

        if (topology.Count == 0)
            warnings.Add(
                "Windows không trả monitor nào.");

        if (topology.Count != screen.MonitorCount)
        {
            warnings.Add(
                $"Số monitor không khớp: topology={topology.Count}, systemMetrics={screen.MonitorCount}.");
        }

        foreach (var monitor in topology.Monitors)
        {
            if (monitor.Width <= 0 ||
                monitor.Height <= 0)
            {
                warnings.Add(
                    $"Monitor {monitor.DeviceName} có kích thước không hợp lệ.");
            }

            if (monitor.DpiX is < 72 or > 768 ||
                monitor.DpiY is < 72 or > 768)
            {
                warnings.Add(
                    $"Monitor {monitor.DeviceName} có DPI bất thường {monitor.DpiX}x{monitor.DpiY}.");
            }

            if (!double.IsFinite(monitor.ScaleX) ||
                !double.IsFinite(monitor.ScaleY) ||
                monitor.ScaleX <= 0 ||
                monitor.ScaleY <= 0)
            {
                warnings.Add(
                    $"Monitor {monitor.DeviceName} có hệ số scale không hợp lệ.");
            }
        }

        if (topology.Monitors.Count > 0)
        {
            var left = topology.Monitors.Min(item => item.Left);
            var top = topology.Monitors.Min(item => item.Top);
            var right = topology.Monitors.Max(
                item => checked(item.Left + item.Width));
            var bottom = topology.Monitors.Max(
                item => checked(item.Top + item.Height));

            var expectedRight = checked(
                screen.VirtualLeft + screen.VirtualWidth);
            var expectedBottom = checked(
                screen.VirtualTop + screen.VirtualHeight);

            if (left != screen.VirtualLeft ||
                top != screen.VirtualTop ||
                right != expectedRight ||
                bottom != expectedBottom)
            {
                warnings.Add(
                    $"Biên topology monitor ({left},{top})-({right},{bottom}) không khớp desktop ảo ({screen.VirtualLeft},{screen.VirtualTop})-({expectedRight},{expectedBottom}).");
            }
        }

        var topologyValid =
            topology.Count > 0 &&
            warnings.All(message =>
                message.StartsWith(
                    "Tiến trình chưa được xác nhận",
                    StringComparison.OrdinalIgnoreCase));

        // Nếu chỉ có cảnh báo awareness thì hình học monitor vẫn hợp lệ,
        // nhưng hệ tọa độ chưa được coi là đã hiệu chỉnh hoàn toàn.
        if (!awareness.PerMonitorAware)
            topologyValid = false;
        else if (warnings.Count == 0)
            topologyValid = true;

        return new(
            awareness,
            topology.Count,
            topologyValid,
            warnings);
    }
}
