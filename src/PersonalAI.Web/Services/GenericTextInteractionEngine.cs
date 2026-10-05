using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace PersonalAI.Web.Services;

public static class TextInteractionWriteModes
{
    public const string ReplaceAll = "replace-all";
    public const string Append = "append";
    public const string ReplaceSelection = "replace-selection";
}

public static class TextInteractionStrategies
{
    public const string AccessibilityDirect = "accessibility-direct";
    public const string ClipboardPaste = "clipboard-paste";
    public const string SendInput = "send-input";
    public const string None = "none";
}

public sealed record TextTargetCapabilities(
    string WindowId,
    nint ControlHandle,
    string ControlClass,
    bool IsFocused,
    bool CanRead,
    bool CanDirectSet,
    bool SupportsSelection,
    bool IsReadOnly,
    bool IsSensitive,
    string Backend,
    string TargetToken = "");

public sealed record TextInteractionRequest(
    string WindowId,
    string Text,
    string WriteMode = TextInteractionWriteModes.ReplaceAll,
    bool AllowClipboard = true,
    bool AllowSendInput = true,
    bool RestoreOnFailure = true);

public sealed record TextInteractionResult(
    bool Applied,
    bool Verified,
    string Strategy,
    bool RepairAttempted,
    bool RolledBack,
    string? ActualText,
    string Detail);

public interface ITextAccessibilityBackend
{
    TextTargetCapabilities Probe(string windowId);
    string? TryRead(TextTargetCapabilities target);
    bool TryDirectSet(
        TextTargetCapabilities target,
        string text,
        string writeMode);
    bool IsSameFocusedTarget(TextTargetCapabilities target);
}

public interface ITextClipboardWriter
{
    bool CanUseSafely(out string reason);
    bool TryPaste(
        TextTargetCapabilities target,
        string text,
        string writeMode,
        out string detail);
}

public interface IGenericTextInteractionEngine
{
    TextInteractionResult Execute(
        TextInteractionRequest request);
}

