using System.Text.Json;
using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public sealed record LeagueAiCoachRequest(
    bool ConfirmExternalAi,
    string? ExpectedAdviceId = null,
    double? ExpectedAdviceGameTimeSeconds = null);

public sealed record LeagueAiCoachResult(
    string AdviceId,
    string Text,
    int Priority,
    double Confidence,
    double GameTimeSeconds,
    double ValidUntilGameTimeSeconds,
    IReadOnlyList<string> Evidence,
    bool UsedLanguageModel,
    string Provider,
    string Model);

public interface ILeagueAiCoachService
{
    Task<LeagueAiCoachResult?> AnalyzeAsync(
        string workspaceId,
        LeagueAiCoachRequest request,
        CancellationToken cancellationToken);
}

public sealed class LeagueAiCoachService(
    ILeagueMatchSnapshotStore snapshots,
    ILeagueCoachService deterministicCoach,
    IAiProviderResolver providers,
    IAuditRecorder audit) : ILeagueAiCoachService
{
    public async Task<LeagueAiCoachResult?> AnalyzeAsync(
        string workspaceId,
        LeagueAiCoachRequest request,
        CancellationToken cancellationToken)
    {
        if (!request.ConfirmExternalAi)
            throw new LeagueAiCoachValidationException(
                "Cần xác nhận riêng trước khi gửi ngữ cảnh trận tới nhà cung cấp AI.");

        var baseAdvice = deterministicCoach.GetCurrent(workspaceId);
        var session = snapshots.GetSession(workspaceId);
        var feed = snapshots.GetEvents(workspaceId);
        var timeline = snapshots.GetTimeline(workspaceId);

        if (baseAdvice is null || session is null || feed is null || timeline is null)
            return null;

        if (baseAdvice.Priority < 60)
            return null;

        if (!string.IsNullOrWhiteSpace(request.ExpectedAdviceId) &&
            !string.Equals(
                request.ExpectedAdviceId,
                baseAdvice.Id,
                StringComparison.Ordinal))
            throw new LeagueAiCoachValidationException(
                "Advice nền đã thay đổi; hãy đọc trạng thái mới trước khi gọi AI.");

        if (request.ExpectedAdviceGameTimeSeconds is double expectedTime &&
            Math.Abs(expectedTime - baseAdvice.GameTimeSeconds) > 0.001)
            throw new LeagueAiCoachValidationException(
                "Advice nền đã hết hạn hoặc bị thay thế.");

        var provider = providers.GetActive();
        if (!provider.IsConfigured)
            throw new LeagueAiCoachValidationException(
                "Nhà cung cấp AI hiện tại chưa được cấu hình.");

        var now = session.LastGameTimeSeconds;
        var recentEvents = feed.Events
            .Where(item =>
                item.MatchSessionId == session.MatchSessionId &&
                now - item.GameTimeSeconds is >= 0 and <= 20)
            .TakeLast(12)
            .Select(item => new
            {
                item.Kind,
                item.Severity,
                item.GameTimeSeconds,
                item.Evidence
            })
            .ToArray();

        var recentSnapshots = timeline.Snapshots
            .TakeLast(8)
            .Select(item => new
            {
                item.GameTimeSeconds,
                item.Level,
                healthPercent = item.MaxHealth > 0
                    ? Math.Round(100d * item.Health / item.MaxHealth, 1)
                    : 0,
                resourcePercent = item.MaxResource > 0
                    ? Math.Round(100d * item.Resource / item.MaxResource, 1)
                    : 0,
                item.ResourceType,
                item.Gold,
                item.AbilityPower,
                item.IsDead
            })
            .ToArray();

        var payload = JsonSerializer.Serialize(new
        {
            mode = "league-game-coach",
            rules = new[]
            {
                "Chỉ dùng dữ liệu quan sát được trong JSON.",
                "Không suy đoán vị trí đối thủ đang khuất tầm nhìn.",
                "Không khẳng định chắc chắn kết quả giao tranh.",
                "Không hướng dẫn tự động bấm phím, aim, né hoặc điều khiển nhân vật.",
                "Lời khuyên tối đa 180 ký tự, bằng tiếng Việt.",
                "Giữ hoặc giảm priority của advice nền; không tự tăng priority.",
                "Trả đúng một JSON object, không markdown."
            },
            currentGameTimeSeconds = now,
            baseAdvice = new
            {
                baseAdvice.Id,
                baseAdvice.Text,
                baseAdvice.Priority,
                baseAdvice.Confidence,
                baseAdvice.GameTimeSeconds,
                baseAdvice.ValidUntilGameTimeSeconds,
                baseAdvice.Evidence
            },
            recentEvents,
            recentSnapshots,
            outputSchema = new
            {
                text = "string <= 180 chars",
                confidence = "number 0..1",
                keep = "boolean"
            }
        });

        var response = await provider.ReplyAsync(
            [
                new ChatMessage(
                    "user",
                    "Bạn là bộ kiểm tra lời khuyên thời gian thực cho League of Legends. " +
                    "Không dùng kiến thức ngoài dữ liệu được cung cấp để khẳng định trạng thái trận. " +
                    "Hãy kiểm tra advice nền và trả JSON theo schema.\n" + payload)
            ],
            cancellationToken);

        var parsed = Parse(response);
        if (parsed is null || !parsed.Keep)
            return null;

        // Revalidate against fresh local state after the external model returns.
        // A tactically stale answer must never reach the HUD.
        var latestSession = snapshots.GetSession(workspaceId);
        var latestBaseAdvice = deterministicCoach.GetCurrent(workspaceId);
        if (latestSession is null ||
            latestSession.MatchSessionId != session.MatchSessionId ||
            latestBaseAdvice is null ||
            !string.Equals(
                latestBaseAdvice.Id,
                baseAdvice.Id,
                StringComparison.Ordinal) ||
            Math.Abs(
                latestBaseAdvice.GameTimeSeconds -
                baseAdvice.GameTimeSeconds) > 0.001 ||
            latestSession.LastGameTimeSeconds >
                baseAdvice.ValidUntilGameTimeSeconds)
        {
            audit.Record(
                AuditAgents.System,
                "league.coach.ai.discarded",
                $"match:{session.MatchSessionId:D}",
                $"advice:{baseAdvice.Id};reason:stale-after-model",
                AuditResults.Denied);
            return null;
        }

        var confidence = Math.Min(
            baseAdvice.Confidence,
            parsed.Confidence);
        var adviceText = parsed.Text.Trim();

        audit.Record(
            AuditAgents.System,
            "league.coach.ai",
            $"match:{session.MatchSessionId:D}",
            $"advice:{baseAdvice.Id};priority:{baseAdvice.Priority};provider:{provider.Name}",
            AuditResults.Succeeded);

        return new(
            baseAdvice.Id,
            adviceText,
            baseAdvice.Priority,
            confidence,
            baseAdvice.GameTimeSeconds,
            baseAdvice.ValidUntilGameTimeSeconds,
            baseAdvice.Evidence,
            UsedLanguageModel: true,
            provider.Name,
            provider.Model);
    }

    private static ParsedAiAdvice? Parse(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw) || raw.Length > 4096)
            return null;

        var text = raw.Trim();

        try
        {
            using var document = JsonDocument.Parse(text);
            var root = document.RootElement;

            if (!root.TryGetProperty("text", out var textElement) ||
                textElement.ValueKind != JsonValueKind.String ||
                !root.TryGetProperty("confidence", out var confidenceElement) ||
                !confidenceElement.TryGetDouble(out var confidence) ||
                !root.TryGetProperty("keep", out var keepElement) ||
                keepElement.ValueKind is not (
                    JsonValueKind.True or JsonValueKind.False))
                return null;

            var adviceText = textElement.GetString()?.Trim();
            if (string.IsNullOrWhiteSpace(adviceText) ||
                adviceText.Length > 180 ||
                !double.IsFinite(confidence) ||
                confidence is < 0 or > 1)
                return null;

            return new(
                adviceText,
                confidence,
                keepElement.ValueKind == JsonValueKind.True);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record ParsedAiAdvice(
        string Text,
        double Confidence,
        bool Keep);
}

public sealed class LeagueAiCoachValidationException(string message)
    : Exception(message);
