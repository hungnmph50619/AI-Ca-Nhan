using System.Net;
using Microsoft.Net.Http.Headers;
using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public static class CompanionHttpContextKeys
{
    public const string Device =
        "PersonalAI.Companion.Device";
}

public static class CompanionEndpoints
{
    public static IServiceCollection AddAndroidCompanion(
        this IServiceCollection services)
    {
        services.AddSingleton<ICompanionService, CompanionService>();
        services.AddScoped<IChatTurnService, ChatTurnService>();
        return services;
    }

    public static IApplicationBuilder UseCompanionAuthentication(
        this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            if (!context.Request.Path.StartsWithSegments(
                "/api/companion/client",
                StringComparison.OrdinalIgnoreCase))
            {
                await next();
                return;
            }

            var companion =
                context.RequestServices
                    .GetRequiredService<ICompanionService>();

            if (!companion.Enabled)
            {
                await WriteError(
                    context,
                    StatusCodes.Status503ServiceUnavailable,
                    "Android Companion đang bị tắt.");
                return;
            }

            if (!context.Request.IsHttps
                && !companion.AllowInsecureHttp)
            {
                await WriteError(
                    context,
                    StatusCodes.Status426UpgradeRequired,
                    "Android Companion yêu cầu HTTPS. Chỉ bật Companion:AllowInsecureHttp trên mạng tin cậy khi bạn chấp nhận rủi ro.");
                return;
            }

            var authorization =
                context.Request.Headers.Authorization
                    .FirstOrDefault();
            if (string.IsNullOrWhiteSpace(authorization)
                || !authorization.StartsWith(
                    "Bearer ",
                    StringComparison.OrdinalIgnoreCase))
            {
                await WriteError(
                    context,
                    StatusCodes.Status401Unauthorized,
                    "Thiếu device token.");
                return;
            }

            var token =
                authorization["Bearer ".Length..]
                    .Trim();
            var device = companion.Authenticate(token);
            if (device is null)
            {
                await WriteError(
                    context,
                    StatusCodes.Status401Unauthorized,
                    "Device token không hợp lệ hoặc đã bị thu hồi.");
                return;
            }

            context.Request.Headers[
                WorkspaceEndpoints.WorkspaceHeaderName] =
                device.WorkspaceId;
            context.Items[
                CompanionHttpContextKeys.Device] =
                device;

            await next();
        });

    public static WebApplication MapAndroidCompanion(
        this WebApplication app)
    {
        app.MapGet("/api/companion/status", (
            ICompanionService companion) =>
            Results.Ok(companion.GetStatus()));

        app.MapPost(
            "/api/companion/admin/pairing/start",
            (
                CompanionPairingStartRequest request,
                HttpContext httpContext,
                ICompanionService companion,
                IWorkspaceContextAccessor workspaceContext,
                IAuditRecorder audit) =>
            {
                if (!IsLocalAdminRequest(httpContext))
                {
                    return Results.Json(
                        new ApiError(
                            "Chỉ desktop local mới được tạo mã ghép nối."),
                        statusCode:
                            StatusCodes.Status403Forbidden);
                }

                if (!request.Confirmed)
                {
                    return Results.Json(
                        new ApiError(
                            "Cần xác nhận trước khi tạo mã ghép nối Android."),
                        statusCode:
                            StatusCodes.Status403Forbidden);
                }

                try
                {
                    var ticket = companion.StartPairing(
                        workspaceContext.CurrentWorkspaceId);

                    audit.Record(
                        AuditAgents.User,
                        "companion.pairing.prepare",
                        $"workspace:{ticket.WorkspaceId}",
                        "user-confirmed-local-pairing",
                        AuditResults.Prepared,
                        workspaceId: ticket.WorkspaceId);

                    return Results.Ok(ticket);
                }
                catch (CompanionDisabledException exception)
                {
                    return Results.Json(
                        new ApiError(exception.Message),
                        statusCode:
                            StatusCodes.Status503ServiceUnavailable);
                }
            });

        app.MapPost(
            "/api/companion/pairing/claim",
            (
                CompanionPairingClaimRequest request,
                HttpContext httpContext,
                ICompanionService companion,
                IAuditRecorder audit) =>
            {
                if (!companion.Enabled)
                {
                    return Results.Json(
                        new ApiError(
                            "Android Companion đang bị tắt."),
                        statusCode:
                            StatusCodes.Status503ServiceUnavailable);
                }

                if (!httpContext.Request.IsHttps
                    && !companion.AllowInsecureHttp)
                {
                    return Results.Json(
                        new ApiError(
                            "Ghép nối Android Companion yêu cầu HTTPS."),
                        statusCode:
                            StatusCodes.Status426UpgradeRequired);
                }

                try
                {
                    var claimed =
                        companion.ClaimPairing(request);

                    audit.Record(
                        AuditAgents.Companion,
                        "companion.pairing.claim",
                        $"device:{claimed.Device.Id:D}",
                        "one-time-pairing-code",
                        AuditResults.Succeeded,
                        workspaceId:
                            claimed.Device.WorkspaceId);

                    return Results.Ok(claimed);
                }
                catch (CompanionPairingException exception)
                {
                    return Results.BadRequest(
                        new ApiError(exception.Message));
                }
            });

        app.MapGet(
            "/api/companion/admin/devices",
            (
                HttpContext httpContext,
                ICompanionService companion,
                IWorkspaceContextAccessor workspaceContext) =>
            {
                if (!IsLocalAdminRequest(httpContext))
                {
                    return Results.Json(
                        new ApiError(
                            "Chỉ desktop local mới được quản lý Android Companion."),
                        statusCode:
                            StatusCodes.Status403Forbidden);
                }

                return Results.Ok(
                    companion.GetDevices(
                        workspaceContext.CurrentWorkspaceId));
            });

        app.MapDelete(
            "/api/companion/admin/devices/{deviceId:guid}",
            (
                Guid deviceId,
                bool? confirmed,
                HttpContext httpContext,
                ICompanionService companion,
                IWorkspaceContextAccessor workspaceContext,
                IAuditRecorder audit) =>
            {
                if (!IsLocalAdminRequest(httpContext))
                {
                    return Results.Json(
                        new ApiError(
                            "Chỉ desktop local mới được thu hồi thiết bị Android."),
                        statusCode:
                            StatusCodes.Status403Forbidden);
                }

                if (confirmed != true)
                {
                    return Results.Json(
                        new ApiError(
                            "Cần xác nhận trước khi thu hồi device token."),
                        statusCode:
                            StatusCodes.Status403Forbidden);
                }

                var workspaceId =
                    workspaceContext.CurrentWorkspaceId;
                var revoked =
                    companion.RevokeDevice(
                        workspaceId,
                        deviceId);

                if (!revoked)
                {
                    return Results.NotFound(
                        new ApiError(
                            "Không tìm thấy thiết bị trong workspace hiện tại."));
                }

                audit.Record(
                    AuditAgents.User,
                    "companion.device.revoke",
                    $"device:{deviceId:D}",
                    "user-confirmed-device-revocation",
                    AuditResults.Succeeded,
                    workspaceId: workspaceId);

                return Results.NoContent();
            });

        app.MapGet(
            "/api/companion/client/me",
            (HttpContext httpContext) =>
            {
                var device =
                    GetAuthenticatedDevice(httpContext);
                return Results.Ok(
                    new CompanionMeResponse(
                        device,
                        PersonalAiRelease.Version,
                        [
                            CompanionCapabilities.Chat,
                            CompanionCapabilities.TasksRead,
                            CompanionCapabilities.CoreStatus
                        ]));
            });

        app.MapGet(
            "/api/companion/client/core",
            (
                HttpContext httpContext,
                IAiProviderResolver providerResolver,
                IWorkspaceContextAccessor workspaceContext) =>
            {
                _ = GetAuthenticatedDevice(
                    httpContext);
                var provider =
                    providerResolver.GetActive();

                return Results.Ok(
                    new CompanionCoreStatusResponse(
                        PersonalAiRelease.Version,
                        PersonalAiRelease.ApiContractVersion,
                        PersonalAiRelease.Channel,
                        workspaceContext.CurrentWorkspaceId,
                        workspaceContext.CurrentWorkspace.Name,
                        provider.IsConfigured,
                        provider.Name,
                        provider.Model));
            });

        app.MapGet(
            "/api/companion/client/tasks",
            (
                HttpContext httpContext,
                ITaskEngineService taskEngine) =>
            {
                _ = GetAuthenticatedDevice(
                    httpContext);
                return Results.Ok(
                    taskEngine.GetAll());
            });

        app.MapPost(
            "/api/companion/client/chat",
            async (
                ChatRequest request,
                HttpContext httpContext,
                IChatTurnService chat,
                CancellationToken cancellationToken) =>
            {
                _ = GetAuthenticatedDevice(
                    httpContext);

                var safeRequest = request with
                {
                    UseTools = false
                };

                try
                {
                    var response =
                        await chat.ExecuteAsync(
                            safeRequest,
                            allowToolProposal: false,
                            AuditAgents.Companion,
                            "paired-android-chat",
                            cancellationToken);
                    return Results.Ok(response);
                }
                catch (ChatValidationException exception)
                {
                    return Results.BadRequest(
                        new ApiError(exception.Message));
                }
                catch (KnowledgeDocumentValidationException exception)
                {
                    return Results.BadRequest(
                        new ApiError(exception.Message));
                }
                catch (InvalidOperationException exception)
                {
                    return Results.Json(
                        new ApiError(exception.Message),
                        statusCode:
                            StatusCodes.Status503ServiceUnavailable);
                }
                catch (HttpRequestException exception)
                {
                    var statusCode =
                        exception.StatusCode
                            == HttpStatusCode.TooManyRequests
                        ? StatusCodes.Status429TooManyRequests
                        : StatusCodes.Status502BadGateway;
                    return Results.Json(
                        new ApiError(exception.Message),
                        statusCode: statusCode);
                }
                catch (TaskCanceledException) when (
                    !cancellationToken.IsCancellationRequested)
                {
                    return Results.Json(
                        new ApiError(
                            "Yêu cầu AI mất quá nhiều thời gian. Hãy thử lại."),
                        statusCode:
                            StatusCodes.Status504GatewayTimeout);
                }
            });

        return app;
    }

    private static CompanionDevice GetAuthenticatedDevice(
        HttpContext context) =>
        context.Items.TryGetValue(
            CompanionHttpContextKeys.Device,
            out var value)
        && value is CompanionDevice device
            ? device
            : throw new InvalidOperationException(
                "Companion authentication middleware chưa gắn device context.");

    private static bool IsLocalAdminRequest(
        HttpContext context)
    {
        var remote =
            NormalizeIp(
                context.Connection.RemoteIpAddress);
        var local =
            NormalizeIp(
                context.Connection.LocalIpAddress);

        if (remote is null)
        {
            return false;
        }

        if (IPAddress.IsLoopback(remote))
        {
            return true;
        }

        return local is not null
            && remote.Equals(local);
    }

    private static IPAddress? NormalizeIp(
        IPAddress? address) =>
        address?.IsIPv4MappedToIPv6 == true
            ? address.MapToIPv4()
            : address;

    private static async Task WriteError(
        HttpContext context,
        int statusCode,
        string error)
    {
        context.Response.StatusCode =
            statusCode;
        context.Response.ContentType =
            "application/json";
        await context.Response.WriteAsJsonAsync(
            new ApiError(error));
    }
}
