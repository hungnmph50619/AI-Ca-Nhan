using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public interface IEmergencyStopService
{
    bool IsEngaged { get; }
    CancellationToken CurrentToken { get; }
    EmergencyStopStatus GetStatus();
    EmergencyStopStatus Engage(
        string workspaceId,
        EngageEmergencyStopRequest request);
    EmergencyStopStatus Release(
        string workspaceId,
        ReleaseEmergencyStopRequest request);
    void ThrowIfEngaged(string surface);
}

public sealed class EmergencyStopService(
    ComputerControlGate computerControl,
    IAuditRecorder audit) : IEmergencyStopService, IDisposable
{
    private static readonly string[] Protected =
    [
        "agents",
        "tools",
        "browser",
        "desktop",
        "training",
        "automation",
        "development-autopilot"
    ];

    private readonly object _gate = new();
    private CancellationTokenSource _cts = new();
    private bool _engaged;
    private DateTimeOffset? _engagedAt;
    private string? _workspaceId;
    private string? _reason;
    private long _generation;

    public bool IsEngaged
    {
        get
        {
            lock (_gate)
                return _engaged;
        }
    }

    public CancellationToken CurrentToken
    {
        get
        {
            lock (_gate)
                return _cts.Token;
        }
    }

    public EmergencyStopStatus GetStatus()
    {
        lock (_gate)
            return Snapshot();
    }

    public EmergencyStopStatus Engage(
        string workspaceId,
        EngageEmergencyStopRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!request.ConfirmStop)
            throw new EmergencyStopValidationException(
                "Cần ConfirmStop=true để kích hoạt nút dừng khẩn cấp.");

        var workspace = NormalizeWorkspace(workspaceId);
        var reason = NormalizeReason(request.Reason);

        bool changed;
        EmergencyStopStatus status;
        lock (_gate)
        {
            changed = !_engaged;
            if (changed)
            {
                _engaged = true;
                _engagedAt = DateTimeOffset.UtcNow;
                _workspaceId = workspace;
                _reason = reason;
                _generation++;
                computerControl.Stop();
                try
                {
                    _cts.Cancel();
                }
                catch (ObjectDisposedException)
                {
                }
            }

            status = Snapshot();
        }

        audit.Record(
            AuditAgents.User,
            "emergency-stop.engage",
            "system:emergency-stop",
            changed
                ? $"reason:{reason};generation:{status.Generation}"
                : $"already-engaged;reason:{status.Reason};generation:{status.Generation}",
            changed ? AuditResults.Succeeded : AuditResults.Prepared,
            workspaceId: workspace,
            level: SystemLogLevels.Security,
            source: "emergency-stop",
            correlationId: $"emergency-stop:{status.Generation}");

        return status;
    }

    public EmergencyStopStatus Release(
        string workspaceId,
        ReleaseEmergencyStopRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!request.ConfirmRelease)
            throw new EmergencyStopValidationException(
                "Cần ConfirmRelease=true sau khi review trước khi mở khóa emergency stop.");

        var workspace = NormalizeWorkspace(workspaceId);
        EmergencyStopStatus status;
        bool changed;

        lock (_gate)
        {
            changed = _engaged;
            if (changed)
            {
                var previous = _cts;
                _cts = new CancellationTokenSource();
                _engaged = false;
                _engagedAt = null;
                _workspaceId = null;
                _reason = null;
                previous.Dispose();
            }

            status = Snapshot();
        }

        audit.Record(
            AuditAgents.User,
            "emergency-stop.release",
            "system:emergency-stop",
            changed
                ? $"user-confirmed-review;generation:{status.Generation}"
                : $"already-released;generation:{status.Generation}",
            AuditResults.Succeeded,
            workspaceId: workspace,
            level: SystemLogLevels.Security,
            source: "emergency-stop",
            correlationId: $"emergency-stop:{status.Generation}");

        return status;
    }

    public void ThrowIfEngaged(string surface)
    {
        lock (_gate)
        {
            if (!_engaged)
                return;

            throw new EmergencyStopEngagedException(
                $"Emergency stop đang bật; '{surface}' bị khóa. Review system log rồi explicit release trước khi tiếp tục.");
        }
    }

    private EmergencyStopStatus Snapshot() =>
        new(
            PersonalAiRelease.Version,
            _engaged,
            _engagedAt,
            _workspaceId,
            _reason,
            _generation,
            CancellationIssued: _engaged && _cts.IsCancellationRequested,
            DesktopControlPaused: computerControl.Paused,
            NewExecutionBlocked: _engaged,
            AuditPreserved: true,
            ExplicitReleaseRequired: true,
            Protected);

    private static string NormalizeWorkspace(string? value)
    {
        var normalized = (value ?? string.Empty)
            .Trim()
            .ToLowerInvariant();
        if (normalized.Length is < 1 or > 80)
            throw new EmergencyStopValidationException(
                "WorkspaceId không hợp lệ.");
        return normalized;
    }

    private static string NormalizeReason(string? value)
    {
        var normalized = string.Join(
            " ",
            (value ?? string.Empty)
                .Split(
                    [' ', '\t', '\r', '\n'],
                    StringSplitOptions.RemoveEmptyEntries));

        if (normalized.Length is < 3 or > 240)
            throw new EmergencyStopValidationException(
                "Reason phải có từ 3 đến 240 ký tự.");

        return normalized;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            try
            {
                _cts.Cancel();
            }
            catch
            {
            }

            _cts.Dispose();
        }
    }
}

public sealed class EmergencyStopMiddleware(
    RequestDelegate next)
{
    public async Task InvokeAsync(
        HttpContext context,
        IEmergencyStopService emergencyStop)
    {
        if (!emergencyStop.IsEngaged
            || !ShouldBlock(context.Request))
        {
            await next(context);
            return;
        }

        context.Response.StatusCode = StatusCodes.Status423Locked;
        context.Response.Headers["Retry-After"] = "1";
        await context.Response.WriteAsJsonAsync(
            new ApiError(
                "Emergency stop đang bật. Tác vụ thực thi mới bị khóa cho tới khi explicit release."));
    }

    private static bool ShouldBlock(HttpRequest request)
    {
        var path = request.Path.Value ?? string.Empty;
        if (path.StartsWith(
                "/api/system/emergency-stop",
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (HttpMethods.IsGet(request.Method)
            || HttpMethods.IsHead(request.Method))
        {
            return false;
        }

        if (path.Equals(
                "/api/computer/control/stop",
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return path.StartsWith("/api/agents", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/api/tools", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/api/browser", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/api/computer", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/api/model-lab", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/api/automations", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/api/development-autopilot", StringComparison.OrdinalIgnoreCase);
    }
}

public static class EmergencyStopApplicationExtensions
{
    public static IApplicationBuilder UseEmergencyStop(
        this IApplicationBuilder app) =>
        app.UseMiddleware<EmergencyStopMiddleware>();
}
