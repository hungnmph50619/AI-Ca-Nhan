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

        app.MapGet("/api/integrations/league/status", (
            HttpContext context,
            IWorkspaceContextAccessor workspace,
            ILeagueMatchSnapshotStore snapshots) =>
        {
            if (!LocalCaller(context))
                return Results.NotFound();

            var timeline = snapshots.GetTimeline(workspace.CurrentWorkspaceId);
            return Results.Ok(new
            {
                online = true,
                bridgeVersion = 2,
                mode = "riot-live-client-factual-snapshots",
                languageModelEnabled = false,
                automaticGameControl = false,
                currentMatchSessionId = timeline?.MatchSessionId,
                timelineCount = timeline?.Snapshots.Count ?? 0,
                lastGameTimeSeconds = timeline?.LastGameTimeSeconds
            });
        });

        app.MapPost("/api/integrations/league/snapshot", (
            HttpContext context,
            LeagueMatchSnapshot snapshot,
            IWorkspaceContextAccessor workspace,
            ILeagueMatchSnapshotStore snapshots) =>
        {
            if (!LocalCaller(context) ||
                context.Request.Headers["X-Xerath-Bridge"].ToString() != "2")
                return Results.NotFound();

            var error = ValidateSnapshot(snapshot);
            if (error is not null)
                return Results.BadRequest(new { error });

            var result = snapshots.Accept(
                workspace.CurrentWorkspaceId,
                snapshot);

            if (!result.Accepted)
            {
                return Results.Json(
                    new
                    {
                        accepted = false,
                        bridgeVersion = 2,
                        result.Reason,
                        result.MatchSessionId,
                        result.TimelineCount
                    },
                    statusCode: StatusCodes.Status409Conflict);
            }

            return Results.Ok(new
            {
                accepted = true,
                bridgeVersion = 2,
                result.NewMatchSession,
                result.MatchSessionId,
                result.TimelineCount
            });
        });

        app.MapGet("/api/integrations/league/timeline", (
            HttpContext context,
            IWorkspaceContextAccessor workspace,
            ILeagueMatchSnapshotStore snapshots) =>
        {
            if (!LocalCaller(context))
                return Results.NotFound();

            var timeline = snapshots.GetTimeline(workspace.CurrentWorkspaceId);
            return timeline is null
                ? Results.NotFound()
                : Results.Ok(timeline);
        });

        app.MapGet("/api/integrations/league/session", (
            HttpContext context,
            IWorkspaceContextAccessor workspace,
            ILeagueMatchSnapshotStore snapshots) =>
        {
            if (!LocalCaller(context))
                return Results.NotFound();

            var session = snapshots.GetSession(workspace.CurrentWorkspaceId);
            return session is null
                ? Results.NotFound()
                : Results.Ok(session);
        });

        app.MapGet("/api/integrations/league/events", (
            HttpContext context,
            IWorkspaceContextAccessor workspace,
            ILeagueMatchSnapshotStore snapshots) =>
        {
            if (!LocalCaller(context))
                return Results.NotFound();

            var events = snapshots.GetEvents(workspace.CurrentWorkspaceId);
            return events is null
                ? Results.NotFound()
                : Results.Ok(events);
        });

        app.MapGet("/api/integrations/league/coach/current", (
            HttpContext context,
            IWorkspaceContextAccessor workspace,
            ILeagueCoachService coach) =>
        {
            if (!LocalCaller(context))
                return Results.NotFound();

            var advice = coach.GetCurrent(workspace.CurrentWorkspaceId);
            return advice is null
                ? Results.NoContent()
                : Results.Ok(advice);
        });

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

    private static string? ValidateSnapshot(
        LeagueMatchSnapshot snapshot)
    {
        if (snapshot.BridgeVersion != 2)
            return "BridgeVersion phải là 2.";
        if (!Guid.TryParseExact(
                snapshot.ClientInstanceId,
                "N",
                out _))
            return "ClientInstanceId không hợp lệ.";
        if (snapshot.Sequence < 1)
            return "Sequence phải lớn hơn 0.";
        if (snapshot.Source != "riot-live-client-data")
            return "Source không được hỗ trợ.";
        if (snapshot.ObservedAtUtc < DateTimeOffset.UtcNow.AddMinutes(-5) ||
            snapshot.ObservedAtUtc > DateTimeOffset.UtcNow.AddMinutes(1))
            return "ObservedAtUtc nằm ngoài cửa sổ cho phép.";
        if (!double.IsFinite(snapshot.GameTimeSeconds) ||
            snapshot.GameTimeSeconds is < 0 or > 86400)
            return "GameTimeSeconds không hợp lệ.";
        if (snapshot.Level is < 1 or > 30)
            return "Level không hợp lệ.";
        if (!FiniteRange(snapshot.MaxHealth, 1, 100000) ||
            !FiniteRange(snapshot.Health, 0, snapshot.MaxHealth * 1.1))
            return "Health không hợp lệ.";
        if (!FiniteRange(snapshot.MaxResource, 0, 100000) ||
            !FiniteRange(
                snapshot.Resource,
                0,
                Math.Max(1, snapshot.MaxResource) * 1.1))
            return "Resource không hợp lệ.";
        if (snapshot.ResourceType.Length > 32)
            return "ResourceType quá dài.";
        if (!FiniteRange(snapshot.Gold, 0, 100000) ||
            !FiniteRange(snapshot.AbilityPower, 0, 100000))
            return "Gold hoặc AbilityPower không hợp lệ.";

        return null;
    }

    private static bool FiniteRange(
        double value,
        double minimum,
        double maximum) =>
        double.IsFinite(value) &&
        value >= minimum &&
        value <= maximum;

    private static bool LocalCaller(HttpContext context)
    {
        var ip = context.Connection.RemoteIpAddress;
        return ip is not null && IPAddress.IsLoopback(ip) &&
               (context.Request.Host.Host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
                context.Request.Host.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase));
    }
}
