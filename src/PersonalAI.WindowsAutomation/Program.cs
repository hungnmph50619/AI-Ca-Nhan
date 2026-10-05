using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;

namespace PersonalAI.WindowsAutomation;

internal sealed record AutomationRequest(
    string Operation,
    string WindowId,
    string? TargetToken = null,
    string? Text = null,
    string? WriteMode = null);

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
    string Detail);

internal static class Program
{
    private static int Main()
    {
        try
        {
            var json = Console.In.ReadToEnd();
            var request = JsonSerializer.Deserialize<AutomationRequest>(
                json,
                JsonOptions());

            if (request is null)
                return WriteError("Không đọc được yêu cầu FlaUI UIA3.");

            using var automation = new UIA3Automation();
            var response = Execute(
                automation,
                request);

            Console.Out.Write(
                JsonSerializer.Serialize(
                    response,
                    JsonOptions()));

            return response.Success ? 0 : 2;
        }
        catch (Exception exception)
        {
            return WriteError(
                $"FlaUI UIA3 gặp lỗi: {exception.GetType().Name}: {exception.Message}");
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

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();
}
