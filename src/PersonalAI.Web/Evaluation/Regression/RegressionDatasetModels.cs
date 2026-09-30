using System.Text.Json;

namespace PersonalAI.Web.Evaluation.Regression;

public sealed record RegressionDatasetItem(
    string Id,
    string Title,
    string Category,
    JsonElement Input,
    JsonElement? Expected,
    IReadOnlyDictionary<string, string> Metadata,
    bool Enabled,
    string WorkspaceId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record CreateRegressionDatasetItemRequest(
    string Id,
    string Title,
    string Category,
    JsonElement Input,
    JsonElement? Expected = null,
    IReadOnlyDictionary<string, string>? Metadata = null,
    bool Enabled = true);

public sealed record UpdateRegressionDatasetItemRequest(
    string Title,
    string Category,
    JsonElement Input,
    JsonElement? Expected = null,
    IReadOnlyDictionary<string, string>? Metadata = null,
    bool Enabled = true);

public sealed record RegressionDatasetStatus(
    string Version,
    string Storage,
    string WorkspaceId,
    int Cases,
    int EnabledCases,
    int MaximumCases,
    int MaximumPayloadBytes);

public sealed record RegressionDatasetExport(
    int SchemaVersion,
    string PersonalAiVersion,
    string WorkspaceId,
    DateTimeOffset ExportedAt,
    IReadOnlyList<RegressionDatasetItem> Cases);

public sealed class RegressionDatasetValidationException(string message)
    : Exception(message);
