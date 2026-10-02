using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public sealed record LeagueVisualProgressEntry(
    DateTimeOffset AtUtc,
    string Stage,
    string Message,
    string? Action = null,
    double? Confidence = null,
    int? X = null,
    int? Y = null);

public sealed record LeagueVisualProgressSnapshot(
    bool Active,
    string Status,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    int ObservationCount,
    int ClickCount,
    IReadOnlyList<LeagueVisualProgressEntry> Entries);

public sealed class LeagueVisualProgressStore
{
    private const int MaximumEntries = 20;
    private readonly object _sync = new();
    private readonly List<LeagueVisualProgressEntry> _entries = [];
    private bool _active;
    private string _status = "idle";
    private DateTimeOffset? _startedAtUtc;
    private DateTimeOffset _updatedAtUtc = DateTimeOffset.UtcNow;
    private int _observationCount;
    private int _clickCount;

    public void Start(string message)
    {
        lock (_sync)
        {
            _entries.Clear();
            _active = true;
            _status = "running";
            _startedAtUtc = DateTimeOffset.UtcNow;
            _updatedAtUtc = _startedAtUtc.Value;
            _observationCount = 0;
            _clickCount = 0;
            AddCore("start", message);
        }
    }

    public void Add(
        string stage,
        string message,
        string? action = null,
        double? confidence = null,
        int? x = null,
        int? y = null,
        bool observation = false,
        bool click = false)
    {
        lock (_sync)
        {
            if (observation)
                _observationCount++;
            if (click)
                _clickCount++;
            AddCore(stage, message, action, confidence, x, y);
        }
    }

    public void Complete(string message)
    {
        lock (_sync)
        {
            _active = false;
            _status = "completed";
            AddCore("complete", message);
        }
    }

    public void Block(string message)
    {
        lock (_sync)
        {
            _active = false;
            _status = "blocked";
            AddCore("blocked", message);
        }
    }

    public LeagueVisualProgressSnapshot Get()
    {
        lock (_sync)
        {
            return new LeagueVisualProgressSnapshot(
                _active,
                _status,
                _startedAtUtc,
                _updatedAtUtc,
                _observationCount,
                _clickCount,
                _entries.ToArray());
        }
    }

    private void AddCore(
        string stage,
        string message,
        string? action = null,
        double? confidence = null,
        int? x = null,
        int? y = null)
    {
        _updatedAtUtc = DateTimeOffset.UtcNow;
        _entries.Add(new LeagueVisualProgressEntry(
            _updatedAtUtc,
            stage,
            message,
            action,
            confidence,
            x,
            y));

        if (_entries.Count > MaximumEntries)
            _entries.RemoveRange(0, _entries.Count - MaximumEntries);
    }
}
