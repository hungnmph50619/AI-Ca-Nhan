namespace PersonalAI.Web.Services;

/// <summary>
/// Quyền điều khiển có thời hạn và ngân sách thao tác; khởi động luôn khóa.
/// Chỉ chặn thao tác của dịch vụ này, không phải dừng khẩn cấp toàn Windows.
/// </summary>
public sealed class ComputerControlGate
{
    public const int MaximumActionsPerSession = 5;
    public const int MaximumSessionSeconds = 60;
    private static readonly TimeSpan SessionDuration =
        TimeSpan.FromSeconds(MaximumSessionSeconds);

    private readonly object _synchronization = new();
    private bool _paused = true;
    private DateTimeOffset? _expiresAt;
    private int _remainingActions;

    public bool Paused
    {
        get
        {
            lock (_synchronization)
            {
                ExpireIfNeeded();
                return _paused;
            }
        }
    }

    public ComputerControlSessionStatus GetStatus()
    {
        lock (_synchronization)
        {
            ExpireIfNeeded();
            return new ComputerControlSessionStatus(
                _paused,
                _paused ? null : _expiresAt,
                _paused ? 0 : _remainingActions);
        }
    }

    public void Stop()
    {
        lock (_synchronization)
        {
            PauseInternal();
        }
    }

    public ComputerControlSessionStatus Enable()
    {
        lock (_synchronization)
        {
            _paused = false;
            _expiresAt = DateTimeOffset.UtcNow + SessionDuration;
            _remainingActions = MaximumActionsPerSession;
            return new ComputerControlSessionStatus(
                false, _expiresAt, _remainingActions);
        }
    }

    /// <summary>
    /// Mỗi lần gọi tốn một suất ngay cả khi Windows từ chối thao tác.
    /// Không dùng cho tác vụ kéo dài; lệnh đang chạy có thể kết thúc
    /// trước khi Stop trả về vì cả hai dùng cùng khóa đồng bộ.
    /// </summary>
    public T RunAllowed<T>(Func<T> action)
    {
        lock (_synchronization)
        {
            ExpireIfNeeded();
            if (_paused)
            {
                throw new ToolExecutionInputException(
                    "Điều khiển máy tính đang tạm dừng hoặc phiên đã hết hạn. Hãy mở mục Máy tính và cho phép lại bằng tay.");
            }

            _remainingActions--;
            try
            {
                return action();
            }
            finally
            {
                if (_remainingActions <= 0
                    || DateTimeOffset.UtcNow >= _expiresAt)
                {
                    PauseInternal();
                }
            }
        }
    }

    private void ExpireIfNeeded()
    {
        if (!_paused &&
            (_remainingActions <= 0 || !_expiresAt.HasValue
                || DateTimeOffset.UtcNow >= _expiresAt.Value))
        {
            PauseInternal();
        }
    }

    private void PauseInternal()
    {
        _paused = true;
        _expiresAt = null;
        _remainingActions = 0;
    }
}

public sealed record ComputerControlSessionStatus(
    bool Paused,
    DateTimeOffset? ExpiresAt,
    int RemainingActions);
