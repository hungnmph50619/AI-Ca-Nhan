namespace PersonalAI.Web.Services;

/// <summary>
/// Quyền điều khiển có thời hạn và ngân sách thao tác; khởi động luôn khóa.
/// Chỉ chặn thao tác của dịch vụ này, không phải dừng khẩn cấp toàn Windows.
/// </summary>
public sealed class ComputerControlGate
{
    public const int MaximumActionsPerSession = 5;
    public const int MaximumSessionSeconds = 60;
    public const int DelegatedMaximumActions = 120;
    public const int DelegatedMaximumMinutes = 30;
    private static readonly TimeSpan SessionDuration =
        TimeSpan.FromSeconds(MaximumSessionSeconds);

    private readonly object _synchronization = new();
    private bool _paused = true;
    private bool _stopHotkeyAvailable;
    private DateTimeOffset? _expiresAt;
    private int _remainingActions;
    private bool _delegated;
    private bool _allowExternalAiContext;

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
                !_paused && _delegated,
                !_paused && _delegated && _allowExternalAiContext);
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
                PauseInternal();
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
            if (!_stopHotkeyAvailable)
                throw new ToolExecutionInputException(
                    "Không thể cho phép điều khiển: phím dừng Ctrl + Shift + F12 chưa sẵn sàng. Hãy chạy ứng dụng trong phiên Windows đang tương tác.");
            _paused = false;
            _delegated = false;
            _allowExternalAiContext = false;
            _expiresAt = DateTimeOffset.UtcNow + SessionDuration;
            _remainingActions = MaximumActionsPerSession;
            return new ComputerControlSessionStatus(
                false, _expiresAt, _remainingActions,
                _stopHotkeyAvailable,
                false,
                false);
        }
    }

    public ComputerControlSessionStatus EnableDelegatedOperator(
        bool allowExternalAiContext)
    {
        lock (_synchronization)
        {
            if (!_stopHotkeyAvailable)
                throw new ToolExecutionInputException(
                    "Không thể ủy quyền Computer Operator vì phím dừng Ctrl + Shift + F12 chưa sẵn sàng.");

            _paused = false;
            _delegated = true;
            _allowExternalAiContext = allowExternalAiContext;
            _expiresAt = DateTimeOffset.UtcNow
                + TimeSpan.FromMinutes(DelegatedMaximumMinutes);
            _remainingActions = DelegatedMaximumActions;

            return new ComputerControlSessionStatus(
                false,
                _expiresAt,
                _remainingActions,
                _stopHotkeyAvailable,
                true,
                _allowExternalAiContext);
        }
    }

    public bool CanAutoApproveComputerTool(
        ToolDefinition definition)
    {
        lock (_synchronization)
        {
            ExpireIfNeeded();
            if (_paused || !_delegated)
                return false;

            if (!definition.Name.StartsWith(
                    "computer.",
                    StringComparison.OrdinalIgnoreCase))
                return false;

            return definition.RequiredPermissions.All(permission =>
                permission.Equals(ToolPermissions.Read, StringComparison.OrdinalIgnoreCase)
                || permission.Equals(ToolPermissions.Write, StringComparison.OrdinalIgnoreCase)
                || permission.Equals(ToolPermissions.Sensitive, StringComparison.OrdinalIgnoreCase)
                || permission.Equals(ToolPermissions.Computer, StringComparison.OrdinalIgnoreCase)
                || permission.Equals(ToolPermissions.External, StringComparison.OrdinalIgnoreCase));
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
            _delegated = false;
            _allowExternalAiContext = false;
            _expiresAt = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(seconds);
            _remainingActions = actions;
            return new ComputerControlSessionStatus(
                false,
                _expiresAt,
                _remainingActions,
                _stopHotkeyAvailable,
                false,
                false);
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
                PauseInternal();
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
        _delegated = false;
        _allowExternalAiContext = false;
        _expiresAt = null;
        _remainingActions = 0;
    }
}

public sealed record ComputerControlSessionStatus(
    bool Paused,
    DateTimeOffset? ExpiresAt,
    int RemainingActions,
    bool StopHotkeyAvailable = false,
    bool DelegatedOperator = false,
    bool AllowExternalAiContext = false);
