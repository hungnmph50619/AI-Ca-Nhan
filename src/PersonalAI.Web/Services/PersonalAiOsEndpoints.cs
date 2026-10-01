namespace PersonalAI.Web.Services;

public static class PersonalAiOsEndpoints
{
    public static IServiceCollection AddPersonalAiOs(
        this IServiceCollection services)
    {
        services.AddScoped<IPersonalAiOsService, PersonalAiOsService>();
        return services;
    }

    public static WebApplication MapPersonalAiOs(
        this WebApplication app)
    {
        app.MapGet("/api/os/status", async (
            IPersonalAiOsService os,
            CancellationToken cancellationToken) =>
            Results.Ok(await os.GetStatusAsync(cancellationToken)));

        app.MapGet("/api/os/manifest", async (
            IPersonalAiOsService os,
            CancellationToken cancellationToken) =>
            Results.Ok(await os.GetManifestAsync(cancellationToken)));

        app.MapGet("/api/os/production-readiness", async (
            IPersonalAiOsService os,
            CancellationToken cancellationToken) =>
            Results.Ok(
                await os.GetProductionReadinessAsync(cancellationToken)));

        return app;
    }
}
