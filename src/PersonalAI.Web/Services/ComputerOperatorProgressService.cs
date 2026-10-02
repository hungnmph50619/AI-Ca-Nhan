namespace PersonalAI.Web.Services;

public sealed record ComputerOperatorProgressEntry(
    DateTimeOffset AtUtc,
    string Stage,
    string Message,
    string? Action = null,
    double? Confidence = null,
    long? ElapsedSincePreviousMs = null);

public sealed record ComputerOperatorProgressSnapshot(
    bool Active,
    bool Paused,
    string Status,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    int ObservationCount,
    int ActionCount,
    string CurrentStage,
    bool Stale,
    int StaleSeconds,
    IReadOnlyList<ComputerOperatorProgressEntry> Entries);

public sealed class ComputerOperatorProgressStore(
    ILogger<ComputerOperatorProgressStore> logger)
{
    private const int MaximumEntries = 60;
    private static readonly TimeSpan StaleThreshold = TimeSpan.FromSeconds(10);
    private readonly object _sync = new();
    private readonly List<ComputerOperatorProgressEntry> _entries = [];
    private bool _active;
    private bool _paused;
    private string _status = "idle";
    private string _currentStage = "idle";
    private DateTimeOffset? _startedAtUtc;
    private DateTimeOffset _updatedAtUtc = DateTimeOffset.UtcNow;
    private DateTimeOffset? _previousEntryAtUtc;
    private int _observationCount;
    private int _actionCount;

    public void Start(string message)
    {
        lock (_sync)
        {
            _entries.Clear();
            _active = true;
            _paused = false;
            _status = "running";
            _currentStage = "start";
            _startedAtUtc = DateTimeOffset.UtcNow;
            _updatedAtUtc = _startedAtUtc.Value;
            _previousEntryAtUtc = null;
            _observationCount = 0;
            _actionCount = 0;
            AddCore("start", message);
            LogCore("start", message);
        }
    }

    public void Add(
        string stage,
        string message,
        string? action = null,
        double? confidence = null,
        bool observation = false,
        bool actionTaken = false)
    {
        lock (_sync)
        {
            if (observation) _observationCount++;
            if (actionTaken) _actionCount++;
            if (_active && !_paused)
                _status = "running";
            _currentStage = stage;
            AddCore(stage, message, action, confidence);
            LogCore(stage, message, action, confidence);
        }
    }

    public void Pause(string message = "Đã tạm dừng theo yêu cầu người dùng.")
    {
        lock (_sync)
        {
            if (!_active)
                return;
            _paused = true;
            _status = "paused";
            _currentStage = "paused";
            AddCore("paused", message);
            LogCore("paused", message);
        }
    }

    public void Resume(string message = "Đã tiếp tục tác vụ.")
    {
        lock (_sync)
        {
            if (!_active)
                return;
            _paused = false;
            _status = "running";
            _currentStage = "resume";
            AddCore("resume", message);
            LogCore("resume", message);
        }
    }

    public void Complete(string message)
    {
        lock (_sync)
        {
            _active = false;
            _paused = false;
            _status = "completed";
            _currentStage = "complete";
            AddCore("complete", message);
            LogCore("complete", message);
        }
    }

    public void StopByUser(string message = "Người dùng đã dừng tác vụ.")
    {
        lock (_sync)
        {
            _active = false;
            _paused = false;
            _status = "stopped";
            _currentStage = "stopped";
            AddCore("stopped", message);
            LogCore("stopped", message);
        }
    }

    public void Block(string message)
    {
        lock (_sync)
        {
            _active = false;
            _paused = false;
            _status = "blocked";
            _currentStage = "blocked";
            AddCore("blocked", message);
            LogCore("blocked", message);
        }
    }

    public ComputerOperatorProgressSnapshot Get()
    {
        lock (_sync)
        {
            var now = DateTimeOffset.UtcNow;
            var staleFor = now - _updatedAtUtc;
            var stale = _active && !_paused && staleFor >= StaleThreshold;
            return new(
                _active,
                _paused,
                _status,
                _startedAtUtc,
                _updatedAtUtc,
                _observationCount,
                _actionCount,
                _currentStage,
                stale,
                stale ? Math.Max(0, (int)staleFor.TotalSeconds) : 0,
                _entries.ToArray());
        }
    }

    private void AddCore(
        string stage,
        string message,
        string? action = null,
        double? confidence = null)
    {
        var now = DateTimeOffset.UtcNow;
        long? elapsed = _previousEntryAtUtc.HasValue
            ? Math.Max(0L, (long)(now - _previousEntryAtUtc.Value).TotalMilliseconds)
            : null;

        _updatedAtUtc = now;
        _previousEntryAtUtc = now;
        _entries.Add(new(
            now,
            stage,
            message,
            action,
            confidence,
            elapsed));

        if (_entries.Count > MaximumEntries)
            _entries.RemoveRange(
                0,
                _entries.Count - MaximumEntries);
    }

    private void LogCore(
        string stage,
        string message,
        string? action = null,
        double? confidence = null)
    {
        logger.LogInformation(
            "[ComputerOperator] {Stage} | action={Action} | confidence={Confidence} | {Message}",
            stage.ToUpperInvariant(),
            action ?? "-",
            confidence is double value
                ? value.ToString("0.00")
                : "-",
            message);
    }
}
