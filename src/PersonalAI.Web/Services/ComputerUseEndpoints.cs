using System.Net;
using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public static class ComputerUseEndpoints
{
    public static IServiceCollection AddComputerUse(
        this IServiceCollection services)
    {
        services.AddSingleton<ComputerControlGate>();
        services.AddSingleton<LeagueVisualProgressStore>();
        services.AddSingleton<ComputerOperatorProgressStore>();
        services.AddSingleton<ComputerOperatorExecutionControl>();
        services.AddSingleton<WindowsAiOperatorConsoleService>();
        services.AddHostedService(sp =>
            sp.GetRequiredService<WindowsAiOperatorConsoleService>());
        services.AddHostedService<WindowsStopHotkeyService>();
        services.AddSingleton<IComputerUseService, WindowsComputerUseService>();
        services.AddSingleton<IWin32TextAccessibilityBackend, WindowsTextAccessibilityBackend>();
        services.AddSingleton<IFlaUiAutomationClient, FlaUiAutomationClient>();
        services.AddSingleton<IFlaUiTextAccessibilityBackend, FlaUiTextAccessibilityBackend>();
        services.AddSingleton<ITextAccessibilityBackend, CompositeTextAccessibilityBackend>();
        services.AddSingleton<ITextClipboardWriter, WindowsTextClipboardWriter>();
        services.AddSingleton<IGenericTextInteractionEngine, GenericTextInteractionEngine>();
        services.AddSingleton<IAdaptiveObservationWakeSource, WindowsDesktopEventWakeSource>();
        services.AddSingleton<IAdaptiveVerificationWaitEngine, AdaptiveVerificationWaitEngine>();
        services.AddSingleton<IComputerDisplayTopologyService, WindowsComputerDisplayTopologyService>();
        services.AddSingleton<IComputerDpiCalibrationService, ComputerDpiCalibrationService>();
        services.AddSingleton<IComputerCoordinateTransformService, ComputerCoordinateTransformService>();
        services.AddSingleton<IComputerSafeTargetingService, ComputerSafeTargetingService>();
        services.AddSingleton<IComputerOperatorAcceptanceService, ComputerOperatorAcceptanceService>();
        services.AddSingleton<IComputerOperatorCheckpointStore, SqliteComputerOperatorCheckpointStore>();
        services.AddSingleton<IComputerOperatorTaskService, ComputerOperatorTaskService>();
        services.AddSingleton<IExecutionAgent, ComputerOperatorExecutionAgent>();
        services.AddSingleton<IExecutionAgentRegistry, ExecutionAgentRegistry>();
        services.AddSingleton<IMicrosoftAgentFrameworkAdapter, MicrosoftAgentFrameworkAdapter>();
        services.AddSingleton<IDesktopScreenshotService, WindowsDesktopScreenshotService>();
        services.AddSingleton<IDesktopFrameDifferenceService, DesktopFrameDifferenceService>();
        services.AddSingleton<IDesktopLocalFastObserver, DesktopLocalFastObserver>();
        services.AddSingleton<IComputerWindowVisibilityService, ComputerWindowVisibilityService>();
        services.AddSingleton<IDesktopTemporalSceneService, DesktopTemporalSceneService>();
        services.AddSingleton<IComputerOperatorActionExecutor, ComputerOperatorActionExecutor>();
        services.AddSingleton<ILeaguePracticeAutomationService, LeaguePracticeAutomationService>();
        services.AddSingleton<IPersonalAiTool, ComputerScreenInfoTool>();
        services.AddSingleton<IPersonalAiTool, ComputerCursorPositionTool>();
        services.AddSingleton<IPersonalAiTool, ComputerWindowsListTool>();
        services.AddSingleton<IPersonalAiTool, ComputerActiveWindowTool>();
        services.AddSingleton<IPersonalAiTool, ComputerFocusWindowTool>();
        services.AddSingleton<IPersonalAiTool, ComputerFocusWindowByQueryTool>();
        services.AddSingleton<IPersonalAiTool, ComputerMinimizeWindowTool>();
        services.AddSingleton<IPersonalAiTool, ComputerMaximizeWindowTool>();
        services.AddSingleton<IPersonalAiTool, ComputerRestoreWindowTool>();
        services.AddSingleton<IPersonalAiTool, ComputerMoveCursorTool>();
        services.AddSingleton<IPersonalAiTool, ComputerClickLeftTool>();
        services.AddSingleton<IPersonalAiTool, ComputerClickRightTool>();
        services.AddSingleton<IPersonalAiTool, ComputerDoubleClickLeftTool>();
        services.AddSingleton<IPersonalAiTool, ComputerScrollTool>();
        services.AddSingleton<IPersonalAiTool, ComputerDragLeftTool>();
        services.AddSingleton<IPersonalAiTool, ComputerTypeTextTool>();
        services.AddSingleton<IPersonalAiTool, ComputerPressKeyTool>();
        services.AddSingleton<IPersonalAiTool, ComputerPressHotkeyTool>();
        services.AddSingleton<IPersonalAiTool, ComputerOpenDefaultBrowserTool>();
        services.AddSingleton<IPersonalAiTool, ComputerOperatorTaskTool>();
        services.AddSingleton<IPersonalAiTool, ComputerGenericAppLaunchTool>();
        services.AddSingleton<IPersonalAiTool, ComputerVisionLocateTool>();
        services.AddSingleton<IPersonalAiTool, ComputerVisionClickTargetTool>();
        services.AddSingleton<IPersonalAiTool, LeaguePracticeOpenTool>();
        return services;
    }

    public static WebApplication MapComputerUse(
        this WebApplication app)
    {
        app.MapGet("/api/computer/status", (
            IComputerUseService computer) =>
            Results.Ok(computer.GetStatus()));

        app.MapGet("/api/computer/league-progress", (
            LeagueVisualProgressStore progress) =>
            Results.Ok(progress.Get()));

        app.MapGet("/api/computer/operator-progress", (
            ComputerOperatorProgressStore progress) =>
            Results.Ok(progress.Get()));

        app.MapGet("/api/computer/operator-checkpoint", (
            HttpContext context,
            IComputerOperatorCheckpointStore checkpoints) =>
        {
            if (!IsLocalRequest(context))
                return Results.StatusCode(StatusCodes.Status403Forbidden);

            var checkpoint = checkpoints.GetLatest();
            if (checkpoint is null)
            {
                return Results.Ok(new
                {
                    coCheckpoint = false,
                    thoiHanKhoiPhucGio =
                        SqliteComputerOperatorCheckpointStore.ResumeLifetime.TotalHours
                });
            }

            return Results.Ok(new
            {
                coCheckpoint = true,
                ma = checkpoint.Id,
                trangThai = checkpoint.Status,
                soMocDaXacMinh =
                    checkpoint.VerifiedMilestones.Count,
                tienDo =
                    checkpoint.GoalProgress,
                capNhatLuc =
                    checkpoint.UpdatedAt,
                thoiHanKhoiPhucGio =
                    SqliteComputerOperatorCheckpointStore.ResumeLifetime.TotalHours,
                nguyenTac =
                    "Checkpoint chỉ khôi phục bằng chứng đã xác minh. Computer Operator luôn quan sát lại desktop trước action mới và không replay action cuối."
            });
        });

        app.MapGet("/api/execution-agents", (
            IExecutionAgentRegistry agents) =>
            Results.Ok(new
            {
                agents = agents.GetAll()
            }));

        app.MapGet("/api/execution-agents/{agentId}", (
            string agentId,
            IExecutionAgentRegistry agents) =>
            agents.TryGet(agentId, out var agent) && agent is not null
                ? Results.Ok(agent.Definition)
                : Results.NotFound(
                    new ApiError("Không tìm thấy execution agent.")));

        app.MapGet("/api/execution-agents/microsoft-agent-framework/status", (
            IMicrosoftAgentFrameworkAdapter adapter) =>
            Results.Ok(adapter.GetStatus()));

        app.MapGet("/api/computer/operator-acceptance", (
            HttpContext context,
            IComputerOperatorAcceptanceService acceptance) =>
        {
            if (!IsLocalRequest(context))
                return Results.StatusCode(StatusCodes.Status403Forbidden);

            var result = acceptance.Run();
            return result.Passed
                ? Results.Ok(result)
                : Results.Json(
                    result,
                    statusCode: StatusCodes.Status500InternalServerError);
        });

        app.MapGet("/api/computer/displays", (
            IComputerDisplayTopologyService displays) =>
            Results.Ok(displays.GetTopology()));

        app.MapGet("/api/computer/dpi-calibration", (
            HttpContext context,
            IComputerDpiCalibrationService calibration) =>
        {
            if (!IsLocalRequest(context))
                return Results.StatusCode(StatusCodes.Status403Forbidden);

            return Results.Ok(
                calibration.GetStatus());
        });

        app.MapGet("/api/computer/operator-console/status", (
            HttpContext context,
            WindowsAiOperatorConsoleService console) =>
        {
            if (!IsLocalRequest(context))
                return Results.StatusCode(StatusCodes.Status403Forbidden);

            return Results.Ok(console.GetDiagnosticStatus());
        });

        app.MapPost("/api/computer/operator-console/test", async (
            HttpContext context,
            WindowsAiOperatorConsoleService console,
            CancellationToken cancellationToken) =>
        {
            if (!IsLocalRequest(context))
                return Results.StatusCode(StatusCodes.Status403Forbidden);

            console.ShowTestConsole(TimeSpan.FromSeconds(15));
            await Task.Delay(350, cancellationToken);

            return Results.Ok(new
            {
                message = "Đã yêu cầu hiển thị AI Operator Console trong 15 giây.",
                status = console.GetDiagnosticStatus()
            });
        });

        // Chỉ nhận thao tác bật/tắt từ máy đang chạy chương trình.
        // Không xem header này là xác thực: người dùng không nên mở máy chủ trên mạng công cộng.
        app.MapPost("/api/computer/control/stop", (
            HttpContext context,
            ComputerControlGate gate,
            ComputerOperatorExecutionControl execution,
            ComputerOperatorProgressStore progress,
            IAuditRecorder audit) =>
        {
            if (!IsLocalRequest(context))
                return Results.StatusCode(StatusCodes.Status403Forbidden);

            execution.Stop();
            gate.Stop();
            progress.StopByUser("Người dùng đã dừng tác vụ từ giao diện điều khiển.");
            audit.Record(AuditAgents.User, "computer.control.stop",
                "computer:desktop", "manual-local-request", AuditResults.Succeeded);
            return Results.Ok(new
            {
                paused = gate.Paused,
                message = "Đã tạm dừng thao tác điều khiển máy tính. Lệnh đang thực hiện có thể đã hoàn thành trước khi dừng."
            });
        });

        app.MapPost("/api/computer/operator/pause", (
            HttpContext context,
            ComputerOperatorExecutionControl execution,
            ComputerOperatorProgressStore progress,
            IAuditRecorder audit) =>
        {
            if (!IsLocalRequest(context))
                return Results.StatusCode(StatusCodes.Status403Forbidden);

            var changed = execution.Pause();
            if (changed)
            {
                progress.Pause();
                audit.Record(
                    AuditAgents.User,
                    "computer.operator.pause",
                    "computer:desktop",
                    "manual-local-request",
                    AuditResults.Succeeded);
            }

            return Results.Ok(new
            {
                running = execution.Running,
                paused = execution.Paused,
                changed,
                message = changed
                    ? "Đã tạm dừng Computer Operator ở ranh giới bước an toàn."
                    : "Computer Operator hiện không ở trạng thái có thể tạm dừng."
            });
        });

        app.MapPost("/api/computer/operator/resume", (
            HttpContext context,
            ComputerOperatorExecutionControl execution,
            ComputerOperatorProgressStore progress,
            IAuditRecorder audit) =>
        {
            if (!IsLocalRequest(context))
                return Results.StatusCode(StatusCodes.Status403Forbidden);

            var changed = execution.Resume();
            if (changed)
            {
                progress.Resume();
                audit.Record(
                    AuditAgents.User,
                    "computer.operator.resume",
                    "computer:desktop",
                    "manual-local-request",
                    AuditResults.Succeeded);
            }

            return Results.Ok(new
            {
                running = execution.Running,
                paused = execution.Paused,
                changed,
                message = changed
                    ? "Đã tiếp tục Computer Operator."
                    : "Computer Operator hiện không ở trạng thái có thể tiếp tục."
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

        // Văn bản chỉ nhập vào Notepad, không đi qua công cụ chung vì công cụ
        // chung ghi tham số vào nhật ký. Không ghi nội dung văn bản vào audit.
        app.MapGet("/api/computer/keyboard/notepad-windows", (
            HttpContext context,
            IComputerUseService computer) =>
        {
            if (!IsLocalRequest(context)
                || context.Request.Headers["X-PersonalAI-Manual-Approval"] != "dong-y")
                return Results.StatusCode(StatusCodes.Status403Forbidden);

            try
            {
                var windows = computer.GetWindows(50).Windows
                    .Where(window => string.Equals(
                        window.ProcessName, "notepad",
                        StringComparison.OrdinalIgnoreCase))
                    .Select(window => new
                    {
                        windowId = window.WindowId,
                        title = window.Title
                    })
                    .ToArray();
                return Results.Ok(new { windows });
            }
            catch (ToolExecutionInputException exception)
            {
                return Results.BadRequest(new ApiError(exception.Message));
            }
        });

        app.MapPost("/api/computer/keyboard/type-notepad", (
            HttpContext context,
            ComputerNotepadTextRequest request,
            IComputerUseService computer,
            IAuditRecorder audit) =>
        {
            if (!IsLocalRequest(context)
                || context.Request.Headers["X-PersonalAI-Manual-Approval"] != "dong-y")
                return Results.StatusCode(StatusCodes.Status403Forbidden);

            try
            {
                var result = computer.TypeNotepadText(
                    request.WindowId, request.Text);
                audit.Record(AuditAgents.User, "computer.keyboard.type-notepad",
                    "computer:desktop", "manual-local-confirmed",
                    AuditResults.Succeeded,
                    "Đã nhập văn bản vào Notepad. Nội dung không được ghi vào nhật ký.");
                return Results.Ok(result);
            }
            catch (ToolExecutionInputException exception)
            {
                return Results.BadRequest(new ApiError(exception.Message));
            }
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
