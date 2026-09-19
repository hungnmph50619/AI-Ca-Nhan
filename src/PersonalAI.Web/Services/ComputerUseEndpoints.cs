using System.Net;
using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public static class ComputerUseEndpoints
{
    public static IServiceCollection AddComputerUse(
        this IServiceCollection services)
    {
        services.AddSingleton<ComputerControlGate>();
        services.AddHostedService<WindowsStopHotkeyService>();
        services.AddSingleton<IComputerUseService, WindowsComputerUseService>();
        services.AddSingleton<IPersonalAiTool, ComputerScreenInfoTool>();
        services.AddSingleton<IPersonalAiTool, ComputerCursorPositionTool>();
        services.AddSingleton<IPersonalAiTool, ComputerWindowsListTool>();
        services.AddSingleton<IPersonalAiTool, ComputerActiveWindowTool>();
        services.AddSingleton<IPersonalAiTool, ComputerFocusWindowTool>();
        services.AddSingleton<IPersonalAiTool, ComputerMoveCursorTool>();
        services.AddSingleton<IPersonalAiTool, ComputerClickLeftTool>();
        return services;
    }

    public static WebApplication MapComputerUse(
        this WebApplication app)
    {
        app.MapGet("/api/computer/status", (
            IComputerUseService computer) =>
            Results.Ok(computer.GetStatus()));

        // Chỉ nhận thao tác bật/tắt từ máy đang chạy chương trình.
        // Không xem header này là xác thực: người dùng không nên mở máy chủ trên mạng công cộng.
        app.MapPost("/api/computer/control/stop", (
            HttpContext context,
            ComputerControlGate gate,
            IAuditRecorder audit) =>
        {
            if (!IsLocalRequest(context))
                return Results.StatusCode(StatusCodes.Status403Forbidden);

            gate.Stop();
            audit.Record(AuditAgents.User, "computer.control.stop",
                "computer:desktop", "manual-local-request", AuditResults.Succeeded);
            return Results.Ok(new
            {
                paused = gate.Paused,
                message = "Đã tạm dừng thao tác điều khiển máy tính. Lệnh đang thực hiện có thể đã hoàn thành trước khi dừng."
            });
        });

        app.MapPost("/api/computer/control/enable", (
            HttpContext context,
            ComputerControlGate gate,
            IComputerUseService computer,
            IAuditRecorder audit) =>
        {
            if (!IsLocalRequest(context)
                || context.Request.Headers["X-PersonalAI-Manual-Approval"] != "dong-y")
                return Results.StatusCode(StatusCodes.Status403Forbidden);

            var status = computer.GetStatus();
            if (!status.Supported || !status.InteractiveSession)
                return Results.BadRequest(new ApiError(
                    "Điều khiển máy tính chỉ khả dụng trong phiên Windows đang tương tác."));

            ComputerControlSessionStatus session;
            try
            {
                session = gate.Enable();
            }
            catch (ToolExecutionInputException exception)
            {
                return Results.BadRequest(new ApiError(exception.Message));
            }
            audit.Record(AuditAgents.User, "computer.control.enable",
                "computer:desktop", "manual-local-request", AuditResults.Succeeded);
            return Results.Ok(new
            {
                paused = session.Paused,
                expiresAt = session.ExpiresAt,
                remainingActions = session.RemainingActions,
                message = "Đã cho phép chuyển cửa sổ, di chuyển chuột và nhấp trái đơn lẻ trong 60 giây hoặc tối đa 5 thao tác. Mỗi công cụ vẫn cần xác nhận riêng."
            });
        });

        return app;
    }

    private static bool IsLocalRequest(HttpContext context)
    {
        var remote = context.Connection.RemoteIpAddress;
        if (remote is null || !IPAddress.IsLoopback(remote))
            return false;

        var origin = context.Request.Headers["Origin"].ToString();
        if (string.IsNullOrWhiteSpace(origin))
            return true;

        return Uri.TryCreate(origin, UriKind.Absolute, out var parsed)
            && parsed.Scheme == context.Request.Scheme
            && parsed.Host.Equals(context.Request.Host.Host, StringComparison.OrdinalIgnoreCase)
            && parsed.Port == (context.Request.Host.Port
                ?? (context.Request.IsHttps ? 443 : 80));
    }
}
