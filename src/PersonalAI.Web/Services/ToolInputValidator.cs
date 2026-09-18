using System.Text.Json;
using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public interface IToolInputValidator
{
    ToolInputValidationResult Validate(JsonElement schema, JsonElement arguments);
}

public sealed class ToolInputValidator : IToolInputValidator
{
    public ToolInputValidationResult Validate(JsonElement schema, JsonElement arguments)
    {
        var errors = new List<string>();

        if (schema.ValueKind != JsonValueKind.Object)
        {
            return new ToolInputValidationResult(false, ["Lược đồ dữ liệu đầu vào không hợp lệ."]);
        }

        if (arguments.ValueKind != JsonValueKind.Object)
        {
            return new ToolInputValidationResult(false, ["Arguments phải là một JSON object."]);
        }

        var required = ReadRequired(schema);
        foreach (var requiredName in required)
        {
            if (!arguments.TryGetProperty(requiredName, out _))
            {
                errors.Add($"Thiếu trường bắt buộc: {requiredName}.");
            }
        }

        var hasProperties = schema.TryGetProperty("properties", out var properties)
            && properties.ValueKind == JsonValueKind.Object;
        var allowAdditional = !schema.TryGetProperty("additionalProperties", out var additional)
            || additional.ValueKind != JsonValueKind.False;

        foreach (var argument in arguments.EnumerateObject())
        {
            if (!hasProperties || !properties.TryGetProperty(argument.Name, out var propertySchema))
            {
                if (!allowAdditional)
                {
                    errors.Add($"Trường không được hỗ trợ: {argument.Name}.");
                }
                continue;
            }

            ValidateProperty(argument.Name, argument.Value, propertySchema, errors);
        }

        return new ToolInputValidationResult(errors.Count == 0, errors);
    }

    private static HashSet<string> ReadRequired(JsonElement schema)
    {
        var required = new HashSet<string>(StringComparer.Ordinal);
        if (!schema.TryGetProperty("required", out var requiredElement)
            || requiredElement.ValueKind != JsonValueKind.Array)
        {
            return required;
        }

        foreach (var item in requiredElement.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(item.GetString()))
            {
                required.Add(item.GetString()!);
            }
        }

        return required;
    }

    private static void ValidateProperty(
        string name,
        JsonElement value,
        JsonElement schema,
        ICollection<string> errors)
    {
        if (schema.ValueKind != JsonValueKind.Object)
        {
            errors.Add($"Lược đồ của trường {name} không hợp lệ.");
            return;
        }

        if (schema.TryGetProperty("type", out var typeElement)
            && typeElement.ValueKind == JsonValueKind.String)
        {
            var expectedType = typeElement.GetString();
            if (!MatchesType(value, expectedType))
            {
                errors.Add($"Trường {name} phải có kiểu {expectedType}.");
                return;
            }
        }

        if (value.ValueKind == JsonValueKind.String)
        {
            var text = value.GetString() ?? string.Empty;
            if (TryGetInt(schema, "minLength", out var minLength)
                && text.Length < minLength)
            {
                errors.Add($"Trường {name} phải có ít nhất {minLength} ký tự.");
            }

            if (TryGetInt(schema, "maxLength", out var maxLength)
                && text.Length > maxLength)
            {
                errors.Add($"Trường {name} không được dài hơn {maxLength} ký tự.");
            }
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var numericValue))
        {
            if (TryGetDouble(schema, "minimum", out var minimum)
                && numericValue < minimum)
            {
                errors.Add($"Trường {name} phải lớn hơn hoặc bằng {minimum}.");
            }

            if (TryGetDouble(schema, "maximum", out var maximum)
                && numericValue > maximum)
            {
                errors.Add($"Trường {name} phải nhỏ hơn hoặc bằng {maximum}.");
            }
        }

        if (schema.TryGetProperty("enum", out var enumElement)
            && enumElement.ValueKind == JsonValueKind.Array)
        {
            var raw = value.GetRawText();
            var matches = enumElement.EnumerateArray()
                .Any(item => string.Equals(item.GetRawText(), raw, StringComparison.Ordinal));
            if (!matches)
            {
                errors.Add($"Giá trị của trường {name} không nằm trong danh sách cho phép.");
            }
        }
    }

    private static bool MatchesType(JsonElement value, string? expectedType) => expectedType switch
    {
        "string" => value.ValueKind == JsonValueKind.String,
        "integer" => value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out _),
        "number" => value.ValueKind == JsonValueKind.Number,
        "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
        "array" => value.ValueKind == JsonValueKind.Array,
        "object" => value.ValueKind == JsonValueKind.Object,
        "null" => value.ValueKind == JsonValueKind.Null,
        _ => false
    };

    private static bool TryGetInt(JsonElement schema, string name, out int value)
    {
        value = 0;
        return schema.TryGetProperty(name, out var element)
            && element.ValueKind == JsonValueKind.Number
            && element.TryGetInt32(out value);
    }

    private static bool TryGetDouble(JsonElement schema, string name, out double value)
    {
        value = 0;
        return schema.TryGetProperty(name, out var element)
            && element.ValueKind == JsonValueKind.Number
            && element.TryGetDouble(out value);
    }
}
