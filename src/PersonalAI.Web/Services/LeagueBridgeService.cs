using System.Collections.Concurrent;

namespace PersonalAI.Web.Services;

public sealed record LeagueMatchSnapshot(
    int BridgeVersion,
    string ClientInstanceId,
    long Sequence,
    DateTimeOffset ObservedAtUtc,
    string Source,
    double GameTimeSeconds,
    int Level,
    double Health,
    double MaxHealth,
    double Resource,
    double MaxResource,
    string ResourceType,
    double Gold,
    double AbilityPower,
    bool IsDead);

public sealed record LeagueMatchSnapshotEnvelope(
    Guid MatchSessionId,
    string WorkspaceId,
    LeagueMatchSnapshot Snapshot);

public sealed record LeagueMatchSnapshotAcceptResult(
    bool Accepted,
    bool NewMatchSession,
    Guid MatchSessionId,
    int TimelineCount,
    string Reason);

public sealed record LeagueMatchTimeline(
    Guid MatchSessionId,
    string WorkspaceId,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset LastObservedAtUtc,
    double LastGameTimeSeconds,
    IReadOnlyList<LeagueMatchSnapshot> Snapshots);

public sealed record LeagueMatchEvent(
    Guid Id,
    Guid MatchSessionId,
    string Kind,
    string Severity,
    double GameTimeSeconds,
    DateTimeOffset ObservedAtUtc,
    string Message,
    IReadOnlyList<string> Evidence);

public sealed record LeagueMatchEventFeed(
    Guid MatchSessionId,
    string WorkspaceId,
    IReadOnlyList<LeagueMatchEvent> Events);

public sealed record LeagueVisibleChampionObservation(
    string Champion,
    string Team,
    double X,
    double Y,
    double Confidence);

public sealed record LeagueVisionObservationBatch(
    int BridgeVersion,
    string ClientInstanceId,
    long Sequence,
    DateTimeOffset ObservedAtUtc,
    string Source,
    double GameTimeSeconds,
    IReadOnlyList<LeagueVisibleChampionObservation> Observations);

public sealed record LeagueChampionLastSeen(
    string Champion,
    string Team,
    double X,
    double Y,
    double Confidence,
    double GameTimeSeconds,
    DateTimeOffset ObservedAtUtc,
    int ObservationCount);

public sealed record LeagueVisionState(
    Guid MatchSessionId,
    string WorkspaceId,
    IReadOnlyList<LeagueChampionLastSeen> LastSeen);

public sealed record LeagueMapSighting(
    string Champion,
    string Team,
    double X,
    double Y,
    double Confidence,
    double LastSeenGameTimeSeconds,
    double LastSeenAgeSeconds,
    int ObservationCount);

public sealed record LeagueMapSituation(
    Guid MatchSessionId,
    string WorkspaceId,
    double CurrentGameTimeSeconds,
    int RecentEnemyCount,
    IReadOnlyList<LeagueMapSighting> Sightings,
    bool EvidenceOnly,
    bool HiddenPositionInference);

public sealed record LeagueVisionAcceptResult(
    bool Accepted,
    Guid MatchSessionId,
    int LastSeenCount,
    string Reason);

public static class LeagueMatchSessionStates
{
    public const string Active = "active";
    public const string Dead = "dead";
    public const string Stale = "stale";
}

public sealed record LeagueMatchSessionSummary(
    Guid MatchSessionId,
    string WorkspaceId,
    string State,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset LastObservedAtUtc,
    double LastGameTimeSeconds,
    int TimelineCount,
    LeagueMatchSnapshot LatestSnapshot);

public interface ILeagueMatchSnapshotStore
{
    LeagueMatchSnapshotAcceptResult Accept(
        string workspaceId,
        LeagueMatchSnapshot snapshot);

    LeagueMatchTimeline? GetTimeline(string workspaceId);

    LeagueMatchSessionSummary? GetSession(string workspaceId);

    LeagueMatchEventFeed? GetEvents(string workspaceId);

    LeagueVisionAcceptResult AcceptVision(
        string workspaceId,
        LeagueVisionObservationBatch batch);

    LeagueVisionState? GetVision(string workspaceId);

