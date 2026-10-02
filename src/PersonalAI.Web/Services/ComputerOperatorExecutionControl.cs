namespace PersonalAI.Web.Services;

public sealed class ComputerOperatorExecutionControl : IDisposable
{
    private readonly object _sync = new();
    private CancellationTokenSource? _stop;
    private bool _running;
    private bool _paused;
    private string _taskName = string.Empty;

    public string TaskName
    {
        get { lock (_sync) return _taskName; }
    }

    public bool Running
    {
        get { lock (_sync) return _running; }
    }

    public bool Paused
    {
        get { lock (_sync) return _paused; }
    }

    public CancellationToken Begin(string taskName)
    {
        lock (_sync)
        {
            _stop?.Cancel();
            _stop?.Dispose();
            _stop = new CancellationTokenSource();
            _running = true;
            _paused = false;
            _taskName = (taskName ?? string.Empty).Trim();
            return _stop.Token;
        }
    }

    public bool Pause()
    {
        lock (_sync)
        {
            if (!_running || _paused)
                return false;

            _paused = true;
            return true;
        }
    }

    public bool Resume()
    {
        lock (_sync)
        {
            if (!_running || !_paused)
                return false;

            _paused = false;
            return true;
        }
    }

    public bool Stop()
    {
        lock (_sync)
        {
            if (!_running)
                return false;

            _running = false;
            _paused = false;
            _stop?.Cancel();
            return true;
        }
    }

    public void Complete()
    {
        lock (_sync)
        {
            _running = false;
            _paused = false;
        }
    }

    public async Task WaitIfPausedAsync(
        CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            lock (_sync)
            {
                if (!_running)
                    throw new OperationCanceledException(
                        "Computer Operator đã bị dừng.");

                if (!_paused)
                    return;
            }

            await Task.Delay(150, cancellationToken);
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            _stop?.Cancel();
            _stop?.Dispose();
            _stop = null;
        }
    }
}
