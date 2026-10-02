using System.Text.Json;
using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public interface IComputerOperatorTaskService
{
    Task<ComputerOperatorTaskResult> RunAsync(
        string goal,
        CancellationToken cancellationToken = default);
}

public sealed record ComputerOperatorTaskStep(
    int Index,
    string Action,
    string Detail);

public sealed record ComputerOperatorTaskResult(
    string Goal,
    bool Completed,
    string Summary,
    IReadOnlyList<ComputerOperatorTaskStep> Steps,
    string Provider,
    string Model);

public sealed class ComputerOperatorTaskService(
    IAiProviderResolver providerResolver,
    IComputerUseService computer,
    ComputerControlGate control,
    ILogger<ComputerOperatorTaskService> logger)
    : IComputerOperatorTaskService
{
    private const int MaximumSteps = 8;
    private static readonly string[] SecretTerms =
    [
        "password", "mật khẩu", "otp", "2fa", "mã xác thực",
        "verification code", "api key", "secret", "access token",
        "refresh token", "private key"
    ];

    private static readonly IReadOnlyList<ProviderFunctionDefinition> Functions =
    [
        Function(
            "focus_window",
            "Tìm cửa sổ đang hiển thị theo tiêu đề hoặc tên tiến trình và chuyển foreground tới cửa sổ phù hợp nhất.",
            """
            {
              "type":"object",
              "properties":{"query":{"type":"string","minLength":2,"maxLength":120}},
              "required":["query"],
              "additionalProperties":false
            }
            """),
        Function(
            "minimize_window",
            "Thu nhỏ đúng cửa sổ theo windowId từ quan sát Windows hiện tại.",
            WindowSchema),
        Function(
            "maximize_window",
            "Phóng to đúng cửa sổ theo windowId từ quan sát Windows hiện tại.",
            WindowSchema),
        Function(
            "restore_window",
            "Khôi phục đúng cửa sổ theo windowId từ quan sát Windows hiện tại.",
            WindowSchema),
        Function(
            "type_text",
            "Nhập văn bản hiển thị vào cửa sổ foreground hiện tại. Không dùng cho mật khẩu, OTP, khóa, token hoặc bí mật.",
            """
            {
              "type":"object",
              "properties":{
                "windowId":{"type":"string","minLength":3,"maxLength":32},
                "text":{"type":"string","minLength":1,"maxLength":1000}
              },
              "required":["windowId","text"],
              "additionalProperties":false
            }
            """),
        Function(
            "press_key",
            "Nhấn một phím hỗ trợ vào cửa sổ foreground hiện tại.",
            """
            {
              "type":"object",
              "properties":{
                "windowId":{"type":"string","minLength":3,"maxLength":32},
                "key":{"type":"string","minLength":1,"maxLength":20}
              },
              "required":["windowId","key"],
              "additionalProperties":false
            }
            """),
        Function(
            "press_hotkey",
            "Nhấn tổ hợp 2-4 phím hỗ trợ vào cửa sổ foreground hiện tại.",
            """
            {
              "type":"object",
              "properties":{
                "windowId":{"type":"string","minLength":3,"maxLength":32},
                "keys":{
                  "type":"array",
                  "minItems":2,
                  "maxItems":4,
                  "uniqueItems":true,
                  "items":{"type":"string","minLength":1,"maxLength":20}
                }
              },
              "required":["windowId","keys"],
              "additionalProperties":false
            }
            """),
        Function(
            "open_browser",
            "Mở trình duyệt mặc định. URL nếu có chỉ được là HTTP/HTTPS và sẽ được backend kiểm tra.",
            """
            {
              "type":"object",
              "properties":{"url":{"type":"string","minLength":8,"maxLength":2048}},
              "additionalProperties":false
            }
            """)
    ];

    private const string WindowSchema =
        """
        {
          "type":"object",
          "properties":{"windowId":{"type":"string","minLength":3,"maxLength":32}},
          "required":["windowId"],
          "additionalProperties":false
        }
        """;

    public async Task<ComputerOperatorTaskResult> RunAsync(
        string goal,
        CancellationToken cancellationToken = default)
    {
        var normalizedGoal = (goal ?? string.Empty).Trim();
        if (normalizedGoal.Length is < 2 or > 1200)
            throw new ToolExecutionInputException(
                "Yêu cầu Computer Operator phải từ 2 đến 1200 ký tự.");

        if (SecretTerms.Any(term =>
                normalizedGoal.Contains(
                    term,
                    StringComparison.OrdinalIgnoreCase)))
            throw new ToolExecutionInputException(
                "Computer Operator v3.1.1 không tự nhập mật khẩu, OTP, token, khóa hoặc bí mật.");

        control.EnableScopedAutomation(
            maximumActions: 12,
            maximumSeconds: 90);

        var provider = providerResolver.GetActive();
        if (!provider.IsConfigured)
            throw new ToolExecutionInputException(
                "Nhà cung cấp AI chưa được cấu hình cho Computer Operator.");

        var steps = new List<ComputerOperatorTaskStep>();
        var messages = new List<ChatMessage>
        {
            new(
                "user",
                "Bạn đang điều khiển Computer Operator v3.1.1 sau khi người dùng đã xác nhận MỘT tác vụ cấp cao. " +
                "Chỉ chọn một hàm mỗi lượt. Không được tự mở shell, xóa dữ liệu, dùng connector, nhập mật khẩu/OTP/bí mật hoặc thao tác ngoài mục tiêu. " +
                "Không đoán tọa độ chuột. Sau mỗi bước sẽ có quan sát Windows mới. " +
                $"Mục tiêu: {normalizedGoal}")
        };

        for (var index = 1; index <= MaximumSteps; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var session = control.GetStatus();
            if (session.Paused)
            {
                return Result(
                    false,
                    "Computer Operator đã dừng vì phiên điều khiển hết hạn, hết ngân sách hoặc bị dừng khẩn cấp.");
            }

            var observation = BuildObservation();
            messages.Add(new ChatMessage(
                "user",
                "Quan sát Windows hiện tại:\n" + observation +
                "\nNếu mục tiêu đã đạt, hãy trả lời bình thường và KHÔNG gọi hàm. " +
                "Nếu chưa đạt, chọn đúng MỘT hàm phù hợp cho bước nhỏ tiếp theo."));

            var decision = await provider.ProposeFunctionCallAsync(
                messages.TakeLast(14).ToArray(),
                Functions,
                cancellationToken);

            if (decision is null)
            {
                return Result(
                    true,
                    steps.Count == 0
                        ? "Không cần thêm thao tác máy tính để hoàn thành yêu cầu."
                        : $"Đã hoàn thành tác vụ sau {steps.Count} bước.");
            }

            ComputerActionResponse action;
            try
            {
                action = ExecuteDecision(
                    decision.Name,
                    decision.Arguments);
            }
            catch (ToolExecutionInputException exception)
            {
                logger.LogWarning(
                    "Computer Operator step {Step} rejected: {Reason}",
                    index,
                    exception.Message);

                return Result(
                    false,
                    $"Dừng ở bước {index}: {exception.Message}");
            }

            var step = new ComputerOperatorTaskStep(
                index,
                decision.Name,
                action.Detail);
            steps.Add(step);

            messages.Add(new ChatMessage(
                "assistant",
                $"Đã thực hiện bước {index}: {decision.Name}. {action.Detail}"));

            await Task.Delay(350, cancellationToken);
        }

        return Result(
            false,
            $"Đã đạt giới hạn {MaximumSteps} bước nên dừng để tránh vòng lặp.");

        ComputerOperatorTaskResult Result(
            bool completed,
            string summary) =>
            new(
                normalizedGoal,
                completed,
                summary,
                steps.ToArray(),
                provider.Name,
                provider.Model);
    }

    private ComputerActionResponse ExecuteDecision(
        string name,
        JsonElement arguments)
    {
        return name switch
        {
            "focus_window" => computer.FocusWindowByQuery(
                RequiredString(arguments, "query")),
            "minimize_window" => computer.MinimizeWindow(
                RequiredString(arguments, "windowId")),
            "maximize_window" => computer.MaximizeWindow(
                RequiredString(arguments, "windowId")),
            "restore_window" => computer.RestoreWindow(
                RequiredString(arguments, "windowId")),
            "type_text" => computer.TypeText(
                RequiredString(arguments, "windowId"),
                RequiredString(arguments, "text")),
            "press_key" => computer.PressKey(
                RequiredString(arguments, "windowId"),
                RequiredString(arguments, "key")),
            "press_hotkey" => computer.PressHotkey(
                RequiredString(arguments, "windowId"),
                RequiredStrings(arguments, "keys")),
            "open_browser" => computer.OpenDefaultBrowser(
                OptionalString(arguments, "url")),
            _ => throw new ToolExecutionInputException(
                $"Computer Operator trả hành động không được hỗ trợ: {name}.")
        };
    }

    private string BuildObservation()
    {
        var active = computer.GetActiveWindow();
        var windows = computer.GetWindows(30).Windows;

        var lines = new List<string>
        {
            active is null
                ? "Foreground: không xác định"
                : $"Foreground: id={active.WindowId}; title={active.Title}; process={active.ProcessName ?? "?"}; rect={active.Left},{active.Top},{active.Width},{active.Height}",
            "Cửa sổ đang hiển thị:"
        };

        foreach (var window in windows)
        {
            lines.Add(
                $"- id={window.WindowId}; title={window.Title}; process={window.ProcessName ?? "?"}; foreground={window.IsForeground}; rect={window.Left},{window.Top},{window.Width},{window.Height}");
        }

        return string.Join("\n", lines);
    }

    private static ProviderFunctionDefinition Function(
        string name,
        string description,
        string schema)
    {
        using var document = JsonDocument.Parse(schema);
        return new ProviderFunctionDefinition(
            name,
            description,
            document.RootElement.Clone());
    }

    private static string RequiredString(
        JsonElement arguments,
        string property)
    {
        if (!arguments.TryGetProperty(property, out var value)
            || value.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(value.GetString()))
            throw new ToolExecutionInputException(
                $"Thiếu tham số {property} cho Computer Operator.");

        return value.GetString()!.Trim();
    }

    private static string? OptionalString(
        JsonElement arguments,
        string property)
    {
        return arguments.TryGetProperty(property, out var value)
            && value.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(value.GetString())
                ? value.GetString()!.Trim()
                : null;
    }

    private static string[] RequiredStrings(
        JsonElement arguments,
        string property)
    {
        if (!arguments.TryGetProperty(property, out var value)
            || value.ValueKind != JsonValueKind.Array)
            throw new ToolExecutionInputException(
                $"Thiếu danh sách {property} cho Computer Operator.");

        var result = value
            .EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString()?.Trim() ?? string.Empty)
            .Where(item => item.Length > 0)
            .ToArray();

        if (result.Length == 0)
            throw new ToolExecutionInputException(
                $"Danh sách {property} đang trống.");

        return result;
    }
}
