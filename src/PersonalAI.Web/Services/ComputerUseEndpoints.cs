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
        services.AddSingleton<IStructuredDesktopSnapshotService, StructuredDesktopSnapshotService>();
        services.AddSingleton<IStructuredDesktopVerificationService, StructuredDesktopVerificationService>();
        services.AddSingleton<IFlaUiTextAccessibilityBackend, FlaUiTextAccessibilityBackend>();
        services.AddSingleton<ITextAccessibilityBackend, CompositeTextAccessibilityBackend>();
        services.AddSingleton<ITextClipboardWriter, WindowsTextClipboardWriter>();
        services.AddSingleton<IGenericTextInteractionEngine, GenericTextInteractionEngine>();
        services.AddSingleton<IAdaptiveObservationWakeSource, WindowsDesktopEventWakeSource>();
        services.AddSingleton<IAdaptiveVerificationWaitEngine, AdaptiveVerificationWaitEngine>();
        services.AddSingleton<IComputerOperatorRuntimeStateIntelligence, ComputerOperatorRuntimeStateIntelligence>();
        services.AddSingleton<IComputerOperatorProgressIntelligence, ComputerOperatorProgressIntelligence>();
        services.AddSingleton<IComputerOperatorLearnedTimingService, ComputerOperatorLearnedTimingService>();
        services.AddSingleton<IComputerOperatorAdaptiveWaitPolicyResolver, ComputerOperatorAdaptiveWaitPolicyResolver>();
        services.AddSingleton<IComputerDisplayTopologyService, WindowsComputerDisplayTopologyService>();
        services.AddSingleton<IComputerDpiCalibrationService, ComputerDpiCalibrationService>();
        services.AddSingleton<IComputerCoordinateTransformService, ComputerCoordinateTransformService>();
        services.AddSingleton<IComputerSafeTargetingService, ComputerSafeTargetingService>();
        services.AddSingleton<IComputerOperatorAcceptanceService, ComputerOperatorAcceptanceService>();
        services.AddSingleton<IComputerOperatorCheckpointStore, SqliteComputerOperatorCheckpointStore>();
        services.AddSingleton<IComputerOperatorTelemetry, ComputerOperatorTelemetry>();
        services.AddSingleton<IUniversalReliableOperatorCoordinator, UniversalReliableOperatorCoordinator>();
        services.AddSingleton<IComputerOperatorTaskService, ComputerOperatorTaskService>();
        services.AddSingleton<IExecutionAgent, ComputerOperatorExecutionAgent>();
        services.AddSingleton<IExecutionAgentRegistry, ExecutionAgentRegistry>();
        services.AddSingleton<IMicrosoftAgentFrameworkAdapter, MicrosoftAgentFrameworkAdapter>();
        services.AddSingleton<IWindowsGraphicsCaptureClient, WindowsGraphicsCaptureClient>();
        services.AddSingleton<IDxgiDesktopDuplicationClient, DxgiDesktopDuplicationClient>();
        services.AddSingleton<IDesktopCaptureHealthTracker, DesktopCaptureHealthTracker>();
        services.AddSingleton<IDesktopCaptureBackendRouter, DesktopCaptureBackendRouter>();
        services.AddSingleton<IDesktopScreenshotService, WindowsDesktopScreenshotService>();
        services.AddSingleton<IDesktopFrameDifferenceService, DesktopFrameDifferenceService>();
        services.AddSingleton<IDesktopLocalVisualSensor, DesktopLocalVisualSensor>();
        services.AddSingleton<IDesktopTemplateMatchingSensor, OpenCvTemplateMatchingSensor>();
        services.AddSingleton<IDesktopVisualTargetPersistenceService, DesktopVisualTargetPersistenceService>();
        services.AddSingleton<ILocalVisualTargetResolver, LocalVisualTargetResolver>();
        services.AddSingleton<ILocalVisualVerificationService, LocalVisualVerificationService>();
        services.AddSingleton<ILocalVisualProviderHealthRegistry, LocalVisualProviderHealthRegistry>();
        services.AddSingleton<ILocalVisualSensorBudgetPolicy, LocalVisualSensorBudgetPolicy>();
        services.AddSingleton<ILocalVisionWorkerClient, LocalVisionWorkerClient>();
        services.AddSingleton<IDesktopOcrProvider, WindowsDesktopOcrSensor>();
        services.AddSingleton<IDesktopOcrProvider, PaddleOnnxDesktopOcrProvider>();
        services.AddSingleton<IDesktopOcrSensor, DesktopOcrSensorRouter>();
        services.AddSingleton<IDesktopOcrActionPlanner, DesktopOcrActionPlanner>();
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

        app.MapPost("/api/computer/operator-runtime-state/classify", (
            HttpContext context,
            ComputerOperatorRuntimeEvidence evidence,
            IComputerOperatorRuntimeStateIntelligence runtimeState) =>
        {
            if (!IsLocalRequest(context))
                return Results.StatusCode(StatusCodes.Status403Forbidden);

            try
            {
                var assessment =
                    runtimeState.Classify(evidence);

                return Results.Ok(new
                {
                    trangThai = assessment.State,
                    quyetDinh = assessment.RecommendedDecision,
                    doTinCay = Math.Round(assessment.Confidence, 2),
                    ketThuc = assessment.Terminal,
                    lyDo = assessment.Reason
                });
            }
            catch (ArgumentOutOfRangeException exception)
            {
                return Results.BadRequest(
                    new ApiError(exception.Message));
            }
        });

        app.MapPost("/api/computer/operator-progress-intelligence/assess", (
            HttpContext context,
            ComputerOperatorProgressObservation observation,
            IComputerOperatorProgressIntelligence progressIntelligence) =>
        {
            if (!IsLocalRequest(context))
                return Results.StatusCode(StatusCodes.Status403Forbidden);

            try
            {
                var assessment =
                    progressIntelligence.Assess(observation);

                return Results.Ok(new
                {
                    diemTienTrien = Math.Round(assessment.ProgressScore, 2),
                    diemHoatDongTaiNguyen = Math.Round(assessment.ResourceActivityScore, 2),
                    soTinHieuTienTrienManh = assessment.CorroboratingProgressSignals,
                    coTienTrienYNgia = assessment.MeaningfulProgress,
                    trangThai = assessment.RuntimeState.State,
                    quyetDinh = assessment.RuntimeState.RecommendedDecision,
                    adaptiveWait = assessment.AdaptiveSample.Status,
                    lyDo = assessment.Reason
                });
            }
            catch (ArgumentOutOfRangeException exception)
            {
                return Results.BadRequest(
                    new ApiError(exception.Message));
            }
        });

        app.MapGet("/api/computer/capture-backends", (
            HttpContext context,
            IDesktopCaptureBackendRouter captureRouter) =>
        {
            if (!IsLocalRequest(context))
                return Results.StatusCode(StatusCodes.Status403Forbidden);

            var snapshot = captureRouter.GetSnapshot();
            return Results.Ok(new
            {
                phienBan = snapshot.Version,
                backend = snapshot.Backends.Select(item => new
                {
                    ten = item.Name,
                    uuTien = item.Priority,
                    khaDung = item.Available,
                    phamVi = item.Scopes,
                    chiTiet = item.Detail
                }),
                uuTienTheoPhamVi = snapshot.PreferredBackendByScope
            });
        });

        app.MapGet("/api/computer/capture-health", (
            HttpContext context,
            IDesktopCaptureHealthTracker captureHealth) =>
        {
            if (!IsLocalRequest(context))
                return Results.StatusCode(StatusCodes.Status403Forbidden);

            var snapshot =
                captureHealth.GetSnapshot();

            return Results.Ok(new
            {
                phienBan = snapshot.Version,
                taoLucUtc = snapshot.GeneratedAtUtc,
                nguon = snapshot.Entries.Select(item => new
                {
                    phamVi = item.Scope,
                    mucTieu = item.Target,
                    backend = item.Backend,
                    thanhCong = item.SuccessCount,
                    thatBai = item.FailureCount,
                    fallback = item.FallbackCount,
                    lanCuoiMs = Math.Round(item.LastLatencyMs, 1),
                    trungBinhMs = Math.Round(item.AverageLatencyMs, 1),
                    frameLienTiepKhongDoi = item.ConsecutiveUnchangedFrames,
                    nghiNgoFrameCu = item.StaleSuspected,
                    capNhatLucUtc = item.LastUpdatedUtc,
                    lyDoFallbackGanNhat = item.LastFallbackReason
                })
            });
        });

        app.MapPost("/api/computer/capture-health/reset", (
            HttpContext context,
            IDesktopCaptureHealthTracker captureHealth) =>
        {
            if (!IsLocalRequest(context))
                return Results.StatusCode(StatusCodes.Status403Forbidden);

            captureHealth.Reset();

            return Results.Ok(new
            {
                daXoa = true,
                message =
                    "Đã xóa capture health telemetry trong bộ nhớ."
            });
        });

        app.MapGet("/api/computer/operator-learned-timing", (
            HttpContext context,
            IComputerOperatorLearnedTimingService timing) =>
        {
            if (!IsLocalRequest(context))
                return Results.StatusCode(StatusCodes.Status403Forbidden);

            return Results.Ok(new
            {
                phienBan = PersonalAiRelease.Version,
                toiThieuMauSanSang =
                    ComputerOperatorLearnedTimingService.MinimumReadySamples,
                hoSo = timing.GetProfiles().Select(item => new
                {
                    giaiDoan = item.Stage,
                    hanhDong = item.Action,
                    soMau = item.SampleCount,
                    trungViMs = Math.Round(item.MedianMilliseconds, 1),
                    p90Ms = Math.Round(item.P90Milliseconds, 1),
                    p95Ms = Math.Round(item.P95Milliseconds, 1),
                    toiDaMs = Math.Round(item.MaximumMilliseconds, 1),
                    doTinCay = Math.Round(item.Confidence, 2),
                    sanSang = item.Ready
                })
            });
        });

        app.MapGet("/api/computer/operator-telemetry", (
            HttpContext context,
            IComputerOperatorTelemetry telemetry) =>
        {
            if (!IsLocalRequest(context))
                return Results.StatusCode(StatusCodes.Status403Forbidden);

            var snapshot = telemetry.GetSnapshot();
            return Results.Ok(new
            {
                phienBan = snapshot.Version,
                chiLuuTrongBoNho = snapshot.MemoryOnly,
                tuongThichOpenTelemetry =
                    snapshot.OpenTelemetryCompatible,
                quyenRiengTu = new
                {
                    coLuuMucTieu = snapshot.ContainsGoals,
                    coLuuNoiDungText =
                        snapshot.ContainsTextPayloads,
                    coLuuAnhManHinh =
                        snapshot.ContainsScreenshots,
                    coLuuToaDo =
                        snapshot.ContainsCoordinates
                },
                toiDaSuKienGanNhat =
                    snapshot.MaximumRecentEvents,
                tongHop = snapshot.Aggregates.Select(item => new
                {
                    giaiDoan = item.Stage,
                    soLan = item.Count,
                    thanhCong = item.SuccessCount,
                    thatBai = item.FailureCount,
                    trungBinhMs =
                        Math.Round(item.AverageMilliseconds, 1),
                    toiDaMs = item.MaximumMilliseconds
                }),
                suKienGanNhat = snapshot.RecentEvents.Select(item => new
                {
                    stt = item.Sequence,
                    lucUtc = item.TimestampUtc,
                    traceId = item.TraceId,
                    giaiDoan = item.Stage,
                    hanhDong = item.Action,
                    duongXacMinh = item.Route,
                    thanhCong = item.Success,
                    thoiGianMs = item.DurationMilliseconds
                })
            });
        });

        app.MapPost("/api/computer/operator-telemetry/reset", (
            HttpContext context,
            IComputerOperatorTelemetry telemetry) =>
        {
            if (!IsLocalRequest(context))
                return Results.StatusCode(StatusCodes.Status403Forbidden);

            telemetry.Reset();
            return Results.Ok(new
            {
                daXoa = true,
                message =
                    "Đã xóa telemetry hiệu năng trong bộ nhớ. Không có nội dung task hoặc ảnh màn hình được lưu."
            });
        });

        app.MapGet("/api/computer/event-observation", (
            HttpContext context,
            IAdaptiveObservationWakeSource wakeSource) =>
        {
            if (!IsLocalRequest(context))
                return Results.StatusCode(StatusCodes.Status403Forbidden);

            var snapshot =
                wakeSource.GetSnapshot();

            return Results.Ok(new
            {
                phienBan = PersonalAiRelease.Version,
                eventDriven = snapshot.EventDrivenAvailable,
                daNhan = snapshot.ReceivedEvents,
                daGop = snapshot.CoalescedEvents,
                daLoai = snapshot.DroppedEvents,
                daDanhThuc = snapshot.DeliveredWakeups,
                pollingFallback = snapshot.PollFallbacks,
                dangCho = snapshot.QueuedEvents,
                suKienGanNhat = snapshot.LastEvent is null
                    ? null
                    : new
                    {
                        loai = snapshot.LastEvent.Kind,
                        cuaSo = snapshot.LastEvent.WindowId,
                        lucUtc = snapshot.LastEvent.OccurredAtUtc,
                        lyDo = snapshot.LastEvent.Reason,
                        nguon = snapshot.LastEvent.Source,
                        doTinCayNguon = Math.Round(snapshot.LastEvent.SourceConfidence, 2),
                        daDuocNguonKhacXacNhan = snapshot.LastEvent.Corroborated,
                        nhomSuKien = snapshot.LastEvent.BurstId,
                        soSuKienTrongNhom = snapshot.LastEvent.BurstSize
                    }
            });
        });

        app.MapGet("/api/computer/uia-events/wait", (
            HttpContext context,
            string windowId,
            int? waitMs,
            IFlaUiAutomationClient flaUi) =>
        {
            if (!IsLocalRequest(context))
                return Results.StatusCode(StatusCodes.Status403Forbidden);

            var resolvedWait =
                Math.Clamp(
                    waitMs ?? 500,
                    100,
                    3000);

            var response =
                flaUi.Invoke(
                    new FlaUiAutomationRequest(
                        "wait-uia-event",
                        windowId,
                        WaitMilliseconds: resolvedWait));

            return Results.Ok(new
            {
                phienBan = PersonalAiRelease.Version,
                thanhCong = response.Success,
                loaiSuKien = response.EventKind,
                cuaSo = response.EventWindowId,
                chiTiet = response.Detail
            });
        });

        app.MapGet("/api/computer/structured-tree", (
            HttpContext context,
            string windowId,
            int? maxNodes,
            int? maxDepth,
            IStructuredDesktopSnapshotService structured) =>
        {
            if (!IsLocalRequest(context))
                return Results.StatusCode(StatusCodes.Status403Forbidden);

            var snapshot =
                structured.CaptureWindow(
                    windowId,
                    maxNodes ?? 200,
                    maxDepth ?? 6);

            if (snapshot is null)
            {
                return Results.NotFound(new
                {
                    phienBan = PersonalAiRelease.Version,
                    message =
                        "Không đọc được structured UI tree bằng UIA3 cho cửa sổ này."
                });
            }

            return Results.Ok(new
            {
                phienBan = PersonalAiRelease.Version,
                nguon = snapshot.Source,
                cuaSo = snapshot.WindowId,
                root = snapshot.RootToken,
                chupLucUtc = snapshot.CapturedAtUtc,
                soNode = snapshot.NodeCount,
                gioiHanNode = snapshot.MaximumNodes,
                gioiHanDoSau = snapshot.MaximumDepth,
                chiTiet = snapshot.Detail,
                node = snapshot.Nodes.Select(item => new
                {
                    token = item.Token,
                    parentToken = item.ParentToken,
                    doSau = item.Depth,
                    vaiTro = item.Role,
                    ten = item.Name,
                    automationId = item.AutomationId,
                    className = item.ClassName,
                    bat = item.IsEnabled,
                    focus = item.IsFocused,
                    offscreen = item.IsOffscreen,
                    bounds = new
                    {
                        x = item.Left,
                        y = item.Top,
                        width = item.Width,
                        height = item.Height
                    },
                    pattern = item.Patterns
                })
            });
        });

        app.MapGet("/api/computer/operator-runtime", (
            HttpContext context,
            IUniversalReliableOperatorCoordinator reliableOperator) =>
        {
            if (!IsLocalRequest(context))
                return Results.StatusCode(StatusCodes.Status403Forbidden);

            var runtime =
                reliableOperator.GetRuntimeSnapshot();

            return Results.Ok(new
            {
                phienBan = runtime.Version,
                sanSang = runtime.Ready,
                lyDo = runtime.Reason,
                khaNangDesktop = runtime.AvailableComputerCapabilities,
                capabilityCache = new
                {
                    generation = runtime.CapabilityCache.Generation,
                    hits = runtime.CapabilityCache.Hits,
                    misses = runtime.CapabilityCache.Misses,
                    dangCache = runtime.CapabilityCache.RuntimeSnapshotCached,
                    tamNgungAdapter = runtime.CapabilityCache.TemporaryUnavailabilityCount
                },
                resilience = runtime.ResilienceDomains.Select(item => new
                {
                    mien = item.Domain,
                    trangThaiMach = item.CircuitState
                }),
                nguyenTac = new[]
                {
                    "Structured/deterministic evidence được ưu tiên trước Vision.",
                    "Strong-local đơn lẻ không được tự hoàn thành nếu chưa đủ bằng chứng.",
                    "Permission denial dừng; pending thì chờ; context đổi thì replan.",
                    "Không replay action có side effect chỉ vì lỗi kỹ thuật.",
                    "Resume luôn quan sát lại desktop hiện tại trước action mới."
                }
            });
        });

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
