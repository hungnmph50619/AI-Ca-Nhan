namespace PersonalAI.Web.Services;

public sealed record LeagueCoachAdvice(
    string Id,
    string Category,
    string Text,
    int Priority,
    double Confidence,
    double GameTimeSeconds,
    double ValidUntilGameTimeSeconds,
    IReadOnlyList<string> Evidence,
    bool UsedLanguageModel);

public interface ILeagueCoachService
{
    LeagueCoachAdvice? GetCurrent(string workspaceId);
}

public sealed class LeagueCoachService(
    ILeagueMatchSnapshotStore snapshots) : ILeagueCoachService
{
    public LeagueCoachAdvice? GetCurrent(string workspaceId)
    {
        var session = snapshots.GetSession(workspaceId);
        var feed = snapshots.GetEvents(workspaceId);
        if (session is null || feed is null)
            return null;

        var now = session.LastGameTimeSeconds;
        var recent = feed.Events
            .Where(item =>
                item.MatchSessionId == session.MatchSessionId &&
                now - item.GameTimeSeconds is >= 0 and <= 8)
            .OrderByDescending(item => item.GameTimeSeconds)
            .ToArray();

        if (session.State == LeagueMatchSessionStates.Dead)
        {
            return Advice(
                "death",
                "state",
                "Bạn đang chờ hồi sinh. Có thể dùng thời gian này để kiểm tra vàng và kế hoạch quay lại đường.",
                100,
                1.0,
                now,
                5,
                ["Trạng thái sống/chết từ Riot Live Client Data API." ]);
        }

        var healthDrop = recent.FirstOrDefault(item =>
            item.Kind == "own-health-drop-high");
        var healthLow = recent.FirstOrDefault(item =>
            item.Kind == "own-health-low");

        if (healthDrop is not null && healthLow is not null)
        {
            return Advice(
                "health-drop-low",
                "danger",
                "Máu vừa giảm mạnh và hiện đang thấp. Ưu tiên giữ khoảng cách và tránh trao đổi kéo dài.",
                95,
                0.95,
                now,
                4,
                CombineEvidence(healthDrop, healthLow));
        }

        if (healthDrop is not null)
        {
            return Advice(
                "health-drop",
                "danger",
                "Bạn vừa mất nhiều máu trong thời gian ngắn. Cân nhắc lùi nhịp và đánh giá lại vị trí trước khi tiếp tục trao đổi.",
                88,
                0.92,
                now,
                4,
                healthDrop.Evidence);
        }

        if (healthLow is not null)
        {
            return Advice(
                "health-low",
                "danger",
                "Máu đang thấp. Giữ khoảng cách và ưu tiên đường rút an toàn.",
                82,
                0.98,
                now,
                5,
                healthLow.Evidence);
        }

        var manaLow = recent.FirstOrDefault(item =>
            item.Kind == "own-mana-low");
        if (manaLow is not null)
        {
            return Advice(
                "mana-low",
                "resource",
                "Năng lượng đang thấp. Hạn chế trao đổi kéo dài và cân nhắc dành tài nguyên cho tình huống cần thiết.",
                62,
                0.98,
                now,
                7,
                manaLow.Evidence);
        }

        var goldHigh = recent.FirstOrDefault(item =>
            item.Kind == "own-gold-high");
        if (goldHigh is not null)
        {
            return Advice(
                "gold-high",
                "economy",
                "Bạn đang giữ khá nhiều vàng. Khi có nhịp an toàn, cân nhắc về mua trang bị.",
                40,
                0.98,
                now,
                8,
                goldHigh.Evidence);
        }

        return null;
    }

    private static LeagueCoachAdvice Advice(
        string id,
        string category,
        string text,
        int priority,
        double confidence,
        double now,
        double ttl,
        IReadOnlyList<string> evidence) =>
        new(
            id,
            category,
            text,
            priority,
            confidence,
            now,
            now + ttl,
            evidence,
            UsedLanguageModel: false);

    private static IReadOnlyList<string> CombineEvidence(
        params LeagueMatchEvent[] events) =>
        events
            .SelectMany(item => item.Evidence)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
}
