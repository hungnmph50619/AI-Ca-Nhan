namespace PersonalAI.Web.Services;

/// <summary>
/// Chốt dừng các thao tác điều khiển máy tính. Mỗi lần khởi động lại luôn khóa.
/// Chỉ chặn các thao tác đi qua dịch vụ này; không phải phím dừng toàn hệ thống.
/// </summary>
public sealed class ComputerControlGate
{
    private readonly object _synchronization = new();
    private bool _paused = true;

    public bool Paused
    {
        get
        {
            lock (_synchronization)
            {
                return _paused;
            }
        }
    }

    public void Stop()
    {
        lock (_synchronization)
        {
            _paused = true;
        }
    }

    public void Enable()
    {
        lock (_synchronization)
        {
            _paused = false;
        }
    }

    /// <summary>
    /// Đồng bộ lệnh ngắn với thao tác dừng; lệnh đã bắt đầu có thể kết thúc
    /// trước khi Stop trả về. Không dùng cho thao tác chạy lâu hay vòng lặp AI.
    /// </summary>
    public T RunAllowed<T>(Func<T> action)
    {
        lock (_synchronization)
        {
            if (_paused)
            {
                throw new ToolExecutionInputException(
                    "Điều khiển máy tính đang tạm dừng. Hãy mở mục Máy tính và cho phép lại bằng tay.");
            }

            return action();
        }
    }
}
