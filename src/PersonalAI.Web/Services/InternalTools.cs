using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public sealed class MemorySearchTool(IPersonalMemoryStore memoryStore) : IPersonalAiTool
{
    private static readonly JsonElement Schema = ToolSchema.Parse(
        """
        {
          "type": "object",
          "properties": {
            "query": { "type": "string", "minLength": 2, "maxLength": 200 },
            "limit": { "type": "integer", "minimum": 1, "maximum": 10 },
            "kind": { "type": "string", "enum": ["fact", "preference", "rule"] },
            "includeStale": { "type": "boolean" }
          },
          "required": ["query"],
          "additionalProperties": false
        }
        """);

    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "ai", "bạn", "biết", "các", "có", "của", "cho", "đó", "được", "gì",
        "hãy", "không", "là", "mình", "một", "này", "những", "tôi", "trong",
        "và", "về", "theo", "để", "đang", "khi", "ở", "thì"
    };

    public ToolDefinition Definition { get; } = new(
        "memory.search",
        "Tìm trong trí nhớ cá nhân đang bật trên máy bằng xếp hạng từ khóa deterministic.",
        "1.0.0",
        [ToolPermissions.Read],
        3_000,
        Schema,
        LocalOnly: true,
        RequiresConfirmation: false);

    public async Task<JsonElement> ExecuteAsync(
        JsonElement arguments,
        CancellationToken cancellationToken = default)
    {
        var query = arguments.GetProperty("query").GetString()?.Trim() ?? string.Empty;
        var limit = arguments.TryGetProperty("limit", out var limitElement)
            ? limitElement.GetInt32()
            : 5;
        var kind = arguments.TryGetProperty("kind", out var kindElement)
            ? kindElement.GetString()?.Trim().ToLowerInvariant()
            : null;
        var includeStale = arguments.TryGetProperty("includeStale", out var staleElement)
            && staleElement.GetBoolean();

        var queryTokens = Tokenize(query)
            .Where(token => token.Length >= 2 && !StopWords.Contains(token))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (queryTokens.Length == 0)
        {
            throw new ToolExecutionInputException(
                "Query tìm trí nhớ phải chứa ít nhất một từ hoặc số có ý nghĩa.");
        }

        var normalizedQuery = Normalize(query);
        var memories = await memoryStore.GetAllAsync(cancellationToken);
        var ranked = memories
            .Where(memory => memory.IsEnabled && !memory.IsExpired)
            .Where(memory => includeStale || !memory.IsStale)
            .Where(memory => kind is null || memory.Kind.Equals(kind, StringComparison.OrdinalIgnoreCase))
            .Select(memory => ScoreMemory(memory, queryTokens, normalizedQuery))
            .Where(item => item.Score > 0)
            .OrderByDescending(item => item.Score)
            .ThenByDescending(item => item.Memory.UpdatedAt)
            .Take(limit)
            .Select(item => new
            {
                item.Memory.Id,
                item.Memory.Kind,
                item.Memory.Content,
                item.Memory.UpdatedAt,
                item.Memory.Retention,
                item.Memory.ExpiresAt,
                item.Memory.IsStale,
                score = Math.Round(item.Score, 2)
            })
            .ToArray();

        return JsonSerializer.SerializeToElement(new
        {
            query,
            resultCount = ranked.Length,
            results = ranked
        });
    }

    private static ScoredMemory ScoreMemory(
        PersonalMemory memory,
        IReadOnlyList<string> queryTokens,
        string normalizedQuery)
    {
        var contentTokens = Tokenize(memory.Content)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var matched = queryTokens.Count(contentTokens.Contains);
        if (matched == 0)
        {
            return new ScoredMemory(memory, 0);
        }

        var coverage = (double)matched / queryTokens.Count;
        var normalizedContent = Normalize(memory.Content);
        var exactPhrase = normalizedQuery.Length >= 3
            && normalizedContent.Contains(normalizedQuery, StringComparison.Ordinal);
        var score = (coverage * 70)
            + Math.Min(20, matched * 5)
            + (exactPhrase ? 25 : 0)
            + (memory.Kind.Equals("rule", StringComparison.OrdinalIgnoreCase) ? 2 : 0);

        return new ScoredMemory(memory, score);
    }

    private static string[] Tokenize(string value) =>
        Regex.Matches(
                (value ?? string.Empty).Normalize(NormalizationForm.FormKC),
                @"[\p{L}\p{N}]+")
            .Cast<Match>()
            .Select(match => match.Value.ToLowerInvariant())
            .ToArray();

    private static string Normalize(string value) =>
        Regex.Replace(
                (value ?? string.Empty).Normalize(NormalizationForm.FormKC).ToLowerInvariant(),
                @"\s+",
                " ")
            .Trim();

    private sealed record ScoredMemory(PersonalMemory Memory, double Score);
}

