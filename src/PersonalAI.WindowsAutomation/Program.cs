using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;
using Microsoft.Graphics.Canvas;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Storage.Streams;
using WinRT;

namespace PersonalAI.WindowsAutomation;

internal sealed record AutomationRequest(
    string Operation,
    string WindowId,
    string? TargetToken = null,
    string? Text = null,
    string? WriteMode = null,
    string? MonitorDevice = null);

internal sealed record AutomationResponse(
    bool Success,
    bool IsFocused,
    bool CanRead,
    bool CanDirectSet,
    bool SupportsSelection,
    bool IsReadOnly,
    bool IsSensitive,
    string ControlClass,
    long NativeWindowHandle,
    string TargetToken,
    string? Value,
    string Detail,
    string? JpegBase64 = null,
    int CaptureWidth = 0,
    int CaptureHeight = 0,
    string CaptureBackend = "");

internal static class Program
{
    private static async Task<int> Main()
    {
        try
        {
            var json = Console.In.ReadToEnd();
            var request = JsonSerializer.Deserialize<AutomationRequest>(
                json,
                JsonOptions());

            if (request is null)
                return WriteError("Không đọc được yêu cầu Windows Automation.");

            AutomationResponse response;
            if (request.Operation.Trim().Equals(
                    "capture-window",
                    StringComparison.OrdinalIgnoreCase))
            {
                response = await CaptureWindowAsync(
                    request);
            }
            else if (request.Operation.Trim().Equals(
                         "capture-monitor",
                         StringComparison.OrdinalIgnoreCase))
            {
                response = await CaptureMonitorAsync(
                    request);
            }
            else
            {
                using var automation = new UIA3Automation();
                response = Execute(
                    automation,
                    request);
            }

            Console.Out.Write(
                JsonSerializer.Serialize(
                    response,
                    JsonOptions()));

            return response.Success ? 0 : 2;
        }
        catch (Exception exception)
        {
            return WriteError(
                $"Windows Automation gặp lỗi: {exception.GetType().Name}: {exception.Message}");
        }
    }

