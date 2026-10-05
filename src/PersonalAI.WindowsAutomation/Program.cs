using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;
using Microsoft.Graphics.Canvas;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Storage.Streams;
using WinRT;
using SharpGen.Runtime;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using static Vortice.Direct3D11.D3D11;
using static Vortice.DXGI.DXGI;

namespace PersonalAI.WindowsAutomation;

internal sealed record AutomationRequest(
    string Operation,
    string WindowId,
    string? TargetToken = null,
    string? Text = null,
    string? WriteMode = null,
    string? MonitorDevice = null,
    int WaitMilliseconds = 0);

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
    string CaptureBackend = "",
    string EventKind = "",
    string EventWindowId = "");

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
            else if (request.Operation.Trim().Equals(
                         "capture-monitor-dxgi",
                         StringComparison.OrdinalIgnoreCase))
            {
                response = CaptureMonitorDxgi(
                    request);
            }
            else if (request.Operation.Trim().Equals(
                         "wait-uia-event",
                         StringComparison.OrdinalIgnoreCase))
            {
                using var automation =
                    new UIA3Automation();

                response =
                    await WaitForUiaEventAsync(
                        automation,
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

    private static AutomationResponse CaptureMonitorDxgi(
        AutomationRequest request)
    {
        var monitorDevice =
            (request.MonitorDevice ?? string.Empty)
                .Trim();

        if (monitorDevice.Length == 0)
        {
            return Empty(
                request,
                "Thiếu tên monitor cho DXGI Desktop Duplication.");
        }

        var featureLevels = new[]
        {
            FeatureLevel.Level_11_1,
            FeatureLevel.Level_11_0,
            FeatureLevel.Level_10_1,
            FeatureLevel.Level_10_0
        };

        try
        {
            using var factory =
                CreateDXGIFactory1<IDXGIFactory1>();

            for (uint adapterIndex = 0;
                 factory.EnumAdapters1(
                     adapterIndex,
                     out IDXGIAdapter1? adapter).Success;
                 adapterIndex++)
            {
                using (adapter)
                {
                    if (adapter is null)
                        continue;

                    for (uint outputIndex = 0;
                         adapter.EnumOutputs(
                             outputIndex,
                             out IDXGIOutput? output).Success;
                         outputIndex++)
                    {
                        using (output)
                        {
                            if (output is null)
                                continue;

                            var description =
                                output.Description;

                            if (!string.Equals(
                                    description.DeviceName?.TrimEnd('\0'),
                                    monitorDevice,
                                    StringComparison.OrdinalIgnoreCase))
                            {
                                continue;
                            }

                            var createResult =
                                D3D11CreateDevice(
                                    adapter,
                                    DriverType.Unknown,
                                    DeviceCreationFlags.BgraSupport,
                                    featureLevels,
                                    out ID3D11Device device,
                                    out _,
                                    out ID3D11DeviceContext context);

                            createResult.CheckError();

                            using (device)
                            using (context)
                            using (var output1 =
                                   output.QueryInterface<IDXGIOutput1>())
                            using (var duplication =
                                   output1.DuplicateOutput(device))
                            {
                                IDXGIResource? desktopResource = null;
                                var frameAcquired = false;

                                try
                                {
                                    duplication.AcquireNextFrame(
                                            700,
                                            out _,
                                            out desktopResource)
                                        .CheckError();

                                    frameAcquired = true;

                                    if (desktopResource is null)
                                    {
                                        return Empty(
                                            request,
                                            "DXGI không trả desktop resource.");
                                    }

                                    using var source =
                                        desktopResource
                                            .QueryInterface<ID3D11Texture2D>();

                                    var sourceDescription =
                                        source.Description;

                                    if (sourceDescription.Width == 0 ||
                                        sourceDescription.Height == 0 ||
                                        sourceDescription.Width > 12000 ||
                                        sourceDescription.Height > 8000)
                                    {
                                        return Empty(
                                            request,
                                            "DXGI trả frame có geometry không hợp lệ.");
                                    }

                                    if (sourceDescription.Format !=
                                        Format.B8G8R8A8_UNorm)
                                    {
                                        return Empty(
                                            request,
                                            $"DXGI trả format chưa hỗ trợ: {sourceDescription.Format}.");
                                    }

                                    var stagingDescription =
                                        new Texture2DDescription
                                        {
                                            Width =
                                                sourceDescription.Width,
                                            Height =
                                                sourceDescription.Height,
                                            MipLevels = 1,
                                            ArraySize = 1,
                                            Format =
                                                sourceDescription.Format,
                                            SampleDescription =
                                                new SampleDescription(
                                                    1,
                                                    0),
                                            Usage =
                                                ResourceUsage.Staging,
                                            BindFlags =
                                                BindFlags.None,
                                            CPUAccessFlags =
                                                CpuAccessFlags.Read,
                                            MiscFlags =
                                                ResourceOptionFlags.None
                                        };

                                    using var staging =
                                        device.CreateTexture2D(
                                            stagingDescription);

                                    context.CopyResource(
                                        staging,
                                        source);

                                    var mapped =
                                        context.Map(
                                            staging,
                                            0,
                                            MapMode.Read,
                                            Vortice.Direct3D11.MapFlags.None);

                                    try
                                    {
                                        var width =
                                            checked((int)sourceDescription.Width);
                                        var height =
                                            checked((int)sourceDescription.Height);

                                        using var bitmap =
                                            new Bitmap(
                                                width,
                                                height,
                                                PixelFormat.Format32bppArgb);

                                        var bounds =
                                            new Rectangle(
                                                0,
                                                0,
                                                width,
                                                height);

                                        var locked =
                                            bitmap.LockBits(
                                                bounds,
                                                ImageLockMode.WriteOnly,
                                                PixelFormat.Format32bppArgb);

                                        try
                                        {
                                            var rowBytes =
                                                checked(width * 4);

                                            var row =
                                                new byte[rowBytes];

                                            for (var y = 0;
                                                 y < height;
                                                 y++)
                                            {
                                                Marshal.Copy(
                                                    mapped.DataPointer +
                                                    checked(y * (int)mapped.RowPitch),
                                                    row,
                                                    0,
                                                    rowBytes);

                                                Marshal.Copy(
                                                    row,
                                                    0,
                                                    locked.Scan0 +
                                                    checked(y * locked.Stride),
                                                    rowBytes);
                                            }
                                        }
                                        finally
                                        {
                                            bitmap.UnlockBits(
                                                locked);
                                        }

                                        using var stream =
                                            new MemoryStream();

                                        var encoder =
                                            ImageCodecInfo
                                                .GetImageEncoders()
                                                .First(item =>
                                                    item.FormatID ==
                                                    ImageFormat.Jpeg.Guid);

                                        using var quality =
                                            new EncoderParameters(1);

                                        quality.Param[0] =
                                            new EncoderParameter(
                                                System.Drawing.Imaging.Encoder.Quality,
                                                82L);

                                        bitmap.Save(
                                            stream,
                                            encoder,
                                            quality);

                                        var bytes =
                                            stream.ToArray();

                                        if (bytes.Length is < 24 or > 8 * 1024 * 1024)
                                        {
                                            return Empty(
                                                request,
                                                "DXGI frame vượt giới hạn dữ liệu ảnh.");
                                        }

                                        return new(
                                            Success: true,
                                            IsFocused: false,
                                            CanRead: false,
                                            CanDirectSet: false,
                                            SupportsSelection: false,
                                            IsReadOnly: false,
                                            IsSensitive: false,
                                            ControlClass: string.Empty,
                                            NativeWindowHandle: 0,
                                            TargetToken: string.Empty,
                                            Value: null,
                                            Detail:
                                                $"Đã capture monitor {monitorDevice} bằng DXGI Desktop Duplication.",
                                            JpegBase64:
                                                Convert.ToBase64String(bytes),
                                            CaptureWidth: width,
                                            CaptureHeight: height,
                                            CaptureBackend:
                                                "dxgi-desktop-duplication");
                                    }
                                    finally
                                    {
                                        context.Unmap(
                                            staging,
                                            0);
                                    }
                                }
                                finally
                                {
                                    desktopResource?.Dispose();

                                    if (frameAcquired)
                                    {
                                        try
                                        {
                                            _ = duplication.ReleaseFrame();
                                        }
                                        catch
                                        {
                                            // Best effort; duplication bị hủy ngay sau request.
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }

            return Empty(
                request,
                $"DXGI không tìm thấy output tương ứng monitor {monitorDevice}.");
        }
        catch (SharpGenException exception)
        {
            return Empty(
                request,
                $"DXGI Desktop Duplication không khả dụng: {exception.ResultCode}.");
        }
        catch (Exception exception) when (
            exception is
                InvalidOperationException or
                ArgumentException or
                ExternalException)
        {
            return Empty(
                request,
                $"DXGI Desktop Duplication lỗi: {exception.GetType().Name}: {exception.Message}");
        }
    }

    private static async Task<AutomationResponse> WaitForUiaEventAsync(
        UIA3Automation automation,
        AutomationRequest request)
    {
        var hwnd =
            ParseWindowId(
                request.WindowId);

        if (hwnd == nint.Zero ||
            !IsWindow(hwnd))
        {
            return Empty(
                request,
                "Cửa sổ UIA cần theo dõi không còn hợp lệ.");
        }

        var waitMilliseconds =
            Math.Clamp(
                request.WaitMilliseconds <= 0
                    ? 500
                    : request.WaitMilliseconds,
                100,
                3000);

        AutomationElement root;
        try
        {
            root =
                automation.FromHandle(
                    hwnd);
        }
        catch (Exception exception)
        {
            return Empty(
                request,
                $"UIA3 không tạo được root từ HWND: {exception.Message}");
        }

        var processId =
            root.Properties.ProcessId.ValueOrDefault;

        var eventReady =
            new TaskCompletionSource<(string Kind, string WindowId, string Detail)>(
                TaskCreationOptions.RunContinuationsAsynchronously);

        var focusHandler =
            automation.RegisterFocusChangedEvent(
                element =>
                {
                    try
                    {
                        if (processId > 0 &&
                            element.Properties.ProcessId.ValueOrDefault != processId)
                        {
                            return;
                        }

                        eventReady.TrySetResult(
                            (
                                "uia-focus-changed",
                                request.WindowId,
                                "UIA3 phát hiện focus accessibility thay đổi."
                            ));
                    }
                    catch
                    {
                        // Event callback không được phép làm hỏng bridge.
                    }
                });

        var propertyHandler =
            root.RegisterPropertyChangedEvent(
                TreeScope.Subtree,
                (_, property, _) =>
                {
                    eventReady.TrySetResult(
                        (
                            "uia-property-changed",
                            request.WindowId,
                            $"UIA3 phát hiện property thay đổi: {property.Name}."
                        ));
                },
                automation.PropertyLibrary.Element.Name,
                automation.PropertyLibrary.Value.Value);

        var structureHandler =
            root.RegisterStructureChangedEvent(
                TreeScope.Subtree,
                (_, changeType, _) =>
                {
                    eventReady.TrySetResult(
                        (
                            "uia-structure-changed",
                            request.WindowId,
                            $"UIA3 phát hiện cấu trúc UI thay đổi: {changeType}."
                        ));
                });

        try
        {
            var completed =
                await Task.WhenAny(
                    eventReady.Task,
                    Task.Delay(
                        waitMilliseconds));

            if (completed != eventReady.Task)
            {
                return Empty(
                    request,
                    $"UIA3 không có event mới trong {waitMilliseconds} ms.");
            }

            var received =
                await eventReady.Task;

            return new(
                Success: true,
                IsFocused: false,
                CanRead: false,
                CanDirectSet: false,
                SupportsSelection: false,
                IsReadOnly: false,
                IsSensitive: false,
                ControlClass: string.Empty,
                NativeWindowHandle: hwnd.ToInt64(),
                TargetToken: string.Empty,
                Value: null,
                Detail: received.Detail,
                EventKind: received.Kind,
                EventWindowId: received.WindowId);
        }
        finally
        {
            try
            {
                focusHandler.Dispose();
            }
            catch
            {
            }

            try
            {
                propertyHandler.Dispose();
            }
            catch
            {
            }

            try
            {
                structureHandler.Dispose();
            }
            catch
            {
            }
        }
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
