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
        services.AddScoped<IDeviceHubService, DeviceHubService>();
        services.AddScoped<IDeviceIdentityService, DeviceIdentityService>();
        services.AddScoped<ISecurePairingService, SecurePairingService>();
        services.AddScoped<IDeviceCapabilityService, DeviceCapabilityService>();
        services.AddScoped<IDeviceOfflineQueueService, DeviceOfflineQueueService>();
        services.AddScoped<IDeviceDataConflictService, DeviceDataConflictService>();
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

        app.MapGet(
            "/api/companion/secure-pairing/status",
            (
                HttpContext httpContext,
                ISecurePairingService securePairing) =>
            {
                if (!IsLocalAdminRequest(httpContext))
                {
                    return Results.Json(
                        new ApiError(
                            "Chỉ desktop local mới được xem Secure Pairing status."),
                        statusCode:
                            StatusCodes.Status403Forbidden);
                }

                return Results.Ok(
                    securePairing.GetStatus());
            });

        app.MapPost(
            "/api/companion/admin/secure-pairing/start",
            (
                CompanionPairingStartRequest request,
                HttpContext httpContext,
                ISecurePairingService securePairing,
                IWorkspaceContextAccessor workspaceContext) =>
            {
                if (!IsLocalAdminRequest(httpContext))
                {
                    return Results.Json(
                        new ApiError(
                            "Chỉ desktop local mới được tạo secure pairing ticket."),
                        statusCode:
                            StatusCodes.Status403Forbidden);
                }

                if (!request.Confirmed)
                {
                    return Results.Json(
                        new ApiError(
                            "Cần xác nhận trước khi tạo secure pairing ticket."),
                        statusCode:
                            StatusCodes.Status403Forbidden);
                }

                try
                {
                    return Results.Ok(
                        securePairing.Start(
                            workspaceContext.CurrentWorkspaceId));
                }
                catch (SecurePairingValidationException exception)
                {
                    return Results.BadRequest(
                        new ApiError(exception.Message));
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
            "/api/companion/secure-pairing/claim",
            (
                SecurePairingClaimRequest request,
                HttpContext httpContext,
                ICompanionService companion,
                ISecurePairingService securePairing) =>
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
                            "Secure pairing yêu cầu HTTPS."),
                        statusCode:
                            StatusCodes.Status426UpgradeRequired);
                }

                try
                {
                    return Results.Ok(
                        securePairing.Claim(request));
                }
                catch (SecurePairingValidationException exception)
                {
                    return Results.BadRequest(
                        new ApiError(exception.Message));
                }
                catch (DeviceIdentityValidationException exception)
                {
                    return Results.BadRequest(
                        new ApiError(exception.Message));
                }
                catch (DeviceHubValidationException exception)
                {
                    return Results.BadRequest(
                        new ApiError(exception.Message));
                }
                catch (CompanionPairingException exception)
                {
                    return Results.BadRequest(
                        new ApiError(exception.Message));
                }
            });

        app.MapGet(
            "/api/device-hub/status",
            (
                HttpContext httpContext,
                IDeviceHubService hub) =>
            {
                if (!IsLocalAdminRequest(httpContext))
                {
                    return Results.Json(
                        new ApiError(
                            "Chỉ desktop local mới được xem Device Hub."),
                        statusCode:
                            StatusCodes.Status403Forbidden);
                }

                return Results.Ok(hub.GetStatus());
            });

        app.MapGet(
            "/api/device-hub/devices",
            (
                HttpContext httpContext,
                IDeviceHubService hub,
                IWorkspaceContextAccessor workspaceContext) =>
            {
                if (!IsLocalAdminRequest(httpContext))
                {
                    return Results.Json(
                        new ApiError(
                            "Chỉ desktop local mới được xem thiết bị Device Hub."),
                        statusCode:
                            StatusCodes.Status403Forbidden);
                }

                return Results.Ok(
                    hub.GetWorkspaceStatus(
                        workspaceContext.CurrentWorkspaceId));
            });

        app.MapPost(
            "/api/device-hub/admin/devices",
            (
                RegisterDeviceHubDeviceRequest request,
                HttpContext httpContext,
                IDeviceHubService hub,
                IWorkspaceContextAccessor workspaceContext) =>
            {
                if (!IsLocalAdminRequest(httpContext))
                {
                    return Results.Json(
                        new ApiError(
                            "Chỉ desktop local mới được đăng ký thiết bị Device Hub."),
                        statusCode:
                            StatusCodes.Status403Forbidden);
                }

                try
                {
                    var device =
                        hub.RegisterLocalDevice(
                            workspaceContext.CurrentWorkspaceId,
                            request);
                    var identities =
                        httpContext.RequestServices
                            .GetRequiredService<IDeviceIdentityService>();
                    _ = identities.EnsureDevice(device);
                    return Results.Ok(device);
                }
                catch (DeviceHubValidationException exception)
                {
                    return Results.BadRequest(
                        new ApiError(exception.Message));
                }
                catch (DeviceIdentityValidationException exception)
                {
                    return Results.BadRequest(
                        new ApiError(exception.Message));
                }
            });

        app.MapGet(
            "/api/device-hub/capabilities/status",
            (
                HttpContext httpContext,
                IDeviceCapabilityService capabilities,
                IWorkspaceContextAccessor workspaceContext) =>
            {
                if (!IsLocalAdminRequest(httpContext))
                {
                    return Results.Json(
                        new ApiError(
                            "Chỉ desktop local mới được xem Device Capability status."),
                        statusCode:
                            StatusCodes.Status403Forbidden);
                }

                try
                {
                    return Results.Ok(
                        capabilities.GetStatus(
                            workspaceContext.CurrentWorkspaceId));
                }
                catch (DeviceCapabilityValidationException exception)
                {
                    return Results.BadRequest(
                        new ApiError(exception.Message));
                }
            });

        app.MapGet(
            "/api/device-hub/devices/{deviceId:guid}/capabilities",
            (
                Guid deviceId,
                HttpContext httpContext,
                IDeviceCapabilityService capabilities,
                IWorkspaceContextAccessor workspaceContext) =>
            {
                if (!IsLocalAdminRequest(httpContext))
                {
                    return Results.Json(
                        new ApiError(
                            "Chỉ desktop local mới được xem capability của thiết bị."),
                        statusCode:
                            StatusCodes.Status403Forbidden);
                }

                try
                {
                    return Results.Ok(
                        capabilities.GetAll(
                            workspaceContext.CurrentWorkspaceId,
                            deviceId));
                }
                catch (DeviceCapabilityValidationException exception)
                {
                    return Results.BadRequest(
                        new ApiError(exception.Message));
                }
                catch (KeyNotFoundException exception)
                {
                    return Results.NotFound(
                        new ApiError(exception.Message));
                }
            });

        app.MapPost(
            "/api/device-hub/admin/devices/{deviceId:guid}/capabilities",
            (
                Guid deviceId,
                RegisterDeviceCapabilityRequest request,
                HttpContext httpContext,
                IDeviceCapabilityService capabilities,
                IWorkspaceContextAccessor workspaceContext) =>
            {
                if (!IsLocalAdminRequest(httpContext))
                {
                    return Results.Json(
                        new ApiError(
                            "Chỉ desktop local mới được đăng ký capability."),
                        statusCode:
                            StatusCodes.Status403Forbidden);
                }

                try
                {
                    return Results.Ok(
                        capabilities.Register(
                            workspaceContext.CurrentWorkspaceId,
                            deviceId,
                            request));
                }
                catch (DeviceCapabilityValidationException exception)
                {
                    return Results.BadRequest(
                        new ApiError(exception.Message));
                }
                catch (DeviceIdentityValidationException exception)
                {
                    return Results.BadRequest(
                        new ApiError(exception.Message));
                }
                catch (KeyNotFoundException exception)
                {
                    return Results.NotFound(
                        new ApiError(exception.Message));
                }
            });

        app.MapPost(
            "/api/device-hub/admin/devices/{deviceId:guid}/capabilities/{capability}/permission",
            (
                Guid deviceId,
                string capability,
                SetDeviceCapabilityPermissionRequest request,
                HttpContext httpContext,
                IDeviceCapabilityService capabilities,
                IWorkspaceContextAccessor workspaceContext) =>
            {
                if (!IsLocalAdminRequest(httpContext))
                {
                    return Results.Json(
                        new ApiError(
                            "Chỉ desktop local mới được thay đổi capability permission."),
                        statusCode:
                            StatusCodes.Status403Forbidden);
                }

                try
                {
                    return Results.Ok(
                        capabilities.SetPermission(
                            workspaceContext.CurrentWorkspaceId,
                            deviceId,
                            capability,
                            request));
                }
                catch (DeviceCapabilityValidationException exception)
                {
                    return Results.BadRequest(
                        new ApiError(exception.Message));
                }
                catch (DeviceIdentityValidationException exception)
                {
                    return Results.BadRequest(
                        new ApiError(exception.Message));
                }
                catch (KeyNotFoundException exception)
                {
                    return Results.NotFound(
                        new ApiError(exception.Message));
                }
            });

        app.MapGet(
            "/api/device-hub/admin/devices/{deviceId:guid}/capabilities/{capability}/access",
            (
                Guid deviceId,
                string capability,
                HttpContext httpContext,
                IDeviceCapabilityService capabilities,
                IWorkspaceContextAccessor workspaceContext) =>
            {
                if (!IsLocalAdminRequest(httpContext))
                {
                    return Results.Json(
                        new ApiError(
                            "Chỉ desktop local mới được kiểm tra capability access."),
                        statusCode:
                            StatusCodes.Status403Forbidden);
                }

                try
                {
                    var result =
                        capabilities.CheckAccess(
                            workspaceContext.CurrentWorkspaceId,
                            deviceId,
                            capability);

                    return result.Allowed
                        ? Results.Ok(result)
                        : Results.Json(
                            result,
                            statusCode:
                                StatusCodes.Status403Forbidden);
                }
                catch (DeviceCapabilityValidationException exception)
                {
                    return Results.BadRequest(
                        new ApiError(exception.Message));
                }
                catch (KeyNotFoundException exception)
                {
                    return Results.NotFound(
                        new ApiError(exception.Message));
                }
            });

        app.MapGet(
            "/api/device-hub/offline-queue/status",
            (
                HttpContext httpContext,
                IDeviceOfflineQueueService offlineQueue) =>
            {
                if (!IsLocalAdminRequest(httpContext))
                {
                    return Results.Json(
                        new ApiError(
                            "Chỉ desktop local mới được xem Offline Queue status."),
                        statusCode:
                            StatusCodes.Status403Forbidden);
                }

                return Results.Ok(offlineQueue.GetStatus());
            });

        app.MapGet(
            "/api/device-hub/offline-queue",
            (
                Guid? deviceId,
                HttpContext httpContext,
                IDeviceOfflineQueueService offlineQueue) =>
            {
                if (!IsLocalAdminRequest(httpContext))
                {
                    return Results.Json(
                        new ApiError(
                            "Chỉ desktop local mới được xem Offline Queue."),
                        statusCode:
                            StatusCodes.Status403Forbidden);
                }

                return Results.Ok(
                    offlineQueue.GetAll(deviceId));
            });

        app.MapPost(
            "/api/device-hub/admin/devices/{deviceId:guid}/offline-queue",
            (
                Guid deviceId,
                EnqueueDeviceOfflineItemRequest request,
                HttpContext httpContext,
                IDeviceOfflineQueueService offlineQueue) =>
            {
                if (!IsLocalAdminRequest(httpContext))
                {
                    return Results.Json(
                        new ApiError(
                            "Chỉ desktop local mới được xếp Offline Queue."),
                        statusCode:
                            StatusCodes.Status403Forbidden);
                }

                try
                {
                    return Results.Ok(
                        offlineQueue.Enqueue(
                            deviceId,
                            request));
                }
                catch (DeviceOfflineQueueValidationException exception)
                {
                    return Results.BadRequest(
                        new ApiError(exception.Message));
                }
                catch (DeviceHubValidationException exception)
                {
                    return Results.BadRequest(
                        new ApiError(exception.Message));
                }
                catch (KeyNotFoundException exception)
                {
                    return Results.NotFound(
                        new ApiError(exception.Message));
                }
            });

        app.MapPost(
            "/api/device-hub/admin/devices/{deviceId:guid}/offline-queue/sync",
            (
                Guid deviceId,
                SyncDeviceOfflineQueueRequest request,
                HttpContext httpContext,
                IDeviceOfflineQueueService offlineQueue) =>
            {
                if (!IsLocalAdminRequest(httpContext))
                {
                    return Results.Json(
                        new ApiError(
                            "Chỉ desktop local mới được đồng bộ Offline Queue."),
                        statusCode:
                            StatusCodes.Status403Forbidden);
                }

                try
                {
                    return Results.Ok(
                        offlineQueue.Sync(
                            deviceId,
                            request));
                }
                catch (DeviceOfflineQueueValidationException exception)
                {
                    return Results.BadRequest(
                        new ApiError(exception.Message));
                }
                catch (DeviceHubValidationException exception)
                {
                    return Results.BadRequest(
                        new ApiError(exception.Message));
                }
                catch (KeyNotFoundException exception)
                {
                    return Results.NotFound(
                        new ApiError(exception.Message));
                }
            });

        app.MapGet(
            "/api/device-hub/data-conflicts/status",
            (
                HttpContext httpContext,
                IDeviceDataConflictService conflicts) =>
            {
                if (!IsLocalAdminRequest(httpContext))
                {
                    return Results.Json(
                        new ApiError(
                            "Chỉ desktop local mới được xem Data Conflict status."),
                        statusCode:
                            StatusCodes.Status403Forbidden);
                }

                return Results.Ok(conflicts.GetStatus());
            });

        app.MapGet(
            "/api/device-hub/data-conflicts/locks",
            (
                HttpContext httpContext,
                IDeviceDataConflictService conflicts) =>
            {
                if (!IsLocalAdminRequest(httpContext))
                {
                    return Results.Json(
                        new ApiError(
                            "Chỉ desktop local mới được xem data locks."),
                        statusCode:
                            StatusCodes.Status403Forbidden);
                }

                return Results.Ok(conflicts.GetLocks());
            });

        app.MapGet(
            "/api/device-hub/data-conflicts/versions",
            (
                HttpContext httpContext,
                IDeviceDataConflictService conflicts) =>
            {
                if (!IsLocalAdminRequest(httpContext))
                {
                    return Results.Json(
                        new ApiError(
                            "Chỉ desktop local mới được xem data versions."),
                        statusCode:
                            StatusCodes.Status403Forbidden);
                }

                return Results.Ok(conflicts.GetVersions());
            });

        app.MapPost(
            "/api/device-hub/data-conflicts/locks",
            (
                AcquireDeviceDataLockRequest request,
                HttpContext httpContext,
                IDeviceDataConflictService conflicts) =>
            {
                if (!IsLocalAdminRequest(httpContext))
                {
                    return Results.Json(
                        new ApiError(
                            "Chỉ desktop local mới được giữ data lock."),
                        statusCode:
                            StatusCodes.Status403Forbidden);
                }

                try
                {
                    return Results.Ok(conflicts.AcquireLock(request));
                }
                catch (DeviceDataConflictValidationException exception)
                {
                    return Results.BadRequest(new ApiError(exception.Message));
                }
                catch (KeyNotFoundException exception)
                {
                    return Results.NotFound(new ApiError(exception.Message));
                }
            });

        app.MapPost(
            "/api/device-hub/data-conflicts/{resourceType}/{resourceId}/locks/release",
            (
                string resourceType,
                string resourceId,
                ReleaseDeviceDataLockRequest request,
                HttpContext httpContext,
                IDeviceDataConflictService conflicts) =>
            {
                if (!IsLocalAdminRequest(httpContext))
                {
                    return Results.Json(
                        new ApiError(
                            "Chỉ desktop local mới được nhả data lock."),
                        statusCode:
                            StatusCodes.Status403Forbidden);
                }

                try
                {
                    return conflicts.ReleaseLock(
                        resourceType,
                        resourceId,
                        request)
                        ? Results.Ok()
                        : Results.NotFound();
                }
                catch (DeviceDataConflictValidationException exception)
                {
                    return Results.BadRequest(new ApiError(exception.Message));
                }
            });

        app.MapPost(
            "/api/device-hub/data-conflicts/apply",
            (
                ApplyDeviceDataMutationRequest request,
                HttpContext httpContext,
                IDeviceDataConflictService conflicts) =>
            {
                if (!IsLocalAdminRequest(httpContext))
                {
                    return Results.Json(
                        new ApiError(
                            "Chỉ desktop local mới được áp dụng data mutation."),
                        statusCode:
                            StatusCodes.Status403Forbidden);
                }

                try
                {
                    var result = conflicts.Apply(request);
                    return result.Decision == DeviceDataConflictDecisions.Accepted
                        ? Results.Ok(result)
                        : Results.Json(
                            result,
                            statusCode:
                                StatusCodes.Status409Conflict);
                }
                catch (DeviceDataConflictValidationException exception)
                {
                    return Results.BadRequest(new ApiError(exception.Message));
                }
                catch (KeyNotFoundException exception)
                {
                    return Results.NotFound(new ApiError(exception.Message));
                }
            });

        app.MapGet(
            "/api/device-hub/identities/status",
            (
                HttpContext httpContext,
                IDeviceIdentityService identities,
                IWorkspaceContextAccessor workspaceContext) =>
            {
                if (!IsLocalAdminRequest(httpContext))
                {
                    return Results.Json(
                        new ApiError(
                            "Chỉ desktop local mới được xem Device Identity status."),
                        statusCode:
                            StatusCodes.Status403Forbidden);
                }

                try
                {
                    return Results.Ok(
                        identities.GetStatus(
                            workspaceContext.CurrentWorkspaceId));
                }
                catch (DeviceIdentityValidationException exception)
                {
                    return Results.BadRequest(
                        new ApiError(exception.Message));
                }
            });

        app.MapGet(
            "/api/device-hub/identities",
            (
                HttpContext httpContext,
                IDeviceIdentityService identities,
                IWorkspaceContextAccessor workspaceContext) =>
            {
                if (!IsLocalAdminRequest(httpContext))
                {
                    return Results.Json(
                        new ApiError(
                            "Chỉ desktop local mới được xem Device Identity."),
                        statusCode:
                            StatusCodes.Status403Forbidden);
                }

                try
                {
                    return Results.Ok(
                        identities.GetAll(
                            workspaceContext.CurrentWorkspaceId));
                }
                catch (DeviceIdentityValidationException exception)
                {
                    return Results.BadRequest(
                        new ApiError(exception.Message));
                }
            });

        app.MapGet(
            "/api/device-hub/identities/{deviceId:guid}",
            (
                Guid deviceId,
                HttpContext httpContext,
                IDeviceIdentityService identities,
                IWorkspaceContextAccessor workspaceContext) =>
            {
                if (!IsLocalAdminRequest(httpContext))
                {
                    return Results.Json(
                        new ApiError(
                            "Chỉ desktop local mới được xem Device Identity."),
                        statusCode:
                            StatusCodes.Status403Forbidden);
                }

                try
                {
                    var identity =
                        identities.Get(
                            workspaceContext.CurrentWorkspaceId,
                            deviceId);
                    return identity is null
                        ? Results.NotFound()
                        : Results.Ok(identity);
                }
                catch (DeviceIdentityValidationException exception)
                {
                    return Results.BadRequest(
                        new ApiError(exception.Message));
                }
            });

        app.MapPost(
            "/api/device-hub/admin/devices/{deviceId:guid}/heartbeat",
            (
                Guid deviceId,
                RecordDeviceHubHeartbeatRequest request,
                HttpContext httpContext,
                IDeviceHubService hub,
                IWorkspaceContextAccessor workspaceContext) =>
            {
                if (!IsLocalAdminRequest(httpContext))
                {
                    return Results.Json(
                        new ApiError(
                            "Chỉ desktop local mới được heartbeat thiết bị local."),
                        statusCode:
                            StatusCodes.Status403Forbidden);
                }

                try
                {
                    var device =
                        hub.RecordLocalHeartbeat(
                            workspaceContext.CurrentWorkspaceId,
                            deviceId,
                            request);
                    var identities =
                        httpContext.RequestServices
                            .GetRequiredService<IDeviceIdentityService>();
                    _ = identities.EnsureDevice(device);
                    return Results.Ok(device);
                }
                catch (DeviceHubValidationException exception)
                {
                    return Results.BadRequest(
                        new ApiError(exception.Message));
                }
                catch (DeviceIdentityValidationException exception)
                {
                    return Results.BadRequest(
                        new ApiError(exception.Message));
                }
                catch (KeyNotFoundException exception)
                {
                    return Results.NotFound(
                        new ApiError(exception.Message));
                }
            });

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
                    var hub =
                        httpContext.RequestServices
                            .GetRequiredService<IDeviceHubService>();
                    var identities =
                        httpContext.RequestServices
                            .GetRequiredService<IDeviceIdentityService>();
                    var hubDevice =
                        hub.RecordCompanionHeartbeat(
                            claimed.Device);
                    _ = identities.EnsureDevice(
                        hubDevice);

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
                catch (DeviceHubValidationException exception)
                {
                    return Results.BadRequest(
                        new ApiError(exception.Message));
                }
                catch (DeviceIdentityValidationException exception)
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
                            CompanionCapabilities.CoreStatus,
                            CompanionCapabilities.DeviceHubStatus,
                            CompanionCapabilities.DeviceIdentity,
                            CompanionCapabilities.SecurePairing,
                            CompanionCapabilities.DeviceCapabilities
                        ]));
            });

        app.MapPost(
            "/api/companion/client/hub/heartbeat",
            (
                HttpContext httpContext,
                IDeviceHubService hub) =>
            {
                try
                {
                    var device =
                        GetAuthenticatedDevice(httpContext);
                    var hubDevice =
                        hub.RecordCompanionHeartbeat(
                            device);
                    var identities =
                        httpContext.RequestServices
                            .GetRequiredService<IDeviceIdentityService>();
                    _ = identities.EnsureDevice(hubDevice);
                    return Results.Ok(hubDevice);
                }
                catch (DeviceHubValidationException exception)
                {
                    return Results.BadRequest(
                        new ApiError(exception.Message));
                }
            });

        app.MapGet(
            "/api/companion/client/hub/identity",
            (
                HttpContext httpContext,
                IDeviceHubService hub,
                IDeviceIdentityService identities) =>
            {
                try
                {
                    var companionDevice =
                        GetAuthenticatedDevice(httpContext);
                    var hubDevice =
                        hub.RecordCompanionHeartbeat(
                            companionDevice);
                    return Results.Ok(
                        identities.EnsureDevice(
                            hubDevice));
                }
                catch (DeviceIdentityValidationException exception)
                {
                    return Results.BadRequest(
                        new ApiError(exception.Message));
                }
            });

        app.MapGet(
            "/api/companion/client/hub/capabilities",
            (
                HttpContext httpContext,
                IDeviceHubService hub,
                IDeviceCapabilityService capabilities) =>
            {
                try
                {
                    var companionDevice =
                        GetAuthenticatedDevice(httpContext);
                    var hubDevice =
                        hub.RecordCompanionHeartbeat(
                            companionDevice);

                    return Results.Ok(
                        capabilities.GetAll(
                            hubDevice.WorkspaceId,
                            hubDevice.Id));
                }
                catch (DeviceCapabilityValidationException exception)
                {
                    return Results.BadRequest(
                        new ApiError(exception.Message));
                }
                catch (KeyNotFoundException exception)
                {
                    return Results.NotFound(
                        new ApiError(exception.Message));
                }
            });

        app.MapPost(
            "/api/companion/client/hub/capabilities/{capability}/authorize",
            (
                string capability,
                HttpContext httpContext,
                IDeviceHubService hub,
                IDeviceCapabilityService capabilities) =>
            {
                try
                {
                    var companionDevice =
                        GetAuthenticatedDevice(httpContext);
                    var hubDevice =
                        hub.RecordCompanionHeartbeat(
                            companionDevice);
                    var result =
                        capabilities.CheckAccess(
                            hubDevice.WorkspaceId,
                            hubDevice.Id,
                            capability);

                    return result.Allowed
                        ? Results.Ok(result)
                        : Results.Json(
                            result,
                            statusCode:
                                StatusCodes.Status403Forbidden);
                }
                catch (DeviceCapabilityValidationException exception)
                {
                    return Results.BadRequest(
                        new ApiError(exception.Message));
                }
                catch (KeyNotFoundException exception)
                {
                    return Results.NotFound(
                        new ApiError(exception.Message));
                }
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
