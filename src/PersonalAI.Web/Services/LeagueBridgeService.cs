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
                state.LastSequenceByClient.Clear();
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

            state.LastSequenceByClient[snapshot.ClientInstanceId] =
                snapshot.Sequence;
            state.LastGameTimeSeconds = snapshot.GameTimeSeconds;
            state.LastObservedAtUtc = snapshot.ObservedAtUtc;
            state.Snapshots.Enqueue(snapshot);

            while (state.Snapshots.Count > MaximumSnapshots)
                state.Snapshots.Dequeue();

            return new(
                Accepted: true,
                NewMatchSession: newSession,
                MatchSessionId: state.SessionId,
                TimelineCount: state.Snapshots.Count,
                Reason: "accepted");
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

    private sealed class WorkspaceState
    {
        public object Sync { get; } = new();
        public Guid SessionId { get; set; }
        public DateTimeOffset StartedAtUtc { get; set; }
        public DateTimeOffset LastObservedAtUtc { get; set; }
        public double LastGameTimeSeconds { get; set; } = -1;
        public Queue<LeagueMatchSnapshot> Snapshots { get; } = new();
        public Dictionary<string, long> LastSequenceByClient { get; } =
            new(StringComparer.Ordinal);
    }
}
