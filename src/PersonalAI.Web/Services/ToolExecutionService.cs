using System.Diagnostics;
using System.Text.Json;
using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public interface IToolPolicy
{
    ToolPolicyDecision Evaluate(
        ToolDefinition definition,
        IReadOnlyList<string>? approvedPermissions,
        bool confirmed);
}

public sealed class ToolPolicy : IToolPolicy
{
    public ToolPolicyDecision Evaluate(
        ToolDefinition definition,
        IReadOnlyList<string>? approvedPermissions,
        bool confirmed)
    {
        var normalized = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var permission in approvedPermissions ?? [])
        {
            var value = (permission ?? string.Empty).Trim().ToUpperInvariant();
            if (value.Length == 0)
            {
                continue;
            }

            if (!ToolPermissions.All.Contains(value))
            {
                return new ToolPolicyDecision(
                    false,
                    normalized.Order(StringComparer.Ordinal).ToArray(),
                    $"Quyền không hợp lệ: {LocalizePermission(value)}.");
            }

            normalized.Add(value);
        }

        var missing = definition.RequiredPermissions
            .Where(required => !normalized.Contains(required))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (missing.Length > 0)
        {
            return new ToolPolicyDecision(
                false,
                normalized.Order(StringComparer.Ordinal).ToArray(),
                $"Thiếu quyền: {string.Join(", ", missing.Select(LocalizePermission))}.");
        }

        var confirmationRequired = definition.RequiresConfirmation
            || definition.RequiredPermissions.Any(ToolPermissions.RequiresExplicitConfirmation);
        if (confirmationRequired && !confirmed)
        {
            return new ToolPolicyDecision(
                false,
                normalized.Order(StringComparer.Ordinal).ToArray(),
                "Công cụ này cần xác nhận rõ ràng trước khi thực thi.");
        }

        return new ToolPolicyDecision(
            true,
            normalized.Order(StringComparer.Ordinal).ToArray());
    }

    private static string LocalizePermission(string permission) =>
        permission.ToUpperInvariant() switch
        {
            "READ" => "ĐỌC",
            "WRITE" => "GHI",
            "DELETE" => "XÓA",
            "EXTERNAL" => "BÊN NGOÀI",
            "SENSITIVE" => "NHẠY CẢM",
            "COMPUTER" => "ĐIỀU KHIỂN MÁY",
            "BROWSER" => "TRÌNH DUYỆT",
            "CONNECTOR" => "KẾT NỐI",
            "DEVELOPMENT" => "PHÁT TRIỂN PHẦN MỀM",
            _ => permission
        };
}

