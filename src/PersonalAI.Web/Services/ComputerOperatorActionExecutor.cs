using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public interface IComputerOperatorActionExecutor
{
    ComputerActionResponse Execute(
        DesktopOperatorDecision decision,
        DesktopScreenshotFrame frame);
}

public sealed class ComputerOperatorActionExecutor(
    IComputerUseService computer,
    IComputerCoordinateTransformService coordinates,
    IComputerSafeTargetingService targeting,
    IFlaUiAutomationClient flaUi)
    : IComputerOperatorActionExecutor
{
    public ComputerActionResponse Execute(
        DesktopOperatorDecision decision,
        DesktopScreenshotFrame frame)
    {
        ArgumentNullException.ThrowIfNull(decision);
        ArgumentNullException.ThrowIfNull(frame);

        var action = (decision.Action ?? string.Empty)
            .Trim()
            .ToLowerInvariant();

        var active = computer.GetActiveWindow();

        ComputerCoordinatePoint? point = null;
        ComputerCoordinatePoint? endPoint = null;
        ComputerSafeTargetPoint? safeTarget = null;

        if (IsClickAction(action))
        {
            // A window-scoped screenshot must belong to the grounded window.
            // Otherwise valid-looking image coordinates may hit a different control.
            if (!string.IsNullOrWhiteSpace(frame.WindowId) &&
                !string.IsNullOrWhiteSpace(decision.CoordinateWindowId) &&
                !frame.WindowId.Equals(
                    decision.CoordinateWindowId,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new ToolExecutionInputException(
                    "Screenshot window và grounded target không khớp; cần chụp lại trước khi click.");
            }

            safeTarget = targeting.Resolve(decision, frame);
            point = safeTarget.Point;
        }
        else if (IsPointerAction(action))
        {
            point = coordinates.ToDesktopPoint(
                BuildCoordinateRequest(decision, useEnd: false),
                frame);
        }

        if (action == "drag-left")
        {
            endPoint = coordinates.ToDesktopPoint(
                BuildCoordinateRequest(decision, useEnd: true),
                frame);
        }

        return action switch
        {
            "move-pointer" => computer.SmoothMoveCursor(
                RequirePoint(point).DesktopX,
                RequirePoint(point).DesktopY,
                420),

            "click-left" => ExecutePointerClick(
                decision,
                RequirePoint(point),
                RequireSafeTarget(safeTarget),
                static (service, windowId, x, y) =>
                    service.ClickLeft(windowId, x, y)),

            "double-click-left" => ExecutePointerClick(
                decision,
                RequirePoint(point),
                RequireSafeTarget(safeTarget),
                static (service, windowId, x, y) =>
                    service.DoubleClickLeft(windowId, x, y)),

            "click-right" => ExecutePointerClick(
                decision,
                RequirePoint(point),
                RequireSafeTarget(safeTarget),
                static (service, windowId, x, y) =>
                    service.ClickRight(windowId, x, y)),

            "scroll" => ExecutePointerScroll(
                decision,
                RequirePoint(point)),

            "drag-left" => ExecutePointerDrag(
                RequirePoint(point),
                RequirePoint(endPoint)),

            "focus-window" => computer.FocusWindowByQuery(
                RequireValue(decision.Query, "query")),

            "minimize" => computer.MinimizeWindow(
                RequireActive(active)),

            "maximize" => computer.MaximizeWindow(
                RequireActive(active)),

            "restore" => computer.RestoreWindow(
                RequireActive(active)),

            "type-text" => ExecuteKeyboard(
                active,
                windowId => computer.TypeText(
                    windowId,
                    RequireValue(decision.Text, "text"))),

            "press-key" => ExecuteKeyboard(
                active,
                windowId => computer.PressKey(
                    windowId,
                    RequireValue(decision.Key, "key"))),

            "press-hotkey" => ExecuteKeyboard(
                active,
                windowId => computer.PressHotkey(
                    windowId,
                    decision.Keys.Count > 0
                        ? decision.Keys
                        : throw new ToolExecutionInputException(
                            "Vision không trả danh sách hotkey."))),

            "open-browser" => computer.OpenDefaultBrowser(
                string.IsNullOrWhiteSpace(decision.Url)
                    ? null
                    : decision.Url),

            "structured-focus" => ExecuteStructured(
                decision,
                "structured-focus"),

            "structured-invoke" => ExecuteStructured(
                decision,
                "structured-invoke"),

            "structured-select" => ExecuteStructured(
                decision,
                "structured-select"),

            "structured-toggle" => ExecuteStructured(
                decision,
                "structured-toggle"),

            "structured-expand" => ExecuteStructured(
                decision,
                "structured-expand"),

            "structured-collapse" => ExecuteStructured(
                decision,
                "structured-collapse"),

            "structured-set-value" => ExecuteStructured(
                decision,
                "structured-set-value"),

            "structured-legacy-default" => ExecuteStructured(
                decision,
                "structured-legacy-default"),

            _ => throw new ToolExecutionInputException(
                $"Computer Operator trả hành động không được hỗ trợ: {decision.Action}.")
        };
    }

    private ComputerActionResponse ExecuteStructured(
        DesktopOperatorDecision decision,
        string operation)
    {
        if (!flaUi.Available)
        {
            throw new ToolExecutionInputException(
                "FlaUI/UIA3 structured executor hiện không khả dụng.");
        }

        var active = computer.GetActiveWindow()
            ?? throw new ToolExecutionInputException(
                "Không xác định được foreground trước structured action.");

        var windowId = RequireValue(
            decision.CoordinateWindowId,
            "coordinateWindowId");

        if (!active.WindowId.Equals(
                windowId,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ToolExecutionInputException(
                "Foreground đã thay đổi trước structured action; từ chối thao tác trên target cũ.");
        }

        var token = RequireValue(
            decision.TargetElementId,
            "targetElementId");

        var response = flaUi.Invoke(
            new FlaUiAutomationRequest(
                Operation: operation,
                WindowId: windowId,
                TargetToken: token,
                Text: operation == "structured-set-value"
                    ? decision.Text
                    : null,
                WriteMode: operation == "structured-set-value"
                    ? "replace-all"
                    : null));

        if (!response.Success)
        {
            throw new ToolExecutionInputException(
                response.Detail);
        }

        return new ComputerActionResponse(
            operation,
            true,
            response.Detail);
    }

    private ComputerActionResponse ExecutePointerClick(
        DesktopOperatorDecision decision,
        ComputerCoordinatePoint point,
        ComputerSafeTargetPoint safeTarget,
        Func<IComputerUseService, string, int, int, ComputerActionResponse> click)
    {
        var targetWindow = RequireWindowAtPoint(
            point.DesktopX,
            point.DesktopY,
            "click");

        // Do not execute an image-grounded click against a different window.
        // The topmost window may have changed since the frame was captured.
        if (!string.IsNullOrWhiteSpace(decision.CoordinateWindowId) &&
            !targetWindow.WindowId.Equals(
                decision.CoordinateWindowId,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ToolExecutionInputException(
                "Cửa sổ tại điểm click không khớp cửa sổ đã grounding; cần quan sát lại trước khi click.");
        }

        _ = computer.SmoothMoveCursor(
            point.DesktopX,
            point.DesktopY,
            420);

        RevalidateWindowAtPoint(
            targetWindow,
            point.DesktopX,
            point.DesktopY,
            "click");

        var result = click(
            computer,
            targetWindow.WindowId,
            point.DesktopX,
            point.DesktopY);

        return result with
        {
            Detail =
                $"{result.Detail} Target={DescribeTarget(decision)} tại ({point.DesktopX}, {point.DesktopY}); canonical=({point.CanonicalX:0.0000},{point.CanonicalY:0.0000}); monitor=({point.MonitorNormalizedX:0.0000},{point.MonitorNormalizedY:0.0000}); hệ={point.Space}; nguồn={point.SourceDescription}; {safeTarget.Detail}"
        };
    }

    private ComputerActionResponse ExecutePointerScroll(
        DesktopOperatorDecision decision,
        ComputerCoordinatePoint point)
    {
        var targetWindow = RequireWindowAtPoint(
            point.DesktopX,
            point.DesktopY,
            "scroll");

        RevalidateWindowAtPoint(
            targetWindow,
            point.DesktopX,
            point.DesktopY,
            "scroll");

        return computer.Scroll(
            targetWindow.WindowId,
            point.DesktopX,
            point.DesktopY,
            decision.ScrollDelta);
    }

    private ComputerActionResponse ExecutePointerDrag(
        ComputerCoordinatePoint start,
        ComputerCoordinatePoint end)
    {
        var targetWindow = RequireWindowAtPoint(
            start.DesktopX,
            start.DesktopY,
            "drag-start");

        RevalidateWindowAtPoint(
            targetWindow,
            start.DesktopX,
            start.DesktopY,
            "drag-start");

        return computer.DragLeft(
            targetWindow.WindowId,
            start.DesktopX,
            start.DesktopY,
            end.DesktopX,
            end.DesktopY,
            650);
    }

    private ComputerActionResponse ExecuteKeyboard(
        ComputerWindowInfo? active,
        Func<string, ComputerActionResponse> execute)
    {
        var windowId = RequireActive(active);
        var current = computer.GetActiveWindow();

        if (current is null ||
            !current.WindowId.Equals(
                windowId,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ToolExecutionInputException(
                "Foreground đã thay đổi trước thao tác bàn phím; từ chối gửi input vào cửa sổ không còn được xác nhận.");
        }

        return execute(windowId);
    }

    private ComputerWindowInfo RequireWindowAtPoint(
        int x,
        int y,
        string primitive)
    {
        var window = computer.GetWindowAtPoint(x, y)
            ?? throw new ToolExecutionInputException(
                $"Không xác định được cửa sổ đích cho primitive {primitive} tại ({x}, {y}).");

        if (window.Width <= 0 || window.Height <= 0)
        {
            throw new ToolExecutionInputException(
                $"Cửa sổ đích của primitive {primitive} không còn vùng hiển thị hợp lệ.");
        }

        return window;
    }

    private void RevalidateWindowAtPoint(
        ComputerWindowInfo expected,
        int x,
        int y,
        string primitive)
    {
        var current = computer.GetWindowAtPoint(x, y)
            ?? throw new ToolExecutionInputException(
                $"Cửa sổ đích biến mất ngay trước primitive {primitive} tại ({x}, {y}).");

        if (!current.WindowId.Equals(
                expected.WindowId,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ToolExecutionInputException(
                $"Cửa sổ topmost đã thay đổi ngay trước primitive {primitive}; từ chối gửi input vào target cũ.");
        }

        if (current.Width <= 0 || current.Height <= 0)
        {
            throw new ToolExecutionInputException(
                $"Cửa sổ đích không còn vùng hiển thị hợp lệ ngay trước primitive {primitive}.");
        }
    }

    private static bool IsClickAction(string action) =>
        action is
            "click-left" or
            "double-click-left" or
            "click-right";

    private static bool IsPointerAction(string action) =>
        action is
            "move-pointer" or
            "click-left" or
            "double-click-left" or
            "click-right" or
            "scroll" or
            "drag-left";

    private static ComputerCoordinateRequest BuildCoordinateRequest(
        DesktopOperatorDecision decision,
        bool useEnd)
    {
        var space = string.IsNullOrWhiteSpace(decision.CoordinateSpace)
            ? ComputerCoordinateSpaces.ImagePixel
            : decision.CoordinateSpace.Trim().ToLowerInvariant();

        return new(
            space,
            useEnd ? decision.EndImageX : decision.ImageX,
            useEnd ? decision.EndImageY : decision.ImageY,
            useEnd ? decision.EndNormalizedX : decision.NormalizedX,
            useEnd ? decision.EndNormalizedY : decision.NormalizedY,
            decision.CoordinateWindowId);
    }

    private static ComputerCoordinatePoint RequirePoint(
        ComputerCoordinatePoint? point) =>
        point ?? throw new ToolExecutionInputException(
            "Không có tọa độ đã chuyển đổi cho primitive chuột.");

    private static ComputerSafeTargetPoint RequireSafeTarget(
        ComputerSafeTargetPoint? target) =>
        target ?? throw new ToolExecutionInputException(
            "Không có vùng mục tiêu an toàn cho primitive click.");

    private static string RequireActive(
        ComputerWindowInfo? active) =>
        active?.WindowId
        ?? throw new ToolExecutionInputException(
            "Không xác định được cửa sổ foreground cho primitive này.");

    private static string RequireValue(
        string value,
        string name) =>
        !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : throw new ToolExecutionInputException(
                $"Vision không trả tham số {name} cần thiết.");

    private static string DescribeTarget(
        DesktopOperatorDecision decision) =>
        string.IsNullOrWhiteSpace(decision.TargetLabel)
            ? "phần tử nhìn thấy"
            : decision.TargetLabel.Trim();
}