public sealed class DocumentSearchTool(IKnowledgeHybridSearchService hybridSearch) : IPersonalAiTool
{
    private static readonly JsonElement Schema = ToolSchema.Parse(
        """
        {
          "type": "object",
          "properties": {
            "query": { "type": "string", "minLength": 2, "maxLength": 500 },
            "limit": { "type": "integer", "minimum": 1, "maximum": 10 }
          },
          "required": ["query"],
          "additionalProperties": false
        }
        """);

    public ToolDefinition Definition { get; } = new(
        "documents.search",
        "Tìm các chunk liên quan trong kho tài liệu bằng hybrid keyword + vector + rerank local.",
        "1.0.0",
        [ToolPermissions.Read],
        10_000,
        Schema,
        LocalOnly: true,
        RequiresConfirmation: false);

    public async Task<JsonElement> ExecuteAsync(
        JsonElement arguments,
        CancellationToken cancellationToken = default)
    {
        var query = arguments.GetProperty("query").GetString()?.Trim() ?? string.Empty;
        var limit = arguments.TryGetProperty("limit", out var limitElement)
            ? limitElement.GetInt32()
            : 5;

        try
        {
            var response = await hybridSearch.SearchAsync(query, limit, cancellationToken);
            return JsonSerializer.SerializeToElement(response);
        }
        catch (KnowledgeDocumentValidationException exception)
        {
            throw new ToolExecutionInputException(exception.Message);
        }
    }
}

public sealed class CalculatorTool : IPersonalAiTool
{
    private static readonly JsonElement Schema = ToolSchema.Parse(
        """
        {
          "type": "object",
          "properties": {
            "expression": { "type": "string", "minLength": 1, "maxLength": 200 }
          },
          "required": ["expression"],
          "additionalProperties": false
        }
        """);

    public ToolDefinition Definition { get; } = new(
        "local.calculate",
        "Tính biểu thức số học local với +, -, *, /, %, dấu ngoặc và số thập phân; không dùng eval hay shell.",
        "1.0.0",
        [ToolPermissions.Read],
        2_000,
        Schema,
        LocalOnly: true,
        RequiresConfirmation: false);

    public Task<JsonElement> ExecuteAsync(
        JsonElement arguments,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var expression = arguments.GetProperty("expression").GetString()?.Trim() ?? string.Empty;

        try
        {
            var result = new DecimalExpressionParser(expression).Parse();
            return Task.FromResult(JsonSerializer.SerializeToElement(new
            {
                expression,
                result,
                resultText = result.ToString("G29", CultureInfo.InvariantCulture)
            }));
        }
        catch (ToolExecutionInputException)
        {
            throw;
        }
        catch (Exception exception) when (exception is OverflowException or DivideByZeroException)
        {
            throw new ToolExecutionInputException(
                exception is DivideByZeroException
                    ? "Không thể chia cho 0."
                    : "Kết quả vượt quá phạm vi số thập phân được hỗ trợ.");
        }
    }

    private sealed class DecimalExpressionParser(string expression)
    {
        private int _position;

        public decimal Parse()
        {
            if (string.IsNullOrWhiteSpace(expression))
            {
                throw new ToolExecutionInputException("Biểu thức không được để trống.");
            }

            var result = ParseExpression();
            SkipWhitespace();
            if (_position != expression.Length)
            {
                throw Error("Có ký tự không hợp lệ trong biểu thức.");
            }
            return result;
        }

