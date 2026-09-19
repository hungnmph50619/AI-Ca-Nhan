using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public static class HardeningEndpoints
{
    public static IServiceCollection AddHardeningFoundation(
        this IServiceCollection services)
    {
        services.AddSingleton<IHardeningRuntimeState, HardeningRuntimeState>();
        services.AddSingleton<IHardeningBackupService, HardeningBackupService>();
        services.AddSingleton<IHardeningPermissionAuditService, HardeningPermissionAuditService>();
        services.AddSingleton<IHardeningStatusService, HardeningStatusService>();
        services.AddHostedService<HardeningRuntimeHostedService>();
        return services;
    }

    public static WebApplication UseV19Hardening(
        this WebApplication app)
    {
        app.UseMiddleware<HardeningApiGuardMiddleware>();
        return app;
    }

    public static WebApplication MapHardeningFoundation(
        this WebApplication app)
    {
        app.MapGet("/api/hardening/status", (
            IHardeningStatusService hardening) =>
            Results.Ok(hardening.GetStatus()));

        app.MapGet("/api/hardening/permissions", (
            IHardeningPermissionAuditService audit) =>
            Results.Ok(audit.Audit()));

        app.MapGet("/api/hardening/backups", (
            IHardeningBackupService backups) =>
            Results.Ok(new
            {
                maximum = HardeningLimits.MaximumBackupCount,
                pendingRestore = backups.HasPendingRestore,
                backups = backups.GetBackups()
            }));

        app.MapPost("/api/hardening/backups", async (
            CreateHardeningBackupRequest request,
            IHardeningBackupService backups,
            IAuditRecorder audit,
            CancellationToken cancellationToken) =>
        {
            if (!request.Confirmed)
            {
                return Results.Json(
                    new ApiError(
                        "Cần xác nhận rõ ràng trước khi tạo backup toàn bộ dữ liệu PersonalAI."),
                    statusCode: StatusCodes.Status403Forbidden);
            }

            try
            {
                var backup = await backups.CreateAsync(
                    cancellationToken);
                audit.Record(
                    AuditAgents.User,
                    "hardening.backup.create",
                    "system:backup",
                    "user-confirmed",
                    AuditResults.Succeeded);
                return Results.Ok(backup);
            }
            catch (HardeningBusyException exception)
            {
                return Results.Json(
                    new ApiError(exception.Message),
                    statusCode: StatusCodes.Status409Conflict);
            }
            catch (HardeningValidationException exception)
            {
                return Results.BadRequest(
                    new ApiError(exception.Message));
            }
        });

        app.MapPost("/api/hardening/restore", async (
            RestoreHardeningBackupRequest request,
            IHardeningBackupService backups,
            IAuditRecorder audit,
            CancellationToken cancellationToken) =>
        {
            if (!request.Confirmed)
            {
                return Results.Json(
                    new ApiError(
                        "Restore có thể thay thế dữ liệu hiện tại. Cần xác nhận rõ ràng."),
                    statusCode: StatusCodes.Status403Forbidden);
            }

            try
            {
                var result = await backups.QueueRestoreAsync(
                    request.BackupId ?? string.Empty,
                    cancellationToken);
                audit.Record(
                    AuditAgents.User,
                    "hardening.restore.queue",
                    "system:restore",
                    "user-confirmed-restart-required",
                    AuditResults.Prepared);
                return Results.Json(
                    result,
                    statusCode: StatusCodes.Status202Accepted);
            }
            catch (HardeningBusyException exception)
            {
                return Results.Json(
                    new ApiError(exception.Message),
                    statusCode: StatusCodes.Status409Conflict);
            }
            catch (HardeningValidationException exception)
            {
                return Results.BadRequest(
                    new ApiError(exception.Message));
            }
            catch (InvalidDataException)
            {
                return Results.BadRequest(
                    new ApiError(
                        "Backup đã chọn bị hỏng hoặc không phải backup PersonalAI hợp lệ."));
            }
        });

        return app;
    }
}
