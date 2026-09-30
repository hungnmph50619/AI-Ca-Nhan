using System.Text.Json;
using PersonalAI.Web.Evaluation.Contracts;
using PersonalAI.Web.Evaluation.Core;

namespace PersonalAI.Web.Evaluation.Evaluators;

/// <summary>
/// Adapts the reviewed minimap-box evaluation rules used by the offline
/// browser/Python tools to the shared Evaluation Engine.
/// </summary>
public sealed class MinimapBoxEvaluator : IEvaluator
{
    public const string CategoryName = "vision.minimap-box";
    public const double DefaultMinimumIou = 0.5;

    public string Category => CategoryName;

    public EvaluationResult Evaluate(EvaluationCase evaluationCase)
    {
        ArgumentNullException.ThrowIfNull(evaluationCase);

        var input = ToObject(evaluationCase.Input, "input");
        var expected = evaluationCase.Expected is null
            ? input
            : ToObject(evaluationCase.Expected, "expected");

        var reviewed = ReadRequiredBoolean(expected, input, "reviewed");
        if (!reviewed)
            throw new EvaluationEngineException(
                $"{evaluationCase.Id}: reference label must be human-reviewed (reviewed=true).");

        ValidateEmbeddedId(input, evaluationCase.Id);
        ValidateEmbeddedId(expected, evaluationCase.Id);

        var truth = ReadBox(expected, input, "truth_box", "truthBox");
        var predicted = ReadBox(input, null, "predicted_box", "predictedBox");
        var minimumIou = ReadOptionalNumber(input, "minimum_iou", "minimumIou")
            ?? ReadOptionalNumber(expected, "minimum_iou", "minimumIou")
            ?? DefaultMinimumIou;

        if (!double.IsFinite(minimumIou) || minimumIou <= 0 || minimumIou > 1)
            throw new EvaluationEngineException(
                $"{evaluationCase.Id}: minimum IoU must be greater than 0 and at most 1.");

        double? iou = truth is not null && predicted is not null
            ? IntersectionOverUnion(truth, predicted)
            : null;

        var outcome = GetOutcome(truth, predicted, iou, minimumIou);
        var matched = outcome is "true_positive" or "true_negative";

        var metrics = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
        {
            ["minimumIou"] = minimumIou,
            ["matched"] = matched ? 1d : 0d
        };
        if (iou is not null)
            metrics["iou"] = iou.Value;

        return new EvaluationResult
        {
            CaseId = evaluationCase.Id,
            Category = CategoryName,
            Score = outcome switch
            {
                "true_negative" => 1d,
                "true_positive" => iou ?? 1d,
                "mismatched_box" => iou ?? 0d,
                _ => 0d
            },
            Metrics = metrics,
            Errors = OutcomeErrors(outcome)
        };
    }

    private static IReadOnlyList<string> OutcomeErrors(string outcome) => outcome switch
    {
        "true_positive" or "true_negative" => Array.Empty<string>(),
        "false_positive" => [ErrorCategory.FalsePositive.ToString()],
        "false_negative" => [ErrorCategory.FalseNegative.ToString()],
        "mismatched_box" =>
        [
            ErrorCategory.FalsePositive.ToString(),
            ErrorCategory.FalseNegative.ToString()
        ],
        _ => [ErrorCategory.Unstable.ToString()]
    };

    private static string GetOutcome(
        int[]? truth,
        int[]? predicted,
        double? iou,
        double minimumIou)
    {
        if (truth is null && predicted is null) return "true_negative";
        if (truth is not null && predicted is not null && iou >= minimumIou)
            return "true_positive";
        if (truth is null) return "false_positive";
        if (predicted is null) return "false_negative";
        return "mismatched_box";
    }

    private static double IntersectionOverUnion(int[] truth, int[] predicted)
    {
        var left = Math.Max(truth[0], predicted[0]);
        var top = Math.Max(truth[1], predicted[1]);
        var right = Math.Min(truth[2], predicted[2]);
        var bottom = Math.Min(truth[3], predicted[3]);

        var intersection =
            Math.Max(0, right - left) * Math.Max(0, bottom - top);
        var truthArea =
            (truth[2] - truth[0]) * (truth[3] - truth[1]);
        var predictedArea =
            (predicted[2] - predicted[0]) * (predicted[3] - predicted[1]);

        return (double)intersection /
            (truthArea + predictedArea - intersection);
    }

    private static JsonElement ToObject(object? value, string field)
    {
        if (value is null)
            throw new EvaluationEngineException($"{field} is required.");

        JsonElement element;
        try
        {
            element = value is JsonElement json
                ? json
                : JsonSerializer.SerializeToElement(value);
        }
        catch (Exception exception) when (
            exception is JsonException or NotSupportedException)
        {
            throw new EvaluationEngineException($"{field} is not valid JSON data.");
        }

        if (element.ValueKind != JsonValueKind.Object)
            throw new EvaluationEngineException($"{field} must be a JSON object.");

        return element;
    }

    private static bool ReadRequiredBoolean(
        JsonElement primary,
        JsonElement fallback,
        params string[] names)
    {
        if (!TryGetProperty(primary, names, out var value) &&
            !TryGetProperty(fallback, names, out value))
            throw new EvaluationEngineException(
                $"Missing required field '{names[0]}'.");

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw new EvaluationEngineException(
                $"Field '{names[0]}' must be boolean.")
        };
    }

    private static int[]? ReadBox(
        JsonElement primary,
        JsonElement? fallback,
        params string[] names)
    {
        if (!TryGetProperty(primary, names, out var value))
        {
            if (fallback is null ||
                !TryGetProperty(fallback.Value, names, out value))
                throw new EvaluationEngineException(
                    $"Missing required field '{names[0]}'.");
        }

        if (value.ValueKind == JsonValueKind.Null)
            return null;
        if (value.ValueKind != JsonValueKind.Array ||
            value.GetArrayLength() != 4)
            throw new EvaluationEngineException(
                $"Field '{names[0]}' must be null or an array of four integers.");

        var box = new int[4];
        var index = 0;
        foreach (var coordinate in value.EnumerateArray())
        {
            if (!coordinate.TryGetInt32(out var number) ||
                number < 0 || number > 1000)
                throw new EvaluationEngineException(
                    $"Field '{names[0]}' must contain integers from 0 to 1000.");
            box[index++] = number;
        }

        if (box[0] >= box[2] || box[1] >= box[3])
            throw new EvaluationEngineException(
                $"Field '{names[0]}' has invalid corner coordinates.");

        return box;
    }

    private static double? ReadOptionalNumber(
        JsonElement source,
        params string[] names)
    {
        if (!TryGetProperty(source, names, out var value))
            return null;
        if (value.ValueKind != JsonValueKind.Number ||
            !value.TryGetDouble(out var number))
            throw new EvaluationEngineException(
                $"Field '{names[0]}' must be numeric.");
        return number;
    }

    private static void ValidateEmbeddedId(JsonElement source, string caseId)
    {
        if (!TryGetProperty(source, ["id"], out var value))
            return;
        if (value.ValueKind != JsonValueKind.String ||
            !string.Equals(value.GetString(), caseId, StringComparison.Ordinal))
            throw new EvaluationEngineException(
                "Embedded sample ID must match the evaluation case ID.");
    }

    private static bool TryGetProperty(
        JsonElement source,
        IReadOnlyList<string> names,
        out JsonElement value)
    {
        foreach (var name in names)
        {
            if (source.TryGetProperty(name, out value))
                return true;
        }

        value = default;
        return false;
    }
}