    LeagueMapSituation? GetMapSituation(string workspaceId);
}

public sealed class LeagueMatchSnapshotStore : ILeagueMatchSnapshotStore
{
    private const int MaximumSnapshots = 180;
    private readonly ConcurrentDictionary<string, WorkspaceState> _states =
        new(StringComparer.OrdinalIgnoreCase);

    public LeagueMatchSnapshotAcceptResult Accept(
        string workspaceId,
        LeagueMatchSnapshot snapshot)
    {
        var state = _states.GetOrAdd(
            workspaceId,
            _ => new WorkspaceState());

        lock (state.Sync)
        {
            var newSession = state.SessionId == Guid.Empty ||
                snapshot.GameTimeSeconds + 20 < state.LastGameTimeSeconds;

            if (newSession)
            {
                state.SessionId = Guid.NewGuid();
                state.StartedAtUtc = snapshot.ObservedAtUtc;
                state.Snapshots.Clear();
                state.Events.Clear();
                state.VisionLastSeen.Clear();
                state.LastSequenceByClient.Clear();
                state.LastVisionSequenceByClient.Clear();
            }

            if (state.LastSequenceByClient.TryGetValue(
                    snapshot.ClientInstanceId,
                    out var lastSequence) &&
                snapshot.Sequence <= lastSequence)
            {
                return new(
                    Accepted: false,
                    NewMatchSession: false,
                    MatchSessionId: state.SessionId,
                    TimelineCount: state.Snapshots.Count,
                    Reason: "duplicate-or-out-of-order-sequence");
            }

            if (state.Snapshots.Count > 0 &&
                snapshot.GameTimeSeconds + 3 < state.LastGameTimeSeconds &&
                !newSession)
            {
                return new(
                    Accepted: false,
                    NewMatchSession: false,
                    MatchSessionId: state.SessionId,
                    TimelineCount: state.Snapshots.Count,
                    Reason: "stale-game-time");
            }

            var previous = state.Snapshots.Count > 0
                ? state.Snapshots.Last()
                : null;

            state.LastSequenceByClient[snapshot.ClientInstanceId] =
                snapshot.Sequence;
            state.LastGameTimeSeconds = snapshot.GameTimeSeconds;
            state.LastObservedAtUtc = snapshot.ObservedAtUtc;
            state.Snapshots.Enqueue(snapshot);

            foreach (var item in BuildEvents(
                         state.SessionId,
                         previous,
                         snapshot))
                state.Events.Enqueue(item);

            while (state.Snapshots.Count > MaximumSnapshots)
                state.Snapshots.Dequeue();
            while (state.Events.Count > 240)
                state.Events.Dequeue();

            return new(
                Accepted: true,
                NewMatchSession: newSession,
                MatchSessionId: state.SessionId,
                TimelineCount: state.Snapshots.Count,
                Reason: "accepted");
        }
    }

    public LeagueVisionAcceptResult AcceptVision(
        string workspaceId,
        LeagueVisionObservationBatch batch)
    {
        if (!_states.TryGetValue(workspaceId, out var state))
            return new(false, Guid.Empty, 0, "no-active-match-session");

        lock (state.Sync)
        {
            if (state.SessionId == Guid.Empty || state.Snapshots.Count == 0)
                return new(false, Guid.Empty, 0, "no-active-match-session");

            if (state.LastVisionSequenceByClient.TryGetValue(
                    batch.ClientInstanceId,
                    out var lastSequence) &&
                batch.Sequence <= lastSequence)
            {
                return new(
                    false,
                    state.SessionId,
                    state.VisionLastSeen.Count,
                    "duplicate-or-out-of-order-sequence");
            }

            if (Math.Abs(batch.GameTimeSeconds - state.LastGameTimeSeconds) > 10)
            {
                return new(
                    false,
                    state.SessionId,
                    state.VisionLastSeen.Count,
                    "vision-game-time-outside-current-window");
            }

            state.LastVisionSequenceByClient[batch.ClientInstanceId] =
                batch.Sequence;

            foreach (var observation in batch.Observations)
            {
                var key = VisionKey(
                    observation.Team,
                    observation.Champion);
                var count = state.VisionLastSeen.TryGetValue(
                    key,
                    out var previous)
                    ? previous.ObservationCount + 1
                    : 1;

                state.VisionLastSeen[key] = new(
                    observation.Champion,
                    observation.Team,
                    observation.X,
                    observation.Y,
                    observation.Confidence,
                    batch.GameTimeSeconds,
                    batch.ObservedAtUtc,
                    count);
            }

            var cutoff = state.LastGameTimeSeconds - 180;
            foreach (var key in state.VisionLastSeen
                         .Where(pair => pair.Value.GameTimeSeconds < cutoff)
                         .Select(pair => pair.Key)
                         .ToArray())
                state.VisionLastSeen.Remove(key);

            return new(
                true,
                state.SessionId,
                state.VisionLastSeen.Count,
                "accepted");
        }
    }

