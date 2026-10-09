using System.Net;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Server;

namespace PersonalAI.Web.Services;

public static class McpIntegrationEndpoints
{
    public const string Route = "/mcp";

    public static IServiceCollection AddPersonalAiMcp(
        this IServiceCollection services)
    {
        services.AddScoped<
            IPersonalAiMcpCapabilityBridge,
            PersonalAiMcpCapabilityBridge>();

        services
            .AddMcpServer()
            .WithHttpTransport(options =>
            {
                options.SessionMode =
                    HttpServerSessionMode.Stateless;
            })
            .WithTools<PersonalAiMcpTools>();

        return services;
    }

    public static WebApplication UsePersonalAiMcpBoundary(
        this WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            if (!context.Request.Path.StartsWithSegments(
                    Route,
                    StringComparison.OrdinalIgnoreCase))
            {
                await next();
                return;
            }

            if (!IsLoopbackRequest(context))
            {
                context.Response.StatusCode =
                    StatusCodes.Status403Forbidden;

                await context.Response.WriteAsJsonAsync(
                    new
                    {
                        error =
                            "MCP của Personal AI chỉ cho phép kết nối loopback trên máy local."
                    });

                return;
            }

            await next();
        });

        return app;
    }

    public static WebApplication MapPersonalAiMcp(
        this WebApplication app)
    {
        app.MapMcp(Route);
        return app;
    }

    internal static bool IsLoopbackRequest(
        HttpContext context)
    {
        var remote =
            context.Connection.RemoteIpAddress;

        if (remote is not null &&
            !IPAddress.IsLoopback(remote))
        {
            return false;
        }

        var host =
            context.Request.Host.Host
                .Trim()
                .Trim('[', ']');

        if (host.Length == 0)
            return true;

        if (host.Equals(
                "localhost",
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return IPAddress.TryParse(
                   host,
                   out var hostAddress) &&
               IPAddress.IsLoopback(
                   hostAddress);
    }
}
