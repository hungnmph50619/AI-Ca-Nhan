using System.Text.Json;

namespace PersonalAI.Web.Models;

public static class ToolPermissions
{
    public const string Read = "READ";
    public const string Write = "WRITE";
    public const string Delete = "DELETE";
    public const string External = "EXTERNAL";
    public const string Sensitive = "SENSITIVE";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(
        [Read, Write, Delete, External, Sensitive],
        StringComparer.OrdinalIgnoreCase);

    public static bool RequiresExplicitConfirmation(string permission) =>
        permission.Equals(Write, StringComparison.OrdinalIgnoreCase)
        || permission.Equals(Delete, StringComparison.OrdinalIgnoreCase)
        || permission.Equals(External, StringComparison.OrdinalIgnoreCase)
        || permission.Equals(Sensitive, StringComparison.OrdinalIgnoreCase);
}

public static class ToolExecutionStatuses
{
    public const string Succeeded = "succeeded";
    public const string Denied = "denied";
    public const string InvalidInput = "invalid-input";
    public const string NotFound = "not-found";
    public const string TimedOut = "timed-out";
    public const string Failed = "failed";
}

public sealed record ToolDefinition(
    string Name,
    string Description,
    string Version,
    IReadOnlyList<string> RequiredPermissions,
    int TimeoutMs,
    JsonElement InputSchema,
    bool LocalOnly = true,
    bool RequiresConfirmation = false);

public sealed record ToolExecutionRequest(
    string ToolName,
    JsonElement Arguments,
    IReadOnlyList<string>? ApprovedPermissions = null,
    bool Confirmed = false);

public sealed record ToolExecutionResponse(
    Guid InvocationId,
    string ToolName,
    string Status,
    bool Success,
    JsonElement? Output,
    string? Error,
    int DurationMs,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    IReadOnlyList<string> RequiredPermissions,
    IReadOnlyList<string> ApprovedPermissions);

public sealed record ToolCatalogResponse(
    string FrameworkVersion,
    IReadOnlyList<string> PermissionTypes,
    IReadOnlyList<ToolDefinition> Tools);

public sealed record ToolPolicyDecision(
    bool Allowed,
    IReadOnlyList<string> ApprovedPermissions,
    string? Error = null);

public sealed record ToolInputValidationResult(
    bool IsValid,
    IReadOnlyList<string> Errors);
