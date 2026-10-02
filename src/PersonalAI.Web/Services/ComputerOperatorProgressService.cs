namespace PersonalAI.Web.Services;

public sealed record ComputerOperatorProgressEntry(
    DateTimeOffset AtUtc,
    string Stage,
    string Message,
    string? Action = null,
    double? Confidence = null);

public sealed record ComputerOperatorProgressSnapshot(
    bool Active,
    string Status,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    int ObservationCount,
    int ActionCount,
    IReadOnlyList<ComputerOperatorProgressEntry> Entries);

public sealed class ComputerOperatorProgressStore(
    ILogger<ComputerOperatorProgressStore> logger)
{
    private const int MaximumEntries = 30;
    private readonly object _sync = new();
    private readonly List<ComputerOperatorProgressEntry> _entries = [];
    private bool _active;
    private string _status = "idle";
    private DateTimeOffset? _startedAtUtc;
    private DateTimeOffset _updatedAtUtc = DateTimeOffset.UtcNow;
    private int _observationCount;
    private int _actionCount;

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
            AddCore(stage, message, action, confidence);
            LogCore(stage, message, action, confidence);
        }
    }

    public void Complete(string message)
    {
        lock (_sync)
        {
            _active = false;
            _status = "completed";
            AddCore("complete", message);
            LogCore("complete", message);
        }
    }

    public void Block(string message)
    {
        lock (_sync)
        {
            _active = false;
            _status = "blocked";
            AddCore("blocked", message);
            LogCore("blocked", message);
        }
    }

    public ComputerOperatorProgressSnapshot Get()
    {
        lock (_sync)
        {
            return new(
                _active,
                _status,
                _startedAtUtc,
                _updatedAtUtc,
                _observationCount,
                _actionCount,
                _entries.ToArray());
        }
    }

    private void AddCore(
        string stage,
        string message,
        string? action = null,
        double? confidence = null)
    {
        _updatedAtUtc = DateTimeOffset.UtcNow;
        _entries.Add(new(
            _updatedAtUtc,
            stage,
            message,
            action,
            confidence));

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