        private decimal ParseExpression()
        {
            var value = ParseTerm();
            while (true)
            {
                if (Match('+')) value += ParseTerm();
                else if (Match('-')) value -= ParseTerm();
                else return value;
            }
        }

        private decimal ParseTerm()
        {
            var value = ParseUnary();
            while (true)
            {
                if (Match('*')) value *= ParseUnary();
                else if (Match('/'))
                {
                    var divisor = ParseUnary();
                    if (divisor == 0) throw new DivideByZeroException();
                    value /= divisor;
                }
                else if (Match('%'))
                {
                    var divisor = ParseUnary();
                    if (divisor == 0) throw new DivideByZeroException();
                    value %= divisor;
                }
                else return value;
            }
        }

        private decimal ParseUnary()
        {
            if (Match('+')) return ParseUnary();
            if (Match('-')) return -ParseUnary();
            return ParsePrimary();
        }

        private decimal ParsePrimary()
        {
            if (Match('('))
            {
                var value = ParseExpression();
                if (!Match(')')) throw Error("Thiếu dấu ngoặc đóng ).");
                return value;
            }

            return ParseNumber();
        }

        private decimal ParseNumber()
        {
            SkipWhitespace();
            var start = _position;
            var hasDigits = false;

            while (_position < expression.Length && char.IsDigit(expression[_position]))
            {
                hasDigits = true;
                _position++;
            }

            if (_position < expression.Length && expression[_position] == '.')
            {
                _position++;
                while (_position < expression.Length && char.IsDigit(expression[_position]))
                {
                    hasDigits = true;
                    _position++;
                }
            }

            if (!hasDigits)
            {
                throw Error("Cần một số hợp lệ tại vị trí hiện tại.");
            }

            if (_position < expression.Length && expression[_position] is 'e' or 'E')
            {
                var exponentStart = _position++;
                if (_position < expression.Length && expression[_position] is '+' or '-') _position++;
                var exponentDigits = _position;
                while (_position < expression.Length && char.IsDigit(expression[_position])) _position++;
                if (_position == exponentDigits)
                {
                    _position = exponentStart;
                }
            }

            var token = expression[start.._position];
            if (!decimal.TryParse(
                    token,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var value))
            {
                throw Error("Số trong biểu thức không hợp lệ hoặc vượt phạm vi hỗ trợ.");
            }
            return value;
        }

        private bool Match(char expected)
        {
            SkipWhitespace();
            if (_position >= expression.Length || expression[_position] != expected) return false;
            _position++;
            return true;
        }

        private void SkipWhitespace()
        {
            while (_position < expression.Length && char.IsWhiteSpace(expression[_position])) _position++;
        }

        private ToolExecutionInputException Error(string message) =>
            new($"{message} (vị trí {_position + 1}).");
    }
}

public sealed class DateMathTool : IPersonalAiTool
{
    private static readonly JsonElement Schema = ToolSchema.Parse(
        """
        {
          "type": "object",
          "properties": {
            "operation": { "type": "string", "enum": ["add", "difference"] },
            "start": { "type": "string", "minLength": 4, "maxLength": 80 },
            "end": { "type": "string", "minLength": 4, "maxLength": 80 },
            "days": { "type": "integer", "minimum": -36500, "maximum": 36500 },
            "hours": { "type": "integer", "minimum": -240000, "maximum": 240000 },
            "minutes": { "type": "integer", "minimum": -1000000, "maximum": 1000000 }
          },
          "required": ["operation", "start"],
          "additionalProperties": false
        }
        """);

    public ToolDefinition Definition { get; } = new(
        "local.date_math",
        "Cộng khoảng thời gian vào một mốc ISO-8601 hoặc tính chênh lệch giữa hai mốc thời gian local.",
        "1.0.0",
        [ToolPermissions.Read],
        2_000,
        Schema,
        LocalOnly: true,
        RequiresConfirmation: false);