    private static async Task<AutomationResponse> CaptureWindowAsync(
        AutomationRequest request)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(
                10,
                0,
                18362))
        {
            return Empty(
                request,
                "Windows Graphics Capture yêu cầu Windows 10 build 18362 trở lên.");
        }

        var hwnd = ParseWindowId(
            request.WindowId);

        if (hwnd == nint.Zero ||
            !IsWindow(hwnd))
        {
            return Empty(
                request,
                "HWND cần capture không còn hợp lệ.");
        }

        if (!GraphicsCaptureSession.IsSupported())
        {
            return Empty(
                request,
                "Windows Graphics Capture không được hệ thống hỗ trợ.");
        }

        var item = CreateCaptureItemForWindow(
            hwnd);

        if (item is null ||
            item.Size.Width <= 0 ||
            item.Size.Height <= 0)
        {
            return Empty(
                request,
                "Không tạo được GraphicsCaptureItem hợp lệ cho cửa sổ.");
        }

        return await CaptureGraphicsItemAsync(
            request,
            item,
            nativeHandle: hwnd.ToInt64(),
            isFocused: GetForegroundWindow() == hwnd,
            successDetail:
                "Đã capture cửa sổ bằng Windows Graphics Capture.");
    }

    private static async Task<AutomationResponse> CaptureMonitorAsync(
        AutomationRequest request)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(
                10,
                0,
                18362))
        {
            return Empty(
                request,
                "Windows Graphics Capture yêu cầu Windows 10 build 18362 trở lên.");
        }

        if (!GraphicsCaptureSession.IsSupported())
        {
            return Empty(
                request,
                "Windows Graphics Capture không được hệ thống hỗ trợ.");
        }

        var device =
            (request.MonitorDevice ?? string.Empty)
                .Trim();

        if (device.Length == 0)
        {
            return Empty(
                request,
                "Thiếu tên monitor cần capture.");
        }

        var monitor = FindMonitorByDeviceName(
            device);

        if (monitor == nint.Zero)
        {
            return Empty(
                request,
                $"Không tìm thấy monitor {device}.");
        }

        var item = CreateCaptureItemForMonitor(
            monitor);

        if (item is null ||
            item.Size.Width <= 0 ||
            item.Size.Height <= 0)
        {
            return Empty(
                request,
                "Không tạo được GraphicsCaptureItem hợp lệ cho monitor.");
        }

        return await CaptureGraphicsItemAsync(
            request,
            item,
            nativeHandle: monitor.ToInt64(),
            isFocused: false,
            successDetail:
                $"Đã capture monitor {device} bằng Windows Graphics Capture.");
    }

    private static async Task<AutomationResponse> CaptureGraphicsItemAsync(
        AutomationRequest request,
        GraphicsCaptureItem item,
        long nativeHandle,
        bool isFocused,
        string successDetail)
    {
        Direct3D11CaptureFramePool? framePool = null;
        GraphicsCaptureSession? session = null;
        Direct3D11CaptureFrame? frame = null;

        try
        {
            var device = CanvasDevice.GetSharedDevice();
            framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
                device,
                DirectXPixelFormat.B8G8R8A8UIntNormalized,
                2,
                item.Size);

            session = framePool.CreateCaptureSession(
                item);

            try
            {
                session.IsCursorCaptureEnabled = false;
            }
            catch
            {
                // Tùy phiên bản Windows; không phải điều kiện bắt buộc để capture.
            }

            var frameReady =
                new TaskCompletionSource<Direct3D11CaptureFrame>(
                    TaskCreationOptions.RunContinuationsAsynchronously);

            framePool.FrameArrived += OnFrameArrived;
            session.StartCapture();

            var completed = await Task.WhenAny(
                frameReady.Task,
                Task.Delay(
                    TimeSpan.FromMilliseconds(2500)));

            if (completed != frameReady.Task)
            {
                return Empty(
                    request,
                    "Windows Graphics Capture không trả frame trong 2500 ms.");
            }

            frame = await frameReady.Task;
            using var bitmap = CanvasBitmap.CreateFromDirect3D11Surface(
                device,
                frame.Surface);

            using var stream =
                new InMemoryRandomAccessStream();

            await bitmap.SaveAsync(
                stream,
                CanvasBitmapFileFormat.Jpeg,
                0.82f);

            stream.Seek(0);
            using var reader =
                new DataReader(
                    stream.GetInputStreamAt(0));

            if (stream.Size is 0 or > 8 * 1024 * 1024)
            {
                return Empty(
                    request,
                    "Frame WGC có kích thước byte không hợp lệ.");
            }

            await reader.LoadAsync(
                checked((uint)stream.Size));

            var bytes =
                new byte[checked((int)stream.Size)];

            reader.ReadBytes(
                bytes);

            return new(
                Success: true,
                IsFocused: isFocused,
                CanRead: false,
                CanDirectSet: false,
                SupportsSelection: false,
                IsReadOnly: false,
                IsSensitive: false,
                ControlClass: string.Empty,
                NativeWindowHandle: nativeHandle,
                TargetToken: string.Empty,
                Value: null,
                Detail: successDetail,
                JpegBase64: Convert.ToBase64String(bytes),
                CaptureWidth: frame.ContentSize.Width,
                CaptureHeight: frame.ContentSize.Height,
                CaptureBackend: "windows-graphics-capture");

            void OnFrameArrived(
                Direct3D11CaptureFramePool sender,
                object args)
            {
                try
                {
                    var next = sender.TryGetNextFrame();
                    if (next is not null &&
                        !frameReady.TrySetResult(next))
                    {
                        next.Dispose();
                    }
                }
                catch (Exception exception)
                {
                    frameReady.TrySetException(
                        exception);
                }
            }
        }
        catch (Exception exception) when (
            exception is
                COMException or
                InvalidOperationException or
                ArgumentException or
                UnauthorizedAccessException)
        {
            return Empty(
                request,
                $"Windows Graphics Capture không khả dụng: {exception.GetType().Name}: {exception.Message}");
        }
        finally
        {
            frame?.Dispose();
            session?.Dispose();
            framePool?.Dispose();
        }
    }

    private static GraphicsCaptureItem? CreateCaptureItemForWindow(
        nint hwnd)
    {
        var interop =
            GraphicsCaptureItem.As<IGraphicsCaptureItemInterop>();

        var itemPointer =
            interop.CreateForWindow(
                hwnd,
                GraphicsCaptureItemGuid);

        if (itemPointer == nint.Zero)
            return null;

        try
        {
            return GraphicsCaptureItem.FromAbi(
                itemPointer);
        }
        finally
        {
            Marshal.Release(
                itemPointer);
        }
    }

    private static GraphicsCaptureItem? CreateCaptureItemForMonitor(
        nint monitor)
    {
        var interop =
            GraphicsCaptureItem.As<IGraphicsCaptureItemInterop>();

        var itemPointer =
            interop.CreateForMonitor(
                monitor,
                GraphicsCaptureItemGuid);

        if (itemPointer == nint.Zero)
            return null;

        try
        {
            return GraphicsCaptureItem.FromAbi(
                itemPointer);
        }
        finally
        {
            Marshal.Release(
                itemPointer);
        }
    }

    private static nint FindMonitorByDeviceName(
        string deviceName)
    {
        nint found = nint.Zero;

        _ = EnumDisplayMonitors(
            nint.Zero,
            nint.Zero,
            (monitor, _, _, _) =>
            {
                var info = new MonitorInfoEx
                {
                    Size = (uint)Marshal.SizeOf<MonitorInfoEx>()
                };

                if (GetMonitorInfo(
                        monitor,
                        ref info) &&
                    string.Equals(
                        info.DeviceName?.TrimEnd('\0'),
                        deviceName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    found = monitor;
                    return false;
                }

                return true;
            },
            nint.Zero);

        return found;
    }

    private static AutomationResponse Execute(
        UIA3Automation automation,
        AutomationRequest request)
    {
        var expectedWindow = ParseWindowId(
            request.WindowId);

        var foreground = GetForegroundWindow();
        if (foreground == nint.Zero ||
            foreground != expectedWindow)
        {
            return Empty(
                request,
                "Cửa sổ mục tiêu không còn ở foreground.");
        }

        var focused = automation.FocusedElement();
        if (focused is null)
        {
            return Empty(
                request,
                "UI Automation không tìm thấy phần tử đang focus.");
        }

        var token = BuildToken(focused);
        if (!string.IsNullOrWhiteSpace(request.TargetToken) &&
            !token.Equals(
                request.TargetToken,
                StringComparison.Ordinal))
        {
            return Empty(
                request,
                "Phần tử đang focus đã thay đổi so với target đã xác minh.");
        }

        var isPassword =
            focused.Properties.IsPassword.ValueOrDefault;

        var valueSupported =
            focused.Patterns.Value.IsSupported;

        var isReadOnly = true;
        if (valueSupported)
        {
            try
            {
                isReadOnly =
                    focused.Patterns.Value.Pattern
                        .IsReadOnly.ValueOrDefault;
            }
            catch
            {
                isReadOnly = true;
            }
        }

        var canRead =
            valueSupported &&
            !isPassword;

        var canDirectSet =
            valueSupported &&
            !isReadOnly &&
            !isPassword;

        var className =
            focused.Properties.ClassName.ValueOrDefault
            ?? string.Empty;

        var nativeHandle =
            focused.Properties.NativeWindowHandle.ValueOrDefault
                .ToInt64();

        return request.Operation.Trim().ToLowerInvariant() switch
        {
            "probe" => new(
                true,
                true,
                canRead,
                canDirectSet,
                SupportsSelection: false,
                isReadOnly,
                isPassword,
                className,
                nativeHandle,
                token,
                Value: null,
                valueSupported
                    ? "FlaUI UIA3 đã xác định focused element và ValuePattern."
                    : "Focused element không hỗ trợ ValuePattern; cần adapter dự phòng."),

            "read" => Read(
                focused,
                canRead,
                isReadOnly,
                isPassword,
                className,
                nativeHandle,
                token),

            "set" => Set(
                focused,
                request,
                canDirectSet,
                isReadOnly,
                isPassword,
                className,
                nativeHandle,
                token),

            "same-focus" => new(
                true,
                true,
                canRead,
                canDirectSet,
                SupportsSelection: false,
                isReadOnly,
                isPassword,
                className,
                nativeHandle,
                token,
                Value: null,
                "Focused element vẫn trùng target FlaUI đã xác minh."),

            _ => Empty(
                request,
                $"Operation FlaUI không được hỗ trợ: {request.Operation}.")
        };
    }

    private static AutomationResponse Read(
        AutomationElement focused,
        bool canRead,
        bool isReadOnly,
        bool isPassword,
        string className,
        long nativeHandle,
        string token)
    {
        if (!canRead)
        {
            return new(
                false,
                true,
                false,
                false,
                SupportsSelection: false,
                isReadOnly,
                isPassword,
                className,
                nativeHandle,
                token,
                Value: null,
                "Focused element không cho phép đọc ValuePattern an toàn.");
        }

        var value =
            focused.Patterns.Value.Pattern
                .Value.Value;

        return new(
            true,
            true,
            true,
            !isReadOnly && !isPassword,
            SupportsSelection: false,
            isReadOnly,
            isPassword,
            className,
            nativeHandle,
            token,
            value,
            "Đã đọc giá trị trực tiếp bằng FlaUI UIA3 ValuePattern.");
    }

    private static AutomationResponse Set(
        AutomationElement focused,
        AutomationRequest request,
        bool canDirectSet,
        bool isReadOnly,
        bool isPassword,
        string className,
        long nativeHandle,
        string token)
    {
        if (!canDirectSet)
        {
            return new(
                false,
                true,
                !isPassword,
                false,
                SupportsSelection: false,
                isReadOnly,
                isPassword,
                className,
                nativeHandle,
                token,
                Value: null,
                isPassword
                    ? "Từ chối ghi trực tiếp vì đây là trường nhạy cảm/password."
                    : "ValuePattern đang read-only hoặc không hỗ trợ ghi.");
        }

        var text = request.Text ?? string.Empty;
        var mode = (request.WriteMode ?? "replace-all")
            .Trim()
            .ToLowerInvariant();

        if (mode != "replace-all")
        {
            return new(
                false,
                true,
                true,
                true,
                SupportsSelection: false,
                isReadOnly,
                isPassword,
                className,
                nativeHandle,
                token,
                Value: null,
                "FlaUI direct-set hiện chỉ nhận replace-all để tránh sửa text mơ hồ.");
        }

        focused.Patterns.Value.Pattern
            .SetValue(text);

        var actual =
            focused.Patterns.Value.Pattern
                .Value.Value;

        return new(
            true,
            true,
            true,
            true,
            SupportsSelection: false,
            isReadOnly,
            isPassword,
            className,
            nativeHandle,
            token,
            actual,
            actual.Equals(
                text,
                StringComparison.Ordinal)
                ? "FlaUI UIA3 đã ghi và đọc lại đúng giá trị."
                : "FlaUI UIA3 đã ghi nhưng readback chưa khớp hoàn toàn.");
    }

    private static string BuildToken(
        AutomationElement element)
    {
        var runtimeId =
            element.Properties.RuntimeId.ValueOrDefault
            ?? Array.Empty<int>();

        var processId =
            element.Properties.ProcessId.ValueOrDefault;

        var automationId =
            element.Properties.AutomationId.ValueOrDefault
            ?? string.Empty;

        return string.Join(
            ":",
            processId.ToString(CultureInfo.InvariantCulture),
            automationId,
            string.Join(",", runtimeId));
    }

    private static AutomationResponse Empty(
        AutomationRequest request,
        string detail) =>
        new(
            false,
            false,
            false,
            false,
            SupportsSelection: false,
            IsReadOnly: false,
            IsSensitive: false,
            ControlClass: string.Empty,
            NativeWindowHandle: 0,
            TargetToken: request.TargetToken ?? string.Empty,
            Value: null,
            detail);

    private static nint ParseWindowId(
        string windowId)
    {
        var value = (windowId ?? string.Empty)
            .Trim();

        if (value.StartsWith(
                "0x",
                StringComparison.OrdinalIgnoreCase))
            value = value[2..];

        if (!long.TryParse(
                value,
                NumberStyles.HexNumber,
                CultureInfo.InvariantCulture,
                out var numeric) ||
            numeric <= 0)
            throw new InvalidOperationException(
                "Window id không hợp lệ.");

        return new nint(numeric);
    }

    private static int WriteError(
        string detail)
    {
        var response = new AutomationResponse(
            false,
            false,
            false,
            false,
            SupportsSelection: false,
            IsReadOnly: false,
            IsSensitive: false,
            ControlClass: string.Empty,
            NativeWindowHandle: 0,
            TargetToken: string.Empty,
            Value: null,
            detail);

        Console.Out.Write(
            JsonSerializer.Serialize(
                response,
                JsonOptions()));

        return 1;
    }

    private static JsonSerializerOptions JsonOptions() =>
        new(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true
        };

    private delegate bool MonitorEnumProc(
        nint monitor,
        nint hdc,
        nint rect,
        nint data);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfoEx
    {
        public uint Size;
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
        public int WorkLeft;
        public int WorkTop;
        public int WorkRight;
        public int WorkBottom;
        public uint Flags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;
    }

    private static readonly Guid GraphicsCaptureItemGuid =
        new("79C3F95B-31F7-4EC2-A464-632EF5D30760");

    [ComImport]
    [Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IGraphicsCaptureItemInterop
    {
        nint CreateForWindow(
            [In] nint window,
            in Guid iid);

        nint CreateForMonitor(
            [In] nint monitor,
            in Guid iid);
    }

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(
        nint hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayMonitors(
        nint hdc,
        nint clip,
        MonitorEnumProc callback,
        nint data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(
        nint monitor,
        ref MonitorInfoEx info);
}