    public LeagueVisionState? GetVision(string workspaceId)
    {
        if (!_states.TryGetValue(workspaceId, out var state))
            return null;

        lock (state.Sync)
        {
            if (state.SessionId == Guid.Empty)
                return null;

            return new(
                state.SessionId,
                workspaceId,
                state.VisionLastSeen.Values
                    .OrderBy(item => item.Team)
                    .ThenBy(item => item.Champion)
                    .ToArray());
        }
    }

    public LeagueMapSituation? GetMapSituation(string workspaceId)
    {
        if (!_states.TryGetValue(workspaceId, out var state))
            return null;

        lock (state.Sync)
        {
            if (state.SessionId == Guid.Empty || state.Snapshots.Count == 0)
                return null;

            var now = state.LastGameTimeSeconds;
            var sightings = state.VisionLastSeen.Values
                .Where(item =>
                    item.Team == "enemy" &&
                    now - item.GameTimeSeconds is >= 0 and <= 45)
                .Select(item => new LeagueMapSighting(
                    item.Champion,
                    item.Team,
                    item.X,
                    item.Y,
                    item.Confidence,
                    item.GameTimeSeconds,
                    Math.Max(0, now - item.GameTimeSeconds),
                    item.ObservationCount))
                .OrderBy(item => item.LastSeenAgeSeconds)
                .ThenBy(item => item.Champion)
                .ToArray();

            return new(
                state.SessionId,
                workspaceId,
                now,
                sightings.Count(item => item.LastSeenAgeSeconds <= 15),
                sightings,
                EvidenceOnly: true,
                HiddenPositionInference: false);
        }
    }

    public LeagueMatchSessionSummary? GetSession(string workspaceId)
    {
        if (!_states.TryGetValue(workspaceId, out var state))
            return null;

        lock (state.Sync)
        {
            if (state.SessionId == Guid.Empty || state.Snapshots.Count == 0)
                return null;

            var latest = state.Snapshots.Last();
            var age = DateTimeOffset.UtcNow - state.LastObservedAtUtc;
            var sessionState = age > TimeSpan.FromSeconds(10)
                ? LeagueMatchSessionStates.Stale
                : latest.IsDead
                    ? LeagueMatchSessionStates.Dead
                    : LeagueMatchSessionStates.Active;

            return new(
                state.SessionId,
                workspaceId,
                sessionState,
                state.StartedAtUtc,
                state.LastObservedAtUtc,
                state.LastGameTimeSeconds,
                state.Snapshots.Count,
                latest);
        }
    }

    public LeagueMatchEventFeed? GetEvents(string workspaceId)
    {
        if (!_states.TryGetValue(workspaceId, out var state))
            return null;

        lock (state.Sync)
        {
            if (state.SessionId == Guid.Empty)
                return null;

            return new(
                state.SessionId,
                workspaceId,
                state.Events.ToArray());
        }
    }