public sealed class GenericTextInteractionEngine(
    ITextAccessibilityBackend accessibility,
    ITextClipboardWriter clipboard,
    IComputerUseService computer)
    : IGenericTextInteractionEngine
{
    public TextInteractionResult Execute(
        TextInteractionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var expected = NormalizeText(request.Text);
        if (expected.Length == 0)
            throw new ToolExecutionInputException(
                "Generic Text Interaction Engine không nhận chuỗi rỗng.");

        if (expected.Length > WindowsComputerUseService.MaximumGenericTextLength)
            throw new ToolExecutionInputException(
                $"Nội dung nhập vượt {WindowsComputerUseService.MaximumGenericTextLength} ký tự.");

        var mode = NormalizeWriteMode(
            request.WriteMode);

        var target = accessibility.Probe(
            request.WindowId);

        if (!target.IsFocused)
            throw new ToolExecutionInputException(
                "Text target không còn focus; đã dừng trước khi nhập.");

        if (target.IsReadOnly)
            throw new ToolExecutionInputException(
                "Text target đang read-only.");

        if (target.IsSensitive)
            throw new ToolExecutionInputException(
                "Không tự động đọc hoặc ghi trường nhạy cảm/password.");

        var before = target.CanRead
            ? accessibility.TryRead(target)
            : null;

        if (before is not null &&
            TextMatches(
                before,
                expected,
                mode))
        {
            return new(
                Applied: false,
                Verified: true,
                TextInteractionStrategies.None,
                RepairAttempted: false,
                RolledBack: false,
                ActualText: before,
                "Text target đã có đúng nội dung cần thiết.");
        }

        var primary = TryWrite(
            target,
            expected,
            mode,
            request,
            out var primaryStrategy,
            out var primaryDetail);

        if (!primary)
        {
            throw new ToolExecutionInputException(
                $"Không có text writer an toàn khả dụng. {primaryDetail}");
        }

        if (!accessibility.IsSameFocusedTarget(target))
        {
            return TryRollback(
                target,
                before,
                request,
                primaryStrategy,
                "Focus đã đổi ngay sau khi nhập; không tiếp tục sửa text.");
        }

        var after = target.CanRead
            ? accessibility.TryRead(target)
            : null;

        if (!target.CanRead || after is null)
        {
            return new(
                Applied: true,
                Verified: false,
                primaryStrategy,
                RepairAttempted: false,
                RolledBack: false,
                ActualText: null,
                $"Đã nhập bằng {primaryStrategy} nhưng accessibility backend chưa đọc lại được; chuyển sang semantic verification thay vì gõ lại.");
        }

        if (TextMatches(after, expected, mode))
        {
            return new(
                Applied: true,
                Verified: true,
                primaryStrategy,
                RepairAttempted: false,
                RolledBack: false,
                ActualText: after,
                $"Local text verifier xác nhận nội dung đúng bằng {target.Backend}. {primaryDetail}");
        }

        // Một lần sửa duy nhất: replace-all toàn bộ. Không sửa từng ký tự.
        if (!accessibility.IsSameFocusedTarget(target))
        {
            return TryRollback(
                target,
                before,
                request,
                primaryStrategy,
                "Focus đã đổi trước repair; đã hủy local retry.");
        }

        var repaired = TryWrite(
            target,
            expected,
            TextInteractionWriteModes.ReplaceAll,
            request,
            out var repairStrategy,
            out var repairDetail);

        if (!repaired)
        {
            return TryRollback(
                target,
                before,
                request,
                primaryStrategy,
                $"Primary write chưa verify được và repair không khả dụng. {repairDetail}");
        }

        var final = target.CanRead
            ? accessibility.TryRead(target)
            : null;

        if (final is not null &&
            TextMatches(
                final,
                expected,
                TextInteractionWriteModes.ReplaceAll))
        {
            return new(
                Applied: true,
                Verified: true,
                repairStrategy,
                RepairAttempted: true,
                RolledBack: false,
                ActualText: final,
                $"Repair replace-all một lần đã được local verifier xác nhận. {repairDetail}");
        }

        return TryRollback(
            target,
            before,
            request,
            repairStrategy,
            "Đã write + repair một lần nhưng local readback vẫn không khớp; cần Gemini/replan.");
    }

    private bool TryWrite(
        TextTargetCapabilities target,
        string expected,
        string mode,
        TextInteractionRequest request,
        out string strategy,
        out string detail)
    {
        if (!accessibility.IsSameFocusedTarget(target))
        {
            strategy = TextInteractionStrategies.None;
            detail = "Focus guard từ chối write vì target đã đổi.";
            return false;
        }

        if (target.CanDirectSet &&
            accessibility.TryDirectSet(
                target,
                expected,
                mode))
        {
            strategy = TextInteractionStrategies.AccessibilityDirect;
            detail = "Đã ghi trực tiếp qua accessibility backend.";
            return true;
        }

        if (request.AllowClipboard &&
            clipboard.CanUseSafely(out var clipboardReason) &&
            clipboard.TryPaste(
                target,
                expected,
                mode,
                out var clipboardDetail))
        {
            strategy = TextInteractionStrategies.ClipboardPaste;
            detail = clipboardDetail;
            return true;
        }

        if (request.AllowSendInput)
        {
            if (mode == TextInteractionWriteModes.ReplaceAll)
            {
                _ = computer.PressHotkey(
                    target.WindowId,
                    ["CTRL", "A"]);
            }

            _ = computer.TypeText(
                target.WindowId,
                expected);

            strategy = TextInteractionStrategies.SendInput;
            detail = "Đã nhập toàn bộ chuỗi bằng SendInput.";
            return true;
        }

        strategy = TextInteractionStrategies.None;
        detail = "Accessibility direct set không khả dụng; clipboard không an toàn; SendInput bị tắt.";
        return false;
    }

    private TextInteractionResult TryRollback(
        TextTargetCapabilities target,
        string? before,
        TextInteractionRequest request,
        string strategy,
        string reason)
    {
        if (!request.RestoreOnFailure ||
            before is null ||
            !target.CanDirectSet ||
            !accessibility.IsSameFocusedTarget(target))
        {
            return new(
                Applied: true,
                Verified: false,
                strategy,
                RepairAttempted: true,
                RolledBack: false,
                ActualText: target.CanRead
                    ? accessibility.TryRead(target)
                    : null,
                reason);
        }

        var restored = accessibility.TryDirectSet(
            target,
            before,
            TextInteractionWriteModes.ReplaceAll);

        return new(
            Applied: true,
            Verified: false,
            strategy,
            RepairAttempted: true,
            RolledBack: restored,
            ActualText: target.CanRead
                ? accessibility.TryRead(target)
                : null,
            restored
                ? reason + " Đã rollback giá trị trước đó."
                : reason + " Rollback không thành công.");
    }

    private static bool TextMatches(
        string actual,
        string expected,
        string mode)
    {
        var normalizedActual = NormalizeText(actual);
        var normalizedExpected = NormalizeText(expected);

        return mode switch
        {
            TextInteractionWriteModes.Append =>
                normalizedActual.EndsWith(
                    normalizedExpected,
                    StringComparison.Ordinal),

            TextInteractionWriteModes.ReplaceSelection =>
                normalizedActual.Contains(
                    normalizedExpected,
                    StringComparison.Ordinal),

            _ =>
                normalizedActual.Equals(
                    normalizedExpected,
                    StringComparison.Ordinal)
        };
    }

    private static string NormalizeText(
        string value) =>
        (value ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal)
            .Normalize(NormalizationForm.FormC);

    private static string NormalizeWriteMode(
        string? mode)
    {
        var normalized = (mode ?? string.Empty)
            .Trim()
            .ToLowerInvariant();

        return normalized switch
        {
            TextInteractionWriteModes.Append =>
                TextInteractionWriteModes.Append,

            TextInteractionWriteModes.ReplaceSelection =>
                TextInteractionWriteModes.ReplaceSelection,

            _ =>
                TextInteractionWriteModes.ReplaceAll
        };
    }
}

