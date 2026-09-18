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
            return new ToolInputValidationResult(false, ["Dữ liệu tham số phải là một đối tượng JSON."]);
        }

        var required = ReadRequired(schema);
        foreach (var requiredName in required)
        {
            if (!arguments.TryGetProperty(requiredName, out _))
            {
                errors.Add($"Thiếu trường bắt buộc: {DisplayFieldName(requiredName)}.");
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
                    errors.Add($"Trường không được hỗ trợ: {DisplayFieldName(argument.Name)}.");
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
            errors.Add($"Lược đồ của trường {DisplayFieldName(name)} không hợp lệ.");
            return;
        }

        if (schema.TryGetProperty("type", out var typeElement)
            && typeElement.ValueKind == JsonValueKind.String)
        {
            var expectedType = typeElement.GetString();
            if (!MatchesType(value, expectedType))
            {
                errors.Add($"Trường {DisplayFieldName(name)} phải có kiểu {DisplayType(expectedType)}.");
                return;
            }
        }

        if (value.ValueKind == JsonValueKind.String)
        {
            var text = value.GetString() ?? string.Empty;
            if (TryGetInt(schema, "minLength", out var minLength)
                && text.Length < minLength)
            {
                errors.Add($"Trường {DisplayFieldName(name)} phải có ít nhất {minLength} ký tự.");
            }

            if (TryGetInt(schema, "maxLength", out var maxLength)
                && text.Length > maxLength)
            {
                errors.Add($"Trường {DisplayFieldName(name)} không được dài hơn {maxLength} ký tự.");
            }
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var numericValue))
        {
            if (TryGetDouble(schema, "minimum", out var minimum)
                && numericValue < minimum)
            {
                errors.Add($"Trường {DisplayFieldName(name)} phải lớn hơn hoặc bằng {minimum}.");
            }

            if (TryGetDouble(schema, "maximum", out var maximum)
                && numericValue > maximum)
            {
                errors.Add($"Trường {DisplayFieldName(name)} phải nhỏ hơn hoặc bằng {maximum}.");
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
                errors.Add($"Giá trị của trường {DisplayFieldName(name)} không nằm trong danh sách cho phép.");
            }
        }
    }

    private static string DisplayFieldName(string name) =>
        name switch
        {
            "query" => "nội dung cần tìm",
            "text" => "văn bản",
            "expression" => "biểu thức",
            "operation" => "phép tính",
            "start" => "mốc bắt đầu",
            "end" => "mốc kết thúc",
            "days" => "số ngày",
            "hours" => "số giờ",
            "minutes" => "số phút",
            "path" => "đường dẫn",
            "content" => "nội dung",
            "mode" => "cách ghi",
            "expectedSha256" => "mã băm SHA-256 kỳ vọng",
            "sourcePath" => "đường dẫn nguồn",
            "destinationPath" => "đường dẫn đích",
            "limit" => "số kết quả tối đa",
            _ => "dữ liệu"
        };

    private static string DisplayType(string? type) =>
        type switch
        {
            "string" => "văn bản",
            "integer" => "số nguyên",
            "number" => "số",
            "boolean" => "đúng/sai",
            "array" => "danh sách",
            "object" => "đối tượng",
            "null" => "rỗng",
            _ => "phù hợp"
        };

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