    public Task<JsonElement> ExecuteAsync(
        JsonElement arguments,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var operation = arguments.GetProperty("operation").GetString() ?? string.Empty;
        var start = ParseDate(arguments.GetProperty("start").GetString(), "start");

        if (operation == "difference")
        {
            if (!arguments.TryGetProperty("end", out var endElement))
            {
                throw new ToolExecutionInputException(
                    "Operation difference cần trường end.");
            }
            if (arguments.TryGetProperty("days", out _)
                || arguments.TryGetProperty("hours", out _)
                || arguments.TryGetProperty("minutes", out _))
            {
                throw new ToolExecutionInputException(
                    "Operation difference không dùng days, hours hoặc minutes.");
            }

            var end = ParseDate(endElement.GetString(), "end");
            var difference = end - start;
            return Task.FromResult(JsonSerializer.SerializeToElement(new
            {
                operation,
                start,
                end,
                totalDays = difference.TotalDays,
                totalHours = difference.TotalHours,
                totalMinutes = difference.TotalMinutes,
                totalSeconds = difference.TotalSeconds,
                ticks = difference.Ticks
            }));
        }

        var days = ReadInt(arguments, "days");
        var hours = ReadInt(arguments, "hours");
        var minutes = ReadInt(arguments, "minutes");
        try
        {
            var result = start.AddDays(days).AddHours(hours).AddMinutes(minutes);
            return Task.FromResult(JsonSerializer.SerializeToElement(new
            {
                operation,
                start,
                days,
                hours,
                minutes,
                result,
                utcResult = result.ToUniversalTime()
            }));
        }
        catch (ArgumentOutOfRangeException)
        {
            throw new ToolExecutionInputException(
                "Kết quả ngày giờ vượt quá phạm vi được hỗ trợ.");
        }
    }

    private static DateTimeOffset ParseDate(string? value, string fieldName)
    {
        if (DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal,
                out var parsed))
        {
            return parsed;
        }

        throw new ToolExecutionInputException(
            $"Trường {fieldName} phải là ngày giờ ISO-8601 hợp lệ, ví dụ 2026-09-18T08:30:00+07:00.");
    }

    private static int ReadInt(JsonElement arguments, string name) =>
        arguments.TryGetProperty(name, out var element) ? element.GetInt32() : 0;
}

public sealed class AppSummaryTool(
    IPersonalMemoryStore memoryStore,
    IKnowledgeDocumentManagementService documentManagement,
    IKnowledgeEmbeddingIndex embeddingIndex) : IPersonalAiTool
{
    private static readonly JsonElement Schema = ToolSchema.Parse(
        """
        {
          "type": "object",
          "properties": {},
          "additionalProperties": false
        }
        """);

    public ToolDefinition Definition { get; } = new(
        "app.summary",
        "Đọc tóm tắt trạng thái dữ liệu nội bộ: trí nhớ, tài liệu và vector index; không trả nội dung chi tiết.",
        "1.0.0",
        [ToolPermissions.Read],
        10_000,
        Schema,
        LocalOnly: true,
        RequiresConfirmation: false);

    public async Task<JsonElement> ExecuteAsync(
        JsonElement arguments,
        CancellationToken cancellationToken = default)
    {
        var memory = await memoryStore.GetStatsAsync(cancellationToken);
        var documents = await documentManagement.GetAllAsync(cancellationToken);
        var embeddings = await embeddingIndex.GetStatusAsync(cancellationToken);

        return JsonSerializer.SerializeToElement(new
        {
            generatedAt = DateTimeOffset.UtcNow,
            memory = new
            {
                memory.Total,
                memory.Enabled,
                memory.Disabled,
                memory.Facts,
                memory.Preferences,
                memory.Rules,
                memory.LongTerm,
                memory.Temporary,
                memory.Expired,
                memory.Stale
            },
            knowledge = new
            {
                documents.Total,
                documents.Ready,
                documents.NeedsIndexing,
                documents.EmbeddingModel,
                documents.Dimensions
            },
            embeddings = new
            {
                embeddings.TotalChunks,
                embeddings.IndexedChunks,
                embeddings.MissingChunks,
                embeddings.Local
            }
        });
    }
}

internal static class ToolSchema
{
    public static JsonElement Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
