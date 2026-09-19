using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace PersonalAI.Web.Services;

/// <summary>
/// First-stage, strictly local transport between Xerath Support Assistant and PersonalAI.
/// Deliberately deterministic: no external LLM calls, no screen frames, no enemy position,
/// no memory writes and no tactical recommendations. All output is whitelisted.
/// </summary>
public static class XerathBridgeEndpoints
{
    public sealed record Signal(string? Kind, double GameTimeSeconds, double? HealthPercent);
    public sealed record Notice(string Text, string Source, bool Verified, int ValidForMilliseconds, bool UsedLanguageModel);

    public static IEndpointRouteBuilder MapXerathBridge(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/integrations/xerath/status", (HttpContext context) =>
            LocalCaller(context) ? Results.Ok(new
            {
                online = true, bridgeVersion = 1, languageModelEnabled = false,
                description = "Cầu nối cục bộ. Chưa chạy mô hình AI thị giác hoặc AI hội thoại."
            }) : Results.NotFound());

        app.MapPost("/api/integrations/xerath/notice", (HttpContext context, Signal signal) =>
        {
            if (!LocalCaller(context) ||
                context.Request.Headers["X-Xerath-Bridge"].ToString() != "1")
                return Results.NotFound();

            if (!double.IsFinite(signal.GameTimeSeconds) || signal.GameTimeSeconds < 0 ||
                signal.GameTimeSeconds > 86400)
                return Results.BadRequest(new { error = "Thời gian trận không hợp lệ." });

            // Whitelist factual signals only. Do not accept arbitrary user prompts
            // or echo an untrusted string into the in-game HUD.
            Notice? notice = signal.Kind switch
            {
                "own-health-loss" when signal.HealthPercent is double hp &&
                                       double.IsFinite(hp) && hp is >= 0 and <= 100 =>
                    new($"Máu Xerath vừa giảm nhanh; hiện còn {hp:0}% (theo chỉ số của bạn).",
                        "riot-local-own-stats", true, 2000, false),
                "completed-kill" when signal.HealthPercent is null =>
                    new("Vừa có điểm hạ gục được ghi nhận. Chưa xác định vị trí giao tranh.",
                        "riot-local-public-event", true, 3000, false),
                _ => null
            };
            return notice is null
                ? Results.BadRequest(new { error = "Loại tín hiệu hoặc giá trị không hợp lệ." })
                : Results.Ok(notice);
        });

        return app;
    }

    private static bool LocalCaller(HttpContext context)
    {
        var ip = context.Connection.RemoteIpAddress;
        return ip is not null && IPAddress.IsLoopback(ip) &&
               (context.Request.Host.Host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
                context.Request.Host.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase));
    }
}