public interface IToolExecutionService
{
    Task<ToolExecutionResponse> ExecuteAsync(
        ToolExecutionRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class ToolExecutionService(
    IToolRegistry registry,
    IToolInputValidator validator,
    IToolPolicy policy,
    IUndoService undo,
    IEmergencyStopService emergencyStop,
    ComputerOperatorExecutionControl operatorExecution,
    ComputerOperatorProgressStore operatorProgress,
    ILogger<ToolExecutionService> logger) : IToolExecutionService
{
    public async Task<ToolExecutionResponse> ExecuteAsync(
        ToolExecutionRequest request,
        CancellationToken cancellationToken = default)
    {
        var invocationId = Guid.NewGuid();
        var startedAt = DateTimeOffset.UtcNow;
        var stopwatch = Stopwatch.StartNew();
        var requestedName = (request.ToolName ?? string.Empty).Trim();

        if (!registry.TryGet(requestedName, out var tool) || tool is null)
        {
            return Complete(
                invocationId,
                requestedName,
                ToolExecutionStatuses.NotFound,
                false,
                null,
                "Không tìm thấy công cụ đã đăng ký.",
                stopwatch,
                startedAt,
                [],
                NormalizeApproved(request.ApprovedPermissions));
        }

        var definition = tool.Definition;
        if (emergencyStop.IsEngaged)
        {
            return Complete(
                invocationId,
                definition.Name,
                ToolExecutionStatuses.Denied,
                false,
                null,
                "Emergency stop đang bật; tool execution bị khóa.",
                stopwatch,
                startedAt,
                definition.RequiredPermissions,
                NormalizeApproved(request.ApprovedPermissions));
        }

        var validation = validator.Validate(definition.InputSchema, request.Arguments);
        if (!validation.IsValid)
        {
            return Complete(
                invocationId,
                definition.Name,
                ToolExecutionStatuses.InvalidInput,
                false,
                null,
                string.Join(" ", validation.Errors),
                stopwatch,
                startedAt,
                definition.RequiredPermissions,
                NormalizeApproved(request.ApprovedPermissions));
        }

        var policyDecision = policy.Evaluate(
            definition,
            request.ApprovedPermissions,
            request.Confirmed);
        if (!policyDecision.Allowed)
        {
            return Complete(
                invocationId,
                definition.Name,
                ToolExecutionStatuses.Denied,
                false,
                null,
                policyDecision.Error,
                stopwatch,
                startedAt,
                definition.RequiredPermissions,
                policyDecision.ApprovedPermissions);
        }

        var trackInConsole = ShouldTrackInOperatorConsole(definition.Name);
        var ownsOperatorSession = false;
        CancellationToken operatorToken = default;
        if (trackInConsole)
        {
            if (!operatorExecution.TryJoinRunning(out operatorToken))
            {
                operatorToken = operatorExecution.Begin(
                    $"Tool: {definition.Name}",
                    pausable: false);
                ownsOperatorSession = true;
                operatorProgress.Start(
                    $"Tool đang chạy: {definition.Name}");
                operatorProgress.Add(
                    "running",
                    $"Đang thực thi tool {definition.Name}. Timeout: {definition.TimeoutMs} ms.");
            }
            else
            {
                logger.LogDebug(
                    "Tool {ToolName} tham gia Computer Operator session hiện tại thay vì tạo session mới.",
                    definition.Name);
            }
        }

        var emergencyToken = emergencyStop.CurrentToken;
        using var timeoutCts = trackInConsole
            ? CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                emergencyToken,
                operatorToken)
            : CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                emergencyToken);
        timeoutCts.CancelAfter(definition.TimeoutMs);
        UndoPreparation? undoPreparation = null;

        try
        {
            undoPreparation = await undo.PrepareAsync(
                invocationId,
                definition.Name,
                request.Arguments,
                timeoutCts.Token);

            var output = await tool.ExecuteAsync(request.Arguments, timeoutCts.Token);
            var response = Complete(
                invocationId,
                definition.Name,
                ToolExecutionStatuses.Succeeded,
                true,
                NormalizeOutput(output),
                null,
                stopwatch,
                startedAt,
                definition.RequiredPermissions,
                policyDecision.ApprovedPermissions);
            var undoId = undo.Complete(
                undoPreparation,
                response);
            if (trackInConsole && ownsOperatorSession)
            {
                operatorProgress.Complete(
                    $"Tool {definition.Name} hoàn tất sau {response.DurationMs} ms.");
            }
            return response with { UndoId = undoId };
        }
        catch (ToolExecutionInputException exception)
        {
            undo.Abandon(undoPreparation);
            if (trackInConsole && ownsOperatorSession)
                operatorProgress.Block($"Tool bị từ chối: {exception.Message}");
            return Complete(
                invocationId,
                definition.Name,
                ToolExecutionStatuses.InvalidInput,
                false,
                null,
                exception.Message,
                stopwatch,
                startedAt,
                definition.RequiredPermissions,
                policyDecision.ApprovedPermissions);
        }
        catch (ToolExecutionStoppedByUserException exception)
        {
            undo.Abandon(undoPreparation);
            if (trackInConsole && ownsOperatorSession)
                operatorProgress.StopByUser(exception.Message);
            return Complete(
                invocationId,
                definition.Name,
                ToolExecutionStatuses.Denied,
                false,
                null,
                exception.Message,
                stopwatch,
                startedAt,
                definition.RequiredPermissions,
                policyDecision.ApprovedPermissions);
        }
        catch (ToolExecutionFailedException exception)
        {
            undo.Abandon(undoPreparation);
            if (trackInConsole && ownsOperatorSession)
                operatorProgress.Block($"Tool thất bại: {exception.Message}");
            return Complete(
                invocationId,
                definition.Name,
                ToolExecutionStatuses.Failed,
                false,
                exception.Output is JsonElement output
                    ? NormalizeOutput(output)
                    : null,
                exception.Message,
                stopwatch,
                startedAt,
                definition.RequiredPermissions,
                policyDecision.ApprovedPermissions);
        }
        catch (OperationCanceledException) when (
            trackInConsole && operatorToken.IsCancellationRequested)
        {
            undo.Abandon(undoPreparation);
            if (ownsOperatorSession)
            {
                operatorProgress.StopByUser(
                    $"Người dùng đã dừng tool {definition.Name} từ AI Operator Console.");
            }
            return Complete(
                invocationId,
                definition.Name,
                ToolExecutionStatuses.Denied,
                false,
                null,
                "Người dùng đã dừng tool đang chạy.",
                stopwatch,
                startedAt,
                definition.RequiredPermissions,
                policyDecision.ApprovedPermissions);
        }
        catch (OperationCanceledException) when (
            emergencyToken.IsCancellationRequested)
        {
            undo.Abandon(undoPreparation);
            return Complete(
                invocationId,
                definition.Name,
                ToolExecutionStatuses.Denied,
                false,
                null,
                "Emergency stop đã hủy tool execution đang chạy.",
                stopwatch,
                startedAt,
                definition.RequiredPermissions,
                policyDecision.ApprovedPermissions);
        }
        catch (OperationCanceledException) when (
            timeoutCts.IsCancellationRequested
            && !cancellationToken.IsCancellationRequested)
        {
            undo.Abandon(undoPreparation);
            if (trackInConsole && ownsOperatorSession)
                operatorProgress.Block(
                    $"Tool {definition.Name} bị timeout sau {definition.TimeoutMs} ms.");
            logger.LogWarning(
                "Tool {ToolName} timed out after {TimeoutMs} ms. Invocation {InvocationId}.",
                definition.Name,
                definition.TimeoutMs,
                invocationId);
            return Complete(
                invocationId,
                definition.Name,
                ToolExecutionStatuses.TimedOut,
                false,
                null,
                $"Công cụ vượt quá thời gian chờ {definition.TimeoutMs} mili giây.",
                stopwatch,
                startedAt,
                definition.RequiredPermissions,
                policyDecision.ApprovedPermissions);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            undo.Abandon(undoPreparation);
            throw;
        }
        catch (Exception exception)
        {
            undo.Abandon(undoPreparation);
            if (trackInConsole && ownsOperatorSession)
                operatorProgress.Block(
                    $"Tool {definition.Name} gặp lỗi. Xem log máy chủ để biết chi tiết.");
            logger.LogError(
                exception,
                "Tool {ToolName} failed. Invocation {InvocationId}.",
                definition.Name,
                invocationId);
            return Complete(
                invocationId,
                definition.Name,
                ToolExecutionStatuses.Failed,
                false,
                null,
                "Công cụ không thực thi được. Chi tiết kỹ thuật chỉ được ghi ở log máy chủ.",
                stopwatch,
                startedAt,
                definition.RequiredPermissions,
                policyDecision.ApprovedPermissions);
        }
        finally
        {
            if (trackInConsole && ownsOperatorSession)
                operatorExecution.Complete();
        }
    }

