using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
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
    IToolAuditLog auditLog,
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

        async Task<ToolExecutionResponse> FinishAsync(
            Guid auditInvocationId,
            string toolName,
            string status,
            bool success,
            JsonElement? output,
            string? error,
            Stopwatch auditStopwatch,
            DateTimeOffset auditStartedAt,
            IReadOnlyList<string> requiredPermissions,
            IReadOnlyList<string> approvedPermissions)
        {
            var response = Complete(
                auditInvocationId,
                toolName,
                status,
                success,
                output,
                error,
                auditStopwatch,
                auditStartedAt,
                requiredPermissions,
                approvedPermissions);

            ToolDefinition? auditDefinition = null;
            if (registry.TryGet(toolName, out var registeredTool) && registeredTool is not null)
            {
                auditDefinition = registeredTool.Definition;
            }

            try
            {
                await auditLog.UpsertAsync(
                    CreateAuditEntry(
                        response,
                        auditDefinition,
                        request.Confirmed,
                        request.Arguments),
                    CancellationToken.None);
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "Could not persist tool audit entry for {ToolName}. Invocation {InvocationId}.",
                    toolName,
                    auditInvocationId);
            }

            return response;
        }

        if (!registry.TryGet(requestedName, out var tool) || tool is null)
        {
            return await FinishAsync(
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
        var validation = validator.Validate(definition.InputSchema, request.Arguments);
        if (!validation.IsValid)
        {
            return await FinishAsync(
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
            return await FinishAsync(
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

        try
        {
            await auditLog.UpsertAsync(
                CreateRunningAuditEntry(
                    invocationId,
                    definition,
                    startedAt,
                    policyDecision.ApprovedPermissions,
                    request.Confirmed,
                    request.Arguments),
                cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Tool audit log was unavailable before executing {ToolName}. Invocation {InvocationId}.",
                definition.Name,
                invocationId);

            return Complete(
                invocationId,
                definition.Name,
                ToolExecutionStatuses.Failed,
                false,
                null,
                "Không thể ghi nhật ký công cụ nên thao tác chưa được thực thi.",
                stopwatch,
                startedAt,
                definition.RequiredPermissions,
                policyDecision.ApprovedPermissions);
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(definition.TimeoutMs);

        try
        {
            var output = await tool.ExecuteAsync(request.Arguments, timeoutCts.Token);
            return await FinishAsync(
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
        }
        catch (ToolExecutionInputException exception)
        {
            return await FinishAsync(
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
        catch (OperationCanceledException) when (
            timeoutCts.IsCancellationRequested
            && !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(
                "Tool {ToolName} timed out after {TimeoutMs} ms. Invocation {InvocationId}.",
                definition.Name,
                definition.TimeoutMs,
                invocationId);
            return await FinishAsync(
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
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Tool {ToolName} failed. Invocation {InvocationId}.",
                definition.Name,
                invocationId);
            return await FinishAsync(
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
    }

    private static ToolAuditEntry CreateRunningAuditEntry(
        Guid invocationId,
        ToolDefinition definition,
        DateTimeOffset startedAt,
        IReadOnlyList<string> approvedPermissions,
        bool confirmed,
        JsonElement arguments)
    {
        var input = Fingerprint(arguments);
        return new ToolAuditEntry(
            invocationId,
            definition.Name,
            definition.Version,
            "running",
            null,
            startedAt,
            null,
            null,
            definition.RequiredPermissions,
            approvedPermissions,
            confirmed,
            "user-request",
            input.Hash,
            input.Bytes,
            null,
            null,
            DetermineReversibility(definition),
            definition.LocalOnly,
            null);
    }

    private static ToolAuditEntry CreateAuditEntry(
        ToolExecutionResponse response,
        ToolDefinition? definition,
        bool confirmed,
        JsonElement arguments)
    {
        var input = Fingerprint(arguments);
        var output = response.Output is null
            ? (Hash: (string?)null, Bytes: (int?)null)
            : Fingerprint(response.Output.Value);

        return new ToolAuditEntry(
            response.InvocationId,
            response.ToolName,
            definition?.Version ?? string.Empty,
            response.Status,
            response.Success,
            response.StartedAt,
            response.CompletedAt,
            response.DurationMs,
            response.RequiredPermissions,
            response.ApprovedPermissions,
            confirmed,
            "user-request",
            input.Hash,
            input.Bytes,
            output.Hash,
            output.Bytes,
            DetermineReversibility(definition),
            definition?.LocalOnly ?? true,
            response.Error);
    }

    private static (string Hash, int Bytes) Fingerprint(JsonElement element)
    {
        var json = element.ValueKind == JsonValueKind.Undefined
            ? "{}"
            : element.GetRawText();
        var bytes = Encoding.UTF8.GetBytes(json);
        return (
            Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
            bytes.Length);
    }

    private static string DetermineReversibility(ToolDefinition? definition)
    {
        if (definition is null)
        {
            return "unknown";
        }

        if (definition.RequiredPermissions.Any(permission =>
            permission.Equals(ToolPermissions.Delete, StringComparison.OrdinalIgnoreCase)))
        {
            return "not-automatically-reversible";
        }

        if (definition.RequiredPermissions.Any(permission =>
            permission.Equals(ToolPermissions.External, StringComparison.OrdinalIgnoreCase)))
        {
            return "external-dependent";
        }

        if (definition.RequiredPermissions.Any(permission =>
            permission.Equals(ToolPermissions.Write, StringComparison.OrdinalIgnoreCase)))
        {
            return "not-guaranteed";
        }

        return "not-applicable";
    }

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