public sealed class WindowsTextAccessibilityBackend
    : ITextAccessibilityBackend
{
    private const int GwlStyle = -16;
    private const long EsReadOnly = 0x0800L;
    private const uint WmGetText = 0x000D;
    private const uint WmGetTextLength = 0x000E;
    private const uint WmSetText = 0x000C;
    private const uint EmSetSel = 0x00B1;
    private const uint EmGetPasswordChar = 0x00D2;

    public TextTargetCapabilities Probe(
        string windowId)
    {
        EnsureWindows();

        var expectedWindow = ParseWindowId(
            windowId);
        var foreground = GetForegroundWindow();

        if (foreground == nint.Zero ||
            foreground != expectedWindow)
        {
            return new(
                windowId,
                nint.Zero,
                string.Empty,
                IsFocused: false,
                CanRead: false,
                CanDirectSet: false,
                SupportsSelection: false,
                IsReadOnly: false,
                IsSensitive: false,
                "win32-accessibility");
        }

        _ = GetWindowThreadProcessId(
            foreground,
            out _);

        var info = new GuiThreadInfo
        {
            Size = (uint)Marshal.SizeOf<GuiThreadInfo>()
        };

        var threadId = GetWindowThreadProcessId(
            foreground,
            out _);

        if (threadId == 0 ||
            !GetGUIThreadInfo(threadId, ref info) ||
            info.Focus == nint.Zero)
        {
            return new(
                windowId,
                nint.Zero,
                string.Empty,
                IsFocused: false,
                CanRead: false,
                CanDirectSet: false,
                SupportsSelection: false,
                IsReadOnly: false,
                IsSensitive: false,
                "win32-accessibility");
        }

        var className = GetClassNameSafe(
            info.Focus);

        var style = GetWindowLongPtr(
            info.Focus,
            GwlStyle).ToInt64();

        var readOnly =
            (style & EsReadOnly) != 0;

        var sensitive =
            SendMessage(
                info.Focus,
                EmGetPasswordChar,
                nint.Zero,
                nint.Zero) != nint.Zero;

        var editableClass =
            className.Contains(
                "Edit",
                StringComparison.OrdinalIgnoreCase) ||
            className.Contains(
                "RichEdit",
                StringComparison.OrdinalIgnoreCase) ||
            className.Contains(
                "RICHEDIT",
                StringComparison.OrdinalIgnoreCase);

        return new(
            windowId,
            info.Focus,
            className,
            IsFocused: true,
            CanRead: editableClass,
            CanDirectSet: editableClass && !readOnly && !sensitive,
            SupportsSelection: editableClass,
            IsReadOnly: readOnly,
            IsSensitive: sensitive,
            "win32-accessibility");
    }

    public string? TryRead(
        TextTargetCapabilities target)
    {
        if (!target.CanRead ||
            target.ControlHandle == nint.Zero)
            return null;

        var length = SendMessage(
            target.ControlHandle,
            WmGetTextLength,
            nint.Zero,
            nint.Zero).ToInt32();

        if (length < 0 ||
            length > WindowsComputerUseService.MaximumGenericTextLength * 4)
            return null;

        var buffer = new StringBuilder(
            Math.Max(2, length + 1));

        _ = SendMessage(
            target.ControlHandle,
            WmGetText,
            new nint(buffer.Capacity),
            buffer);

        return buffer.ToString();
    }

    public bool TryDirectSet(
        TextTargetCapabilities target,
        string text,
        string writeMode)
    {
        if (!target.CanDirectSet ||
            target.ControlHandle == nint.Zero ||
            !IsSameFocusedTarget(target))
            return false;

        var mode = (writeMode ?? string.Empty)
            .Trim()
            .ToLowerInvariant();

        if (mode == TextInteractionWriteModes.Append)
        {
            var current = TryRead(target);
            if (current is null)
                return false;

            text = current + text;
        }

        if (mode == TextInteractionWriteModes.ReplaceSelection)
        {
            // Win32 direct set không có readback selection portable đủ chắc chắn.
            return false;
        }

        var result = SendMessage(
            target.ControlHandle,
            WmSetText,
            nint.Zero,
            text);

        return result != nint.Zero;
    }

    public bool IsSameFocusedTarget(
        TextTargetCapabilities target)
    {
        if (target.ControlHandle == nint.Zero)
            return false;

        var expectedWindow = ParseWindowId(
            target.WindowId);

        if (GetForegroundWindow() != expectedWindow)
            return false;

        var threadId = GetWindowThreadProcessId(
            expectedWindow,
            out _);

        if (threadId == 0)
            return false;

        var info = new GuiThreadInfo
        {
            Size = (uint)Marshal.SizeOf<GuiThreadInfo>()
        };

        return GetGUIThreadInfo(
                   threadId,
                   ref info) &&
               info.Focus == target.ControlHandle;
    }

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
        {
            throw new ToolExecutionInputException(
                "Window id cho text target không hợp lệ.");
        }

        return new nint(numeric);
    }

    private static string GetClassNameSafe(
        nint handle)
    {
        var buffer = new StringBuilder(256);
        var length = GetClassName(
            handle,
            buffer,
            buffer.Capacity);

        return length > 0
            ? buffer.ToString()
            : string.Empty;
    }

    private static void EnsureWindows()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException(
                "Text accessibility backend hiện chỉ chạy trên Windows.");
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct GuiThreadInfo
    {
        public uint Size;
        public uint Flags;
        public nint Active;
        public nint Focus;
        public nint Capture;
        public nint MenuOwner;
        public nint MoveSize;
        public nint Caret;
        public Rect CaretRect;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(
        nint window,
        out uint processId);

    [DllImport("user32.dll")]
    private static extern bool GetGUIThreadInfo(
        uint threadId,
        ref GuiThreadInfo info);

    [DllImport(
        "user32.dll",
        CharSet = CharSet.Unicode)]
    private static extern int GetClassName(
        nint window,
        StringBuilder className,
        int maximumCount);

    [DllImport(
        "user32.dll",
        EntryPoint = "GetWindowLongPtrW",
        SetLastError = true)]
    private static extern nint GetWindowLongPtr64(
        nint window,
        int index);

    [DllImport(
        "user32.dll",
        EntryPoint = "GetWindowLongW",
        SetLastError = true)]
    private static extern int GetWindowLong32(
        nint window,
        int index);

    private static nint GetWindowLongPtr(
        nint window,
        int index) =>
        nint.Size == 8
            ? GetWindowLongPtr64(window, index)
            : new nint(GetWindowLong32(window, index));

    [DllImport("user32.dll")]
    private static extern nint SendMessage(
        nint window,
        uint message,
        nint wParam,
        nint lParam);

    [DllImport(
        "user32.dll",
        CharSet = CharSet.Unicode)]
    private static extern nint SendMessage(
        nint window,
        uint message,
        nint wParam,
        StringBuilder lParam);

    [DllImport(
        "user32.dll",
        CharSet = CharSet.Unicode)]
    private static extern nint SendMessage(
        nint window,
        uint message,
        nint wParam,
        string lParam);
}