    private static bool ShouldTrackInOperatorConsole(string toolName) =>
        !IsComputerOperatorWrapper(toolName)
        && !toolName.Equals(
            "computer.vision.locate",
            StringComparison.OrdinalIgnoreCase)
        && !toolName.Equals(
            "computer.vision.click-target",
            StringComparison.OrdinalIgnoreCase)
        && !toolName.Equals(
            "league.practice.open",
            StringComparison.OrdinalIgnoreCase);

    private static bool IsComputerOperatorWrapper(string toolName) =>
        toolName.Equals(
            "computer.operator.run-task",
            StringComparison.OrdinalIgnoreCase)
        || toolName.Equals(
            "computer.app.launch",
            StringComparison.OrdinalIgnoreCase);

    private static ToolExecutionResponse Complete(
        Guid invocationId,
        string toolName,
        string status,
        bool success,
        JsonElement? output,
        string? error,
        Stopwatch stopwatch,
        DateTimeOffset startedAt,
        IReadOnlyList<string> requiredPermissions,
        IReadOnlyList<string> approvedPermissions)
    {
        stopwatch.Stop();
        var completedAt = DateTimeOffset.UtcNow;
        return new ToolExecutionResponse(
            invocationId,
            toolName,
            status,
            success,
            output,
            error,
            checked((int)Math.Min(stopwatch.ElapsedMilliseconds, int.MaxValue)),
            startedAt,
            completedAt,
            requiredPermissions,
            approvedPermissions);
    }

    private static string LocalizePermission(string permission) =>
        permission.ToUpperInvariant() switch
        {
            "READ" => "ĐỌC",
            "WRITE" => "GHI",
            "DELETE" => "XÓA",
            "EXTERNAL" => "BÊN NGOÀI",
            "SENSITIVE" => "NHẠY CẢM",
            "COMPUTER" => "ĐIỀU KHIỂN MÁY",
            "BROWSER" => "TRÌNH DUYỆT",
            "CONNECTOR" => "KẾT NỐI",
            "DEVELOPMENT" => "PHÁT TRIỂN PHẦN MỀM",
            _ => permission
        };

    private static JsonElement NormalizeOutput(JsonElement output)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            WriteCamelCase(output, writer);
        }

        using var document = JsonDocument.Parse(stream.ToArray());
        return document.RootElement.Clone();
    }

    private static void WriteCamelCase(JsonElement element, Utf8JsonWriter writer)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject())
                {
                    writer.WritePropertyName(JsonNamingPolicy.CamelCase.ConvertName(property.Name));
                    WriteCamelCase(property.Value, writer);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                {
                    WriteCamelCase(item, writer);
                }
                writer.WriteEndArray();
                break;
            default:
                element.WriteTo(writer);
                break;
        }
    }

    private static IReadOnlyList<string> NormalizeApproved(IReadOnlyList<string>? values) =>
        (values ?? [])
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim().ToUpperInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.Ordinal)
            .ToArray();
}

public sealed class ToolExecutionInputException(string message) : Exception(message);

public sealed class ToolExecutionStoppedByUserException(string message) : Exception(message);

public sealed class ToolExecutionFailedException : Exception
{
    public ToolExecutionFailedException(
        string message,
        JsonElement? output = null)
        : base(message)
    {
        Output = output?.Clone();
    }

    public JsonElement? Output { get; }
}
