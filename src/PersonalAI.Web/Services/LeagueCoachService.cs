using System.Collections.Concurrent;

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
    private readonly ConcurrentDictionary<string, CoachState> _states =
        new(StringComparer.OrdinalIgnoreCase);

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
                now - item.GameTimeSeconds is >= 0 and <= 10)
            .OrderByDescending(item => item.GameTimeSeconds)
            .ToArray();

        var candidates = BuildCandidates(session, recent);
        if (candidates.Count == 0)
            return null;

        var state = _states.GetOrAdd(
            workspaceId,
            _ => new CoachState());

        lock (state.Sync)
        {
            if (state.MatchSessionId != session.MatchSessionId)
            {
                state.MatchSessionId = session.MatchSessionId;
                state.LastIssuedByAdvice.Clear();
            }

            foreach (var candidate in candidates
                         .OrderByDescending(item => item.Priority)
                         .ThenByDescending(item => item.GameTimeSeconds))
            {
                if (candidate.ValidUntilGameTimeSeconds < now)
                    continue;

                if (!state.LastIssuedByAdvice.TryGetValue(
                        candidate.Id,
                        out var previous))
                {
                    state.LastIssuedByAdvice[candidate.Id] =
                        new(candidate.GameTimeSeconds, candidate.Priority);
                    return candidate;
                }

                // Polling the exact same triggering event must return a stable
                // identity. The Xerath client can safely deduplicate it.
                if (Math.Abs(
                        previous.TriggerGameTimeSeconds -
                        candidate.GameTimeSeconds) < 0.001)
                    return candidate;

                var cooldown = CooldownSeconds(candidate.Priority);
                if (candidate.GameTimeSeconds -
                    previous.TriggerGameTimeSeconds < cooldown)
                    continue;

                state.LastIssuedByAdvice[candidate.Id] =
                    new(candidate.GameTimeSeconds, candidate.Priority);
                return candidate;
            }

            return null;
        }
    }

    private static List<LeagueCoachAdvice> BuildCandidates(
        LeagueMatchSessionSummary session,
        IReadOnlyList<LeagueMatchEvent> recent)
    {
        var items = new List<LeagueCoachAdvice>();

        var death = recent.FirstOrDefault(item =>
            item.Kind == "own-death");
        if (session.State == LeagueMatchSessionStates.Dead &&
            death is not null)
        {
            items.Add(Advice(
                "death",
                "state",
                "Bạn đang chờ hồi sinh. Có thể dùng thời gian này để kiểm tra vàng và kế hoạch quay lại đường.",
                100,
                1.0,
                death.GameTimeSeconds,
                8,
                death.Evidence));
        }

        var healthDrop = recent.FirstOrDefault(item =>
            item.Kind == "own-health-drop-high");
        var healthLow = recent.FirstOrDefault(item =>
            item.Kind == "own-health-low");

        if (healthDrop is not null && healthLow is not null)
        {
            var triggerTime = Math.Max(
                healthDrop.GameTimeSeconds,
                healthLow.GameTimeSeconds);
            items.Add(Advice(
                "health-drop-low",
                "danger",
                "Máu vừa giảm mạnh và hiện đang thấp. Ưu tiên giữ khoảng cách và tránh trao đổi kéo dài.",
                95,
                0.95,
                triggerTime,
                5,
                CombineEvidence(healthDrop, healthLow)));
        }
        else
        {
            if (healthDrop is not null)
            {
                items.Add(Advice(
                    "health-drop",
                    "danger",
                    "Bạn vừa mất nhiều máu trong thời gian ngắn. Cân nhắc lùi nhịp và đánh giá lại vị trí trước khi tiếp tục trao đổi.",
                    88,
                    0.92,
                    healthDrop.GameTimeSeconds,
                    5,
                    healthDrop.Evidence));
            }

            if (healthLow is not null)
            {
                items.Add(Advice(
                    "health-low",
                    "danger",
                    "Máu đang thấp. Giữ khoảng cách và ưu tiên đường rút an toàn.",
                    82,
                    0.98,
                    healthLow.GameTimeSeconds,
                    6,
                    healthLow.Evidence));
            }
        }

        var manaLow = recent.FirstOrDefault(item =>
            item.Kind == "own-mana-low");
        if (manaLow is not null)
        {
            items.Add(Advice(
                "mana-low",
                "resource",
                "Năng lượng đang thấp. Hạn chế trao đổi kéo dài và cân nhắc dành tài nguyên cho tình huống cần thiết.",
                62,
                0.98,
                manaLow.GameTimeSeconds,
                8,
                manaLow.Evidence));
        }

        var goldHigh = recent.FirstOrDefault(item =>
            item.Kind == "own-gold-high");
        if (goldHigh is not null)
        {
            items.Add(Advice(
                "gold-high",
                "economy",
                "Bạn đang giữ khá nhiều vàng. Khi có nhịp an toàn, cân nhắc về mua trang bị.",
                40,
                0.98,
                goldHigh.GameTimeSeconds,
                10,
                goldHigh.Evidence));
        }

        return items;
    }

    private static double CooldownSeconds(int priority) =>
        priority >= 90 ? 4 :
        priority >= 80 ? 6 :
        priority >= 60 ? 10 :
        15;

    private static LeagueCoachAdvice Advice(
        string id,
        string category,
        string text,
        int priority,
        double confidence,
        double triggerGameTime,
        double ttl,
        IReadOnlyList<string> evidence) =>
        new(
            id,
            category,
            text,
            priority,
            confidence,
            triggerGameTime,
            triggerGameTime + ttl,
            evidence,
            UsedLanguageModel: false);

    private static IReadOnlyList<string> CombineEvidence(
        params LeagueMatchEvent[] events) =>
        events
            .SelectMany(item => item.Evidence)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    private sealed class CoachState
    {
        public object Sync { get; } = new();
        public Guid MatchSessionId { get; set; }
        public Dictionary<string, IssuedAdvice> LastIssuedByAdvice { get; } =
            new(StringComparer.Ordinal);
    }

    private sealed record IssuedAdvice(
        double TriggerGameTimeSeconds,
        int Priority);
}