public sealed class WindowsTextClipboardWriter(
    IComputerUseService computer)
    : ITextClipboardWriter
{
    private const uint CfUnicodeText = 13;
    private const uint GmemMoveable = 0x0002;
    private const uint WmPaste = 0x0302;

    public bool CanUseSafely(
        out string reason)
    {
        if (!OperatingSystem.IsWindows())
        {
            reason = "Clipboard writer chỉ hỗ trợ Windows.";
            return false;
        }

        var formats = new List<uint>();
        uint current = 0;
        while ((current = EnumClipboardFormats(current)) != 0)
        {
            formats.Add(current);
            if (formats.Count > 16)
                break;
        }

        var safe =
            formats.Count == 0 ||
            formats.All(format =>
                format is CfUnicodeText or 1);

        reason = safe
            ? "Clipboard đang rỗng hoặc chỉ chứa text."
            : "Clipboard chứa format không phải text; không dùng để tránh phá dữ liệu clipboard người dùng.";

        return safe;
    }

    public bool TryPaste(
        TextTargetCapabilities target,
        string text,
        string writeMode,
        out string detail)
    {
        detail = string.Empty;

        if (!target.IsFocused ||
            target.ControlHandle == nint.Zero)
        {
            detail = "Text target không còn focus.";
            return false;
        }

        string? previous = null;
        var hadText = IsClipboardFormatAvailable(
            CfUnicodeText);

        if (hadText)
            previous = ReadClipboardText();

        try
        {
            if (!WriteClipboardText(text))
            {
                detail = "Không ghi được text tạm vào clipboard.";
                return false;
            }

            var mode = (writeMode ?? string.Empty)
                .Trim()
                .ToLowerInvariant();

            if (mode == TextInteractionWriteModes.ReplaceAll)
            {
                _ = computer.PressHotkey(
                    target.WindowId,
                    ["CTRL", "A"]);
            }

            _ = SendMessage(
                target.ControlHandle,
                WmPaste,
                nint.Zero,
                nint.Zero);

            detail = "Đã paste toàn bộ chuỗi bằng clipboard text-only và chuẩn bị khôi phục clipboard.";
            return true;
        }
        finally
        {
            if (hadText)
                _ = WriteClipboardText(
                    previous ?? string.Empty);
            else
                _ = ClearClipboard();
        }
    }

    private static string? ReadClipboardText()
    {
        if (!OpenClipboard(nint.Zero))
            return null;

        try
        {
            var handle = GetClipboardData(
                CfUnicodeText);
            if (handle == nint.Zero)
                return null;

            var pointer = GlobalLock(handle);
            if (pointer == nint.Zero)
                return null;

            try
            {
                return Marshal.PtrToStringUni(pointer);
            }
            finally
            {
                _ = GlobalUnlock(handle);
            }
        }
        finally
        {
            _ = CloseClipboard();
        }
    }

    private static bool WriteClipboardText(
        string value)
    {
        if (!OpenClipboard(nint.Zero))
            return false;

        nint memory = nint.Zero;
        try
        {
            if (!EmptyClipboard())
                return false;

            var bytes = checked(
                (value.Length + 1) * 2);

            memory = GlobalAlloc(
                GmemMoveable,
                new nuint((uint)bytes));

            if (memory == nint.Zero)
                return false;

            var pointer = GlobalLock(memory);
            if (pointer == nint.Zero)
                return false;

            try
            {
                Marshal.Copy(
                    value.ToCharArray(),
                    0,
                    pointer,
                    value.Length);
                Marshal.WriteInt16(
                    pointer,
                    value.Length * 2,
                    0);
            }
            finally
            {
                _ = GlobalUnlock(memory);
            }

            if (SetClipboardData(
                    CfUnicodeText,
                    memory) == nint.Zero)
                return false;

            memory = nint.Zero;
            return true;
        }
        finally
        {
            if (memory != nint.Zero)
                _ = GlobalFree(memory);
            _ = CloseClipboard();
        }
    }

    private static bool ClearClipboard()
    {
        if (!OpenClipboard(nint.Zero))
            return false;

        try
        {
            return EmptyClipboard();
        }
        finally
        {
            _ = CloseClipboard();
        }
    }

    [DllImport("user32.dll")]
    private static extern uint EnumClipboardFormats(
        uint format);

    [DllImport("user32.dll")]
    private static extern bool IsClipboardFormatAvailable(
        uint format);

    [DllImport("user32.dll")]
    private static extern bool OpenClipboard(
        nint newOwner);

    [DllImport("user32.dll")]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll")]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll")]
    private static extern nint GetClipboardData(
        uint format);

    [DllImport("user32.dll")]
    private static extern nint SetClipboardData(
        uint format,
        nint memory);

    [DllImport("kernel32.dll")]
    private static extern nint GlobalAlloc(
        uint flags,
        nuint bytes);

    [DllImport("kernel32.dll")]
    private static extern nint GlobalLock(
        nint memory);

    [DllImport("kernel32.dll")]
    private static extern bool GlobalUnlock(
        nint memory);

    [DllImport("kernel32.dll")]
    private static extern nint GlobalFree(
        nint memory);

    [DllImport("user32.dll")]
    private static extern nint SendMessage(
        nint window,
        uint message,
        nint wParam,
        nint lParam);
}
