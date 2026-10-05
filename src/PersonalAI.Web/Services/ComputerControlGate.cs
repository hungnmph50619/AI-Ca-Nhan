namespace PersonalAI.Web.Services;

/// <summary>
/// Quyền điều khiển có thời hạn và ngân sách thao tác; khởi động luôn khóa.
/// Chỉ chặn thao tác của dịch vụ này, không phải dừng khẩn cấp toàn Windows.
/// </summary>
public static class ComputerControlPauseReasons
{
    public const string None = "none";
    public const string ManualStop = "manual-stop";
    public const string HotkeyUnavailable = "hotkey-unavailable";
    public const string LeaseExpired = "lease-expired";
}

public sealed class ComputerControlGate
{
    public const int MaximumActionsPerSession = 5;
    public const int MaximumSessionSeconds = 60;
    private static readonly TimeSpan SessionDuration =
        TimeSpan.FromSeconds(MaximumSessionSeconds);

    private readonly object _synchronization = new();
    private bool _paused = true;
    private bool _stopHotkeyAvailable;
    private DateTimeOffset? _expiresAt;
    private int _remainingActions;
    private string _pauseReason = ComputerControlPauseReasons.None;
    private bool _scopedAutomation;

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
                _paused ? 0 : _remainingActions,
                _stopHotkeyAvailable,
                _pauseReason,
                _scopedAutomation);
        }
    }

    /// <summary>
    /// Chỉ được gọi bởi bộ đăng ký phím Windows. Khi phím dừng không hoạt
    /// động, khóa ngay cả phiên đã được cấp trước đó.
    /// </summary>
    public void SetStopHotkeyAvailable(bool available)
    {
        lock (_synchronization)
        {
            _stopHotkeyAvailable = available;
            if (!available)
                PauseInternal(ComputerControlPauseReasons.HotkeyUnavailable);
        }
    }

    public void Stop()
    {
        lock (_synchronization)
        {
            PauseInternal(ComputerControlPauseReasons.ManualStop);
        }
    }

    public ComputerControlSessionStatus Enable()
    {
        lock (_synchronization)
        {
            if (!_stopHotkeyAvailable)
                throw new ToolExecutionInputException(
                    "Không thể cho phép điều khiển: phím dừng Ctrl + Shift + F12 chưa sẵn sàng. Hãy chạy ứng dụng trong phiên Windows đang tương tác.");
            _paused = false;
            _pauseReason = ComputerControlPauseReasons.None;
            _scopedAutomation = false;
            _expiresAt = DateTimeOffset.UtcNow + SessionDuration;
            _remainingActions = MaximumActionsPerSession;
            return new ComputerControlSessionStatus(
                false, _expiresAt, _remainingActions,
                _stopHotkeyAvailable,
                _pauseReason,
                _scopedAutomation);
        }
    }

    internal ComputerControlSessionStatus EnableScopedAutomation(
        int maximumActions,
        int maximumSeconds)
    {
        lock (_synchronization)
        {
            if (!_stopHotkeyAvailable)
                throw new ToolExecutionInputException(
                    "Không thể chạy tự động hóa desktop vì phím dừng Ctrl + Shift + F12 chưa sẵn sàng.");

            var actions = Math.Clamp(maximumActions, 1, 16);
            var seconds = Math.Clamp(maximumSeconds, 15, 300);
            _paused = false;
            _pauseReason = ComputerControlPauseReasons.None;
            _scopedAutomation = true;
            _expiresAt = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(seconds);
            _remainingActions = actions;
            return new ComputerControlSessionStatus(
                false,
                _expiresAt,
                _remainingActions,
                _stopHotkeyAvailable,
                _pauseReason,
                _scopedAutomation);
        }
    }

    internal bool EnsureScopedAutomationLease(
        int maximumActions,
        int maximumSeconds,
        int minimumRemainingSeconds = 20)
    {
        lock (_synchronization)
        {
            ExpireIfNeeded();

            if (!_stopHotkeyAvailable)
            {
                PauseInternal(ComputerControlPauseReasons.HotkeyUnavailable);
                return false;
            }

            if (_pauseReason is
                ComputerControlPauseReasons.ManualStop or
                ComputerControlPauseReasons.HotkeyUnavailable)
            {
                return false;
            }

            var now = DateTimeOffset.UtcNow;
            var shouldRenew =
                _paused ||
                !_scopedAutomation ||
                !_expiresAt.HasValue ||
                _remainingActions <= 1 ||
                (_expiresAt.Value - now).TotalSeconds <=
                    Math.Max(5, minimumRemainingSeconds);

            if (!shouldRenew)
                return true;

            var actions = Math.Clamp(maximumActions, 1, 16);
            var seconds = Math.Clamp(maximumSeconds, 15, 300);

            _paused = false;
            _pauseReason = ComputerControlPauseReasons.None;
            _scopedAutomation = true;
            _expiresAt = now + TimeSpan.FromSeconds(seconds);
            _remainingActions = actions;
            return true;
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
            if (!_stopHotkeyAvailable)
                PauseInternal(ComputerControlPauseReasons.HotkeyUnavailable);
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
                    || DateTimeOffset.UtcNow >= _expiresAt!.Value)
                {
                    PauseInternal(ComputerControlPauseReasons.LeaseExpired);
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
            PauseInternal(ComputerControlPauseReasons.LeaseExpired);
        }
    }

    private void PauseInternal(
        string reason)
    {
        _paused = true;
        _pauseReason = reason;
        _expiresAt = null;
        _remainingActions = 0;
    }
}

public sealed record ComputerControlSessionStatus(
    bool Paused,
    DateTimeOffset? ExpiresAt,
    int RemainingActions,
    bool StopHotkeyAvailable = false,
    string PauseReason = ComputerControlPauseReasons.None,
    bool ScopedAutomation = false);
