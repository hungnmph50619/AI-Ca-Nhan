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

        return app;
    }
}
