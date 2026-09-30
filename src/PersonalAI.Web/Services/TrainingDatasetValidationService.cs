using System.Text;
using System.Text.Json;
using PersonalAI.Web.ModelLab;
using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public interface ITrainingDatasetValidationService
{
    TrainingDatasetValidationReport Validate(
        ValidateTrainingDatasetRequest request);
}

public sealed class TrainingDatasetValidationService(
    IModelLabDatasetStore datasets,
    IDataCleaningService cleaning,
    IWorkspaceContextAccessor workspace,
    IAuditRecorder audit) : ITrainingDatasetValidationService
{
    public const int MaximumReportedIssues = 250;
    public const int MaximumCharactersPerExample = 120_000;

    public TrainingDatasetValidationReport Validate(
        ValidateTrainingDatasetRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.SourceVersion < 1)
            throw new TrainingDatasetValidationException(
                "SourceVersion phải lớn hơn hoặc bằng 1.");

        var source = datasets.GetVersion(
            request.DatasetId,
            request.SourceVersion)
            ?? throw new KeyNotFoundException(
                "Không tìm thấy dataset version cần kiểm tra.");

        if (!string.Equals(
                source.WorkspaceId,
                workspace.CurrentWorkspaceId,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new TrainingDatasetValidationException(
                "Dataset version không thuộc workspace hiện tại.");
        }

        var checksumMatched = true;
        var expected = request.ExpectedContentSha256?.Trim().ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(expected))
        {
            if (expected.Length != 64 ||
                expected.Any(character => !Uri.IsHexDigit(character)))
            {
                throw new TrainingDatasetValidationException(
                    "ExpectedContentSha256 phải gồm đúng 64 ký tự hệ mười sáu.");
            }

            checksumMatched = string.Equals(
                expected,
                source.ContentSha256,
                StringComparison.OrdinalIgnoreCase);
        }

        var cleaningReport = cleaning.Clean(
            new CleanModelLabDatasetRequest(
                source.DatasetId,
                source.Version,
                CreateCleanedVersion: false));

        var issues = new List<TrainingDatasetValidationIssue>();
        if (!checksumMatched)
        {
            AddIssue(
                issues,
                null,
                TrainingDatasetValidationCodes.ChecksumMismatch,
                TrainingDatasetValidationSeverities.Error,
                "Checksum dataset không khớp giá trị kỳ vọng.");
        }

        foreach (var removal in cleaningReport.Removals)
        {
            var code = removal.Category switch
            {
                DataCleaningCategories.Duplicate =>
                    TrainingDatasetValidationCodes.Duplicate,
                DataCleaningCategories.Secret =>
                    TrainingDatasetValidationCodes.Secret,
                DataCleaningCategories.Corrupt =>
                    TrainingDatasetValidationCodes.Corrupt,
                _ => TrainingDatasetValidationCodes.BadExample
            };

            AddIssue(
                issues,
                removal.SourceIndex,
                code,
                TrainingDatasetValidationSeverities.Error,
                removal.Reason);
        }

        long estimatedTokens = 0;
        var schemaErrors = 0;
        var warningCount = 0;
        var schemaRejected = new HashSet<int>();

        for (var index = 0; index < source.Items.Count; index++)
        {
            var item = source.Items[index];
            estimatedTokens += EstimateTokens(item);

            if (!ValidateExample(
                item,
                index,
                issues,
                out var warningsForItem))
            {
                schemaErrors++;
                schemaRejected.Add(index);
            }

            warningCount += warningsForItem;
        }

        var cleaningRejected = cleaningReport.Removals
            .Select(item => item.SourceIndex)
            .ToHashSet();
        cleaningRejected.UnionWith(schemaRejected);

        var total = source.Items.Count;
        var rejected = cleaningRejected.Count;
        var accepted = Math.Max(0, total - rejected);
        var duplicateRatio = total == 0
            ? 0d
            : (double)cleaningReport.DuplicateItems / total;

        var sourceAfter = datasets.GetVersion(
            source.DatasetId,
            source.Version)
            ?? throw new InvalidOperationException(
                "Dataset nguồn không còn tồn tại sau validation.");
        var unchanged = string.Equals(
            source.ContentSha256,
            sourceAfter.ContentSha256,
            StringComparison.OrdinalIgnoreCase) &&
            source.ItemCount == sourceAfter.ItemCount;

        var valid =
            checksumMatched &&
            total > 0 &&
            rejected == 0 &&
            schemaErrors == 0 &&
            cleaningReport.SecretItems == 0 &&
            cleaningReport.CorruptItems == 0 &&
            unchanged;

        audit.Record(
            AuditAgents.System,
            "model-lab.training-dataset.validate",
            $"model-lab-dataset:{source.DatasetId}:v{source.Version}",
            $"valid:{valid};accepted:{accepted};rejected:{rejected};sha:{source.ContentSha256}",
            valid ? AuditResults.Succeeded : AuditResults.Failed);

        return new TrainingDatasetValidationReport(
            PersonalAiRelease.Version,
            workspace.CurrentWorkspaceId,
            source.DatasetId,
            source.Version,
            source.ContentSha256,
            checksumMatched,
            valid,
            total,
            accepted,
            rejected,
            warningCount,
            estimatedTokens,
            duplicateRatio,
            cleaningReport.SecretItems,
            schemaErrors,
            cleaningReport.BadExampleItems,
            cleaningReport.CorruptItems,
            unchanged,
            issues,
            DateTimeOffset.UtcNow);
    }

    private static bool ValidateExample(
        JsonElement item,
        int index,
        ICollection<TrainingDatasetValidationIssue> issues,
        out int warnings)
    {
        warnings = 0;
        if (item.ValueKind != JsonValueKind.Object)
        {
            AddIssue(
                issues,
                index,
                TrainingDatasetValidationCodes.InvalidTopLevel,
                TrainingDatasetValidationSeverities.Error,
                "Mỗi training example phải là JSON object.");
            return false;
        }

        var rawLength = Encoding.UTF8.GetByteCount(item.GetRawText());
        if (rawLength > MaximumCharactersPerExample)
        {
            AddIssue(
                issues,
                index,
                TrainingDatasetValidationCodes.OversizedExample,
                TrainingDatasetValidationSeverities.Error,
                $"Training example vượt giới hạn {MaximumCharactersPerExample:N0} byte.");
            return false;
        }

        if (item.TryGetProperty("messages", out var messages))
            return ValidateMessages(messages, index, issues);

        if (HasNonEmptyText(item, "input"))
        {
            if (HasNonEmptyValue(item, "expected") ||
                HasNonEmptyValue(item, "output") ||
                HasNonEmptyValue(item, "completion"))
            {
                return true;
            }

            AddIssue(
                issues,
                index,
                TrainingDatasetValidationCodes.MissingTrainingContent,
                TrainingDatasetValidationSeverities.Error,
                "Example có input nhưng thiếu expected/output/completion.");
            return false;
        }

        if (HasNonEmptyText(item, "prompt"))
        {
            if (HasNonEmptyValue(item, "completion") ||
                HasNonEmptyValue(item, "output"))
            {
                return true;
            }

            AddIssue(
                issues,
                index,
                TrainingDatasetValidationCodes.MissingTrainingContent,
                TrainingDatasetValidationSeverities.Error,
                "Example có prompt nhưng thiếu completion/output.");
            return false;
        }

        if (HasNonEmptyText(item, "content"))
        {
            warnings++;
            AddIssue(
                issues,
                index,
                TrainingDatasetValidationCodes.TextOnlyExample,
                TrainingDatasetValidationSeverities.Warning,
                "Example chỉ có content; hợp lệ làm dữ liệu văn bản nhưng chưa phải cặp supervised input/output.");
            return true;
        }

        AddIssue(
            issues,
            index,
            TrainingDatasetValidationCodes.MissingTrainingContent,
            TrainingDatasetValidationSeverities.Error,
            "Không tìm thấy messages, input/expected, prompt/completion hoặc content hợp lệ.");
        return false;
    }

    private static bool ValidateMessages(
        JsonElement messages,
        int index,
        ICollection<TrainingDatasetValidationIssue> issues)
    {
        if (messages.ValueKind != JsonValueKind.Array ||
            messages.GetArrayLength() < 2)
        {
            AddIssue(
                issues,
                index,
                TrainingDatasetValidationCodes.InvalidMessages,
                TrainingDatasetValidationSeverities.Error,
                "messages phải là array có ít nhất 2 message.");
            return false;
        }

        var allowedRoles = new HashSet<string>(
            ["system", "user", "assistant", "tool"],
            StringComparer.OrdinalIgnoreCase);
        var hasUser = false;
        var hasAssistant = false;

        foreach (var message in messages.EnumerateArray())
        {
            if (message.ValueKind != JsonValueKind.Object ||
                !message.TryGetProperty("role", out var roleElement) ||
                roleElement.ValueKind != JsonValueKind.String ||
                !message.TryGetProperty("content", out var contentElement) ||
                contentElement.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(contentElement.GetString()))
            {
                AddIssue(
                    issues,
                    index,
                    TrainingDatasetValidationCodes.InvalidMessages,
                    TrainingDatasetValidationSeverities.Error,
                    "Mỗi message phải có role và content dạng chuỗi không rỗng.");
                return false;
            }

            var role = roleElement.GetString() ?? string.Empty;
            if (!allowedRoles.Contains(role))
            {
                AddIssue(
                    issues,
                    index,
                    TrainingDatasetValidationCodes.InvalidMessages,
                    TrainingDatasetValidationSeverities.Error,
                    $"Role '{role}' không được hỗ trợ.");
                return false;
            }

            hasUser |= role.Equals("user", StringComparison.OrdinalIgnoreCase);
            hasAssistant |= role.Equals("assistant", StringComparison.OrdinalIgnoreCase);
        }

        if (!hasUser || !hasAssistant)
        {
            AddIssue(
                issues,
                index,
                TrainingDatasetValidationCodes.InvalidMessages,
                TrainingDatasetValidationSeverities.Error,
                "Chat training example phải có ít nhất một user message và một assistant message.");
            return false;
        }

        return true;
    }

    private static bool HasNonEmptyText(JsonElement item, string property)
    {
        if (!item.TryGetProperty(property, out var value))
            return false;

        return value.ValueKind == JsonValueKind.String &&
               !string.IsNullOrWhiteSpace(value.GetString());
    }

    private static bool HasNonEmptyValue(JsonElement item, string property)
    {
        if (!item.TryGetProperty(property, out var value))
            return false;

        return value.ValueKind switch
        {
            JsonValueKind.String => !string.IsNullOrWhiteSpace(value.GetString()),
            JsonValueKind.Array => value.GetArrayLength() > 0,
            JsonValueKind.Object => value.EnumerateObject().Any(),
            JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => true,
            _ => false
        };
    }

    private static long EstimateTokens(JsonElement item)
    {
        var characters = CountStringCharacters(item);
        if (characters == 0)
            characters = item.GetRawText().Length;

        return Math.Max(1, (characters + 3L) / 4L);
    }

    private static long CountStringCharacters(JsonElement element)
    {
        long total = 0;
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    total += property.Name.Length;
                    total += CountStringCharacters(property.Value);
                }
                break;
            case JsonValueKind.Array:
                foreach (var child in element.EnumerateArray())
                    total += CountStringCharacters(child);
                break;
            case JsonValueKind.String:
                total += element.GetString()?.Length ?? 0;
                break;
        }

        return total;
    }

    private static void AddIssue(
        ICollection<TrainingDatasetValidationIssue> issues,
        int? sourceIndex,
        string code,
        string severity,
        string message)
    {
        if (issues.Count >= MaximumReportedIssues)
            return;

        issues.Add(new TrainingDatasetValidationIssue(
            sourceIndex,
            code,
            severity,
            message));
    }
}
