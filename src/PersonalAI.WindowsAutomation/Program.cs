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
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
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
    int WaitMilliseconds = 0,
    int MaxNodes = 200,
    int MaxDepth = 6);

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
    string EventWindowId = "",
    string StructuredJson = "",
    string OcrJson = "");

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
                         "ocr-window",
                         StringComparison.OrdinalIgnoreCase))
            {
                response =
                    await OcrWindowAsync(
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
            else if (request.Operation.Trim().Equals(
                         "read-uia-tree",
                         StringComparison.OrdinalIgnoreCase))
            {
                using var automation =
                    new UIA3Automation();

                response =
                    ReadUiaTree(
                        automation,
                        request);
            }
            else if (IsStructuredPatternOperation(
                         request.Operation))
            {
                using var automation =
                    new UIA3Automation();

                response =
                    ExecuteStructuredPattern(
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

    private static async Task<AutomationResponse> OcrWindowAsync(
        AutomationRequest request)
    {
        var captured =
            await CaptureWindowAsync(
                request);

        if (!captured.Success ||
            string.IsNullOrWhiteSpace(
                captured.JpegBase64))
        {
            return captured with
            {
                Detail =
                    $"Windows OCR không capture được cửa sổ: {captured.Detail}"
            };
        }

        OcrEngine? engine;
        try
        {
            engine =
                OcrEngine.TryCreateFromUserProfileLanguages();
        }
        catch (Exception exception)
        {
            return captured with
            {
                Success = false,
                Detail =
                    $"Windows OCR engine không khả dụng: {exception.GetType().Name}: {exception.Message}",
                JpegBase64 = null
            };
        }

        if (engine is null)
        {
            return captured with
            {
                Success = false,
                Detail =
                    "Windows OCR engine không có ngôn ngữ người dùng phù hợp.",
                JpegBase64 = null
            };
        }

        if (captured.CaptureWidth >
                OcrEngine.MaxImageDimension ||
            captured.CaptureHeight >
                OcrEngine.MaxImageDimension)
        {
            return captured with
            {
                Success = false,
                Detail =
                    $"Ảnh {captured.CaptureWidth}x{captured.CaptureHeight} vượt giới hạn Windows OCR {OcrEngine.MaxImageDimension}px.",
                JpegBase64 = null
            };
        }

        try
        {
            var bytes =
                Convert.FromBase64String(
                    captured.JpegBase64);

            using var stream =
                new InMemoryRandomAccessStream();

            using (var writer =
                   new DataWriter(
                       stream.GetOutputStreamAt(0)))
            {
                writer.WriteBytes(
                    bytes);

                await writer.StoreAsync();
                await writer.FlushAsync();
                writer.DetachStream();
            }

            stream.Seek(0);

            var decoder =
                await BitmapDecoder.CreateAsync(
                    stream);

            using var bitmap =
                await decoder.GetSoftwareBitmapAsync(
                    BitmapPixelFormat.Bgra8,
                    BitmapAlphaMode.Premultiplied);

            var result =
                await engine.RecognizeAsync(
                    bitmap);

            var payload =
                new
                {
                    text =
                        result.Text ?? string.Empty,
                    language =
                        engine.RecognizerLanguage?.LanguageTag
                        ?? string.Empty,
                    lines =
                        result.Lines
                            .Select(line =>
                                new
                                {
                                    text =
                                        line.Text
                                        ?? string.Empty,
                                    words =
                                        line.Words
                                            .Select(word =>
                                            {
                                                var rect =
                                                    word.BoundingRect;

                                                return new
                                                {
                                                    text =
                                                        word.Text
                                                        ?? string.Empty,
                                                    left =
                                                        rect.X,
                                                    top =
                                                        rect.Y,
                                                    width =
                                                        rect.Width,
                                                    height =
                                                        rect.Height
                                                };
                                            })
                                            .ToArray()
                                })
                            .ToArray()
                };

            return captured with
            {
                Success = true,
                CanRead = true,
                Detail =
                    $"Windows OCR đọc được {result.Lines.Count} dòng bằng {engine.RecognizerLanguage?.LanguageTag ?? "ngôn ngữ mặc định"}.",
                JpegBase64 = null,
                OcrJson =
                    JsonSerializer.Serialize(
                        payload,
                        JsonOptions())
            };
        }
        catch (Exception exception) when (
            exception is
                ArgumentException or
                InvalidOperationException or
                COMException)
        {
            return captured with
            {
                Success = false,
                Detail =
                    $"Windows OCR thất bại: {exception.GetType().Name}: {exception.Message}",
                JpegBase64 = null
            };
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

    private static AutomationResponse ReadUiaTree(
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
                "Cửa sổ UIA cần đọc không còn hợp lệ.");
        }

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

        var maximumNodes =
            Math.Clamp(
                request.MaxNodes <= 0
                    ? 200
                    : request.MaxNodes,
                20,
                1000);

        var maximumDepth =
            Math.Clamp(
                request.MaxDepth <= 0
                    ? 6
                    : request.MaxDepth,
                1,
                12);

        var nodes =
            new List<StructuredUiNode>(
                Math.Min(
                    maximumNodes,
                    256));

        var rootToken =
            BuildToken(
                root);

        var queue =
            new Queue<(AutomationElement Element, string ParentToken, int Depth)>();

        queue.Enqueue(
            (
                root,
                string.Empty,
                0
            ));

        while (queue.Count > 0 &&
               nodes.Count < maximumNodes)
        {
            var current =
                queue.Dequeue();

            StructuredUiNode? node;
            try
            {
                node =
                    ToStructuredNode(
                        current.Element,
                        current.ParentToken,
                        current.Depth);
            }
            catch
            {
                // Một control bị dispose giữa lúc duyệt cây không được
                // làm hỏng toàn bộ structured snapshot.
                continue;
            }

            nodes.Add(
                node);

            if (current.Depth >= maximumDepth)
                continue;

            AutomationElement[] children;
            try
            {
                children =
                    current.Element.FindAllChildren();
            }
            catch
            {
                continue;
            }

            foreach (var child in children)
            {
                if (nodes.Count +
                    queue.Count >= maximumNodes)
                {
                    break;
                }

                queue.Enqueue(
                    (
                        child,
                        node.Token,
                        current.Depth + 1
                    ));
            }
        }

        var snapshot =
            new StructuredUiSnapshot(
                request.WindowId,
                rootToken,
                DateTimeOffset.UtcNow,
                nodes.Count,
                maximumNodes,
                maximumDepth,
                nodes);

        return new(
            Success: true,
            IsFocused:
                GetForegroundWindow() == hwnd,
            CanRead: true,
            CanDirectSet: false,
            SupportsSelection: false,
            IsReadOnly: true,
            IsSensitive: false,
            ControlClass:
                root.Properties.ClassName.ValueOrDefault
                ?? string.Empty,
            NativeWindowHandle:
                hwnd.ToInt64(),
            TargetToken:
                rootToken,
            Value: null,
            Detail:
                $"Đã đọc UIA3 structured tree với {nodes.Count} node.",
            StructuredJson:
                JsonSerializer.Serialize(
                    snapshot,
                    JsonOptions()));
    }

    private static StructuredUiNode ToStructuredNode(
        AutomationElement element,
        string parentToken,
        int depth)
    {
        var token =
            BuildToken(
                element);

        var bounds =
            element.Properties.BoundingRectangle.ValueOrDefault;

        var patterns =
            new List<string>();

        if (element.Patterns.Value.IsSupported)
            patterns.Add("Value");

        if (element.Patterns.Invoke.IsSupported)
            patterns.Add("Invoke");

        if (element.Patterns.SelectionItem.IsSupported)
            patterns.Add("SelectionItem");

        if (element.Patterns.Toggle.IsSupported)
            patterns.Add("Toggle");

        if (element.Patterns.ExpandCollapse.IsSupported)
            patterns.Add("ExpandCollapse");

        if (element.Patterns.LegacyIAccessible.IsSupported)
            patterns.Add("LegacyIAccessible");

        return new(
            Token: token,
            ParentToken: parentToken,
            Depth: depth,
            Role:
                element.Properties.ControlType.ValueOrDefault
                    .ToString(),
            Name:
                element.Properties.Name.ValueOrDefault
                ?? string.Empty,
            AutomationId:
                element.Properties.AutomationId.ValueOrDefault
                ?? string.Empty,
            ClassName:
                element.Properties.ClassName.ValueOrDefault
                ?? string.Empty,
            IsEnabled:
                element.Properties.IsEnabled.ValueOrDefault,
            IsFocused:
                element.Properties.HasKeyboardFocus.ValueOrDefault,
            IsOffscreen:
                element.Properties.IsOffscreen.ValueOrDefault,
            Left:
                bounds.Left,
            Top:
                bounds.Top,
            Width:
                bounds.Width,
            Height:
                bounds.Height,
            Patterns:
                patterns.ToArray(),
            ToggleState:
                ReadToggleState(element),
            ExpandCollapseState:
                ReadExpandCollapseState(element),
            IsSelected:
                ReadSelectionState(element),
            Value:
                ReadSafeValue(element),
            IsSensitive:
                element.Properties.IsPassword.ValueOrDefault);
    }

    private static string? ReadSafeValue(
        AutomationElement element)
    {
        if (!element.Patterns.Value.IsSupported ||
            element.Properties.IsPassword.ValueOrDefault)
        {
            return null;
        }

        try
        {
            return element.Patterns.Value.Pattern
                .Value.Value;
        }
        catch
        {
            return null;
        }
    }

    private static string ReadToggleState(
        AutomationElement element)
    {
        if (!element.Patterns.Toggle.IsSupported)
            return string.Empty;

        try
        {
            return element.Patterns.Toggle.Pattern
                .ToggleState.ValueOrDefault
                .ToString();
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string ReadExpandCollapseState(
        AutomationElement element)
    {
        if (!element.Patterns.ExpandCollapse.IsSupported)
            return string.Empty;

        try
        {
            return element.Patterns.ExpandCollapse.Pattern
                .ExpandCollapseState.ValueOrDefault
                .ToString();
        }
        catch
        {
            return string.Empty;
        }
    }

    private static bool? ReadSelectionState(
        AutomationElement element)
    {
        if (!element.Patterns.SelectionItem.IsSupported)
            return null;

        try
        {
            return element.Patterns.SelectionItem.Pattern
                .IsSelected.ValueOrDefault;
        }
        catch
        {
            return null;
        }
    }

    private sealed record StructuredUiNode(
        string Token,
        string ParentToken,
        int Depth,
        string Role,
        string Name,
        string AutomationId,
        string ClassName,
        bool IsEnabled,
        bool IsFocused,
        bool IsOffscreen,
        int Left,
        int Top,
        int Width,
        int Height,
        string[] Patterns,
        string ToggleState = "",
        string ExpandCollapseState = "",
        bool? IsSelected = null,
        string? Value = null,
        bool IsSensitive = false);

    private sealed record StructuredUiSnapshot(
        string WindowId,
        string RootToken,
        DateTimeOffset CapturedAtUtc,
        int NodeCount,
        int MaximumNodes,
        int MaximumDepth,
        IReadOnlyList<StructuredUiNode> Nodes);

    private static bool IsStructuredPatternOperation(
        string operation) =>
        (operation ?? string.Empty)
            .Trim()
            .ToLowerInvariant() is
            "structured-focus" or
            "structured-invoke" or
            "structured-select" or
            "structured-toggle" or
            "structured-expand" or
            "structured-collapse" or
            "structured-set-value" or
            "structured-legacy-default";

    private static AutomationResponse ExecuteStructuredPattern(
        UIA3Automation automation,
        AutomationRequest request)
    {
        var hwnd = ParseWindowId(
            request.WindowId);

        if (hwnd == nint.Zero ||
            !IsWindow(hwnd))
        {
            return Empty(
                request,
                "Cửa sổ structured target không còn hợp lệ.");
        }

        if (GetForegroundWindow() != hwnd)
        {
            return Empty(
                request,
                "Cửa sổ structured target không còn ở foreground.");
        }

        if (string.IsNullOrWhiteSpace(
                request.TargetToken))
        {
            return Empty(
                request,
                "Thiếu TargetToken cho structured action.");
        }

        AutomationElement root;
        try
        {
            root = automation.FromHandle(
                hwnd);
        }
        catch (Exception exception)
        {
            return Empty(
                request,
                $"UIA3 không tạo được root cho structured action: {exception.Message}");
        }

        var target = FindByToken(
            root,
            request.TargetToken,
            maximumNodes: 1000,
            maximumDepth: 12);

        if (target is null)
        {
            return Empty(
                request,
                "Structured target không còn tồn tại; yêu cầu re-observe/replan.");
        }

        if (!target.Properties.IsEnabled.ValueOrDefault ||
            target.Properties.IsOffscreen.ValueOrDefault)
        {
            return Empty(
                request,
                "Structured target hiện disabled hoặc offscreen.");
        }

        var token = BuildToken(
            target);

        var className =
            target.Properties.ClassName.ValueOrDefault
            ?? string.Empty;

        var nativeHandle =
            target.Properties.NativeWindowHandle.ValueOrDefault
                .ToInt64();

        var isPassword =
            target.Properties.IsPassword.ValueOrDefault;

        var operation =
            request.Operation.Trim().ToLowerInvariant();

        try
        {
            switch (operation)
            {
                case "structured-focus":
                    target.Focus();
                    break;

                case "structured-invoke":
                    if (!target.Patterns.Invoke.IsSupported)
                        return Empty(
                            request,
                            "Structured target không hỗ trợ InvokePattern.");

                    target.Patterns.Invoke.Pattern.Invoke();
                    break;

                case "structured-select":
                    if (!target.Patterns.SelectionItem.IsSupported)
                        return Empty(
                            request,
                            "Structured target không hỗ trợ SelectionItemPattern.");

                    target.Patterns.SelectionItem.Pattern.Select();
                    break;

                case "structured-toggle":
                    if (!target.Patterns.Toggle.IsSupported)
                        return Empty(
                            request,
                            "Structured target không hỗ trợ TogglePattern.");

                    target.Patterns.Toggle.Pattern.Toggle();
                    break;

                case "structured-expand":
                    if (!target.Patterns.ExpandCollapse.IsSupported)
                        return Empty(
                            request,
                            "Structured target không hỗ trợ ExpandCollapsePattern.");

                    target.Patterns.ExpandCollapse.Pattern.Expand();
                    break;

                case "structured-collapse":
                    if (!target.Patterns.ExpandCollapse.IsSupported)
                        return Empty(
                            request,
                            "Structured target không hỗ trợ ExpandCollapsePattern.");

                    target.Patterns.ExpandCollapse.Pattern.Collapse();
                    break;

                case "structured-set-value":
                    if (isPassword)
                        return Empty(
                            request,
                            "Từ chối structured-set-value trên trường password/nhạy cảm.");

                    if (!target.Patterns.Value.IsSupported)
                        return Empty(
                            request,
                            "Structured target không hỗ trợ ValuePattern.");

                    var valuePattern =
                        target.Patterns.Value.Pattern;

                    if (valuePattern.IsReadOnly.ValueOrDefault)
                        return Empty(
                            request,
                            "Structured ValuePattern đang read-only.");

                    valuePattern.SetValue(
                        request.Text ?? string.Empty);
                    break;

                case "structured-legacy-default":
                    if (!target.Patterns.LegacyIAccessible.IsSupported)
                        return Empty(
                            request,
                            "Structured target không hỗ trợ LegacyIAccessiblePattern.");

                    target.Patterns.LegacyIAccessible.Pattern
                        .DoDefaultAction();
                    break;

                default:
                    return Empty(
                        request,
                        $"Structured operation chưa được hỗ trợ: {request.Operation}.");
            }
        }
        catch (Exception exception)
        {
            return Empty(
                request,
                $"Structured UIA action thất bại: {exception.GetType().Name}: {exception.Message}");
        }

        string? value = null;
        var canRead =
            target.Patterns.Value.IsSupported &&
            !isPassword;

        if (canRead)
        {
            try
            {
                value =
                    target.Patterns.Value.Pattern
                        .Value.Value;
            }
            catch
            {
                value = null;
            }
        }

        return new(
            Success: true,
            IsFocused:
                target.Properties.HasKeyboardFocus.ValueOrDefault,
            CanRead: canRead,
            CanDirectSet:
                target.Patterns.Value.IsSupported &&
                !isPassword,
            SupportsSelection:
                target.Patterns.SelectionItem.IsSupported,
            IsReadOnly:
                target.Patterns.Value.IsSupported
                    ? target.Patterns.Value.Pattern
                        .IsReadOnly.ValueOrDefault
                    : true,
            IsSensitive:
                isPassword,
            ControlClass:
                className,
            NativeWindowHandle:
                nativeHandle,
            TargetToken:
                token,
            Value:
                value,
            Detail:
                $"Đã thực hiện {operation} bằng FlaUI/UIA3 trên target token đã revalidate.");
    }

    private static AutomationElement? FindByToken(
        AutomationElement root,
        string targetToken,
        int maximumNodes,
        int maximumDepth)
    {
        var queue =
            new Queue<(AutomationElement Element, int Depth)>();

        queue.Enqueue(
            (root, 0));

        var visited = 0;

        while (queue.Count > 0 &&
               visited < maximumNodes)
        {
            var current =
                queue.Dequeue();

            visited++;

            string token;
            try
            {
                token = BuildToken(
                    current.Element);
            }
            catch
            {
                continue;
            }

            if (token.Equals(
                    targetToken,
                    StringComparison.Ordinal))
            {
                return current.Element;
            }

            if (current.Depth >= maximumDepth)
                continue;

            AutomationElement[] children;
            try
            {
                children =
                    current.Element.FindAllChildren();
            }
            catch
            {
                continue;
            }

            foreach (var child in children)
            {
                if (visited +
                    queue.Count >= maximumNodes)
                {
                    break;
                }

                queue.Enqueue(
                    (child, current.Depth + 1));
            }
        }

        return null;
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