    private static IReadOnlyList<LeagueMatchEvent> BuildEvents(
        Guid sessionId,
        LeagueMatchSnapshot? previous,
        LeagueMatchSnapshot current)
    {
        if (previous is null)
            return Array.Empty<LeagueMatchEvent>();

        var events = new List<LeagueMatchEvent>();

        void Add(
            string kind,
            string severity,
            string message,
            params string[] evidence) =>
            events.Add(new(
                Guid.NewGuid(),
                sessionId,
                kind,
                severity,
                current.GameTimeSeconds,
                current.ObservedAtUtc,
                message,
                evidence));

        if (!previous.IsDead && current.IsDead)
            Add(
                "own-death",
                "critical",
                "Người chơi vừa bị hạ gục.",
                $"gameTime={current.GameTimeSeconds:0.0}s");

        if (previous.IsDead && !current.IsDead)
            Add(
                "own-respawn",
                "info",
                "Người chơi vừa hồi sinh.",
                $"gameTime={current.GameTimeSeconds:0.0}s");

        if (current.Level > previous.Level)
            Add(
                "own-level-up",
                "info",
                $"Cấp độ tăng từ {previous.Level} lên {current.Level}.",
                $"level={current.Level}");

        var dt = current.GameTimeSeconds - previous.GameTimeSeconds;
        if (!current.IsDead &&
            !previous.IsDead &&
            dt > 0 &&
            dt <= 5 &&
            previous.MaxHealth > 0 &&
            current.MaxHealth > 0)
        {
            var previousHealthPercent =
                100d * previous.Health / previous.MaxHealth;
            var currentHealthPercent =
                100d * current.Health / current.MaxHealth;
            var loss = previousHealthPercent - currentHealthPercent;

            if (loss >= 20)
                Add(
                    "own-health-drop-high",
                    loss >= 35 ? "critical" : "high",
                    $"Máu vừa giảm {loss:0}% trong {dt:0.0} giây.",
                    $"hpBefore={previousHealthPercent:0.0}%",
                    $"hpNow={currentHealthPercent:0.0}%",
                    $"observedWindow={dt:0.0}s");

            if (previousHealthPercent > 25 &&
                currentHealthPercent <= 25)
                Add(
                    "own-health-low",
                    "high",
                    $"Máu hiện còn {currentHealthPercent:0}%.",
                    $"hpNow={currentHealthPercent:0.0}%");
        }

        if (!current.IsDead &&
            current.ResourceType.Equals(
                "MANA",
                StringComparison.OrdinalIgnoreCase) &&
            previous.MaxResource > 0 &&
            current.MaxResource > 0)
        {
            var previousResourcePercent =
                100d * previous.Resource / previous.MaxResource;
            var currentResourcePercent =
                100d * current.Resource / current.MaxResource;

            if (previousResourcePercent > 20 &&
                currentResourcePercent <= 20)
                Add(
                    "own-mana-low",
                    "medium",
                    $"Năng lượng hiện còn {currentResourcePercent:0}%.",
                    $"manaNow={currentResourcePercent:0.0}%");
        }

        if (previous.Gold < 1200 && current.Gold >= 1200)
            Add(
                "own-gold-high",
                "low",
                $"Vàng hiện có đã đạt {current.Gold:0}.",
                $"gold={current.Gold:0}");

        return events;
    }

    public LeagueMatchTimeline? GetTimeline(string workspaceId)
    {
        if (!_states.TryGetValue(workspaceId, out var state))
            return null;

        lock (state.Sync)
        {
            if (state.SessionId == Guid.Empty || state.Snapshots.Count == 0)
                return null;

            return new(
                state.SessionId,
                workspaceId,
                state.StartedAtUtc,
                state.LastObservedAtUtc,
                state.LastGameTimeSeconds,
                state.Snapshots.ToArray());
        }
    }

    private static string VisionKey(string team, string champion) =>
        team.Trim().ToLowerInvariant() + ":" +
        champion.Trim().ToLowerInvariant();

    private sealed class WorkspaceState
    {
        public object Sync { get; } = new();
        public Guid SessionId { get; set; }
        public DateTimeOffset StartedAtUtc { get; set; }
        public DateTimeOffset LastObservedAtUtc { get; set; }
        public double LastGameTimeSeconds { get; set; } = -1;
        public Queue<LeagueMatchSnapshot> Snapshots { get; } = new();
        public Queue<LeagueMatchEvent> Events { get; } = new();
        public Dictionary<string, LeagueChampionLastSeen> VisionLastSeen { get; } =
            new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, long> LastSequenceByClient { get; } =
            new(StringComparer.Ordinal);
        public Dictionary<string, long> LastVisionSequenceByClient { get; } =
            new(StringComparer.Ordinal);
    }
}
