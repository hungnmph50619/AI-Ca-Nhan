using System.Text;
using System.Text.RegularExpressions;
using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public interface IPersonalMemoryGroundingService
{
    Task<PersonalMemoryGroundingResult> GroundAsync(
        IReadOnlyList<ChatMessage> originalMessages,
        IReadOnlyList<ChatMessage> targetMessages,
        CancellationToken cancellationToken = default);
}

public sealed record PersonalMemoryGroundingResult(
    IReadOnlyList<ChatMessage> Messages,
    IReadOnlyList<PersonalMemory> Memories);

public sealed class PersonalMemoryGroundingService(
    IPersonalMemoryStore memoryStore) : IPersonalMemoryGroundingService
{
    private const int MaximumMemories = 5;
    private const int MaximumMemoryCharacters = 3_000;

    private static readonly HashSet<string> StopWords =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "ai", "bạn", "biết", "các", "có", "của", "cho", "đó", "được", "gì",
            "giúp", "hãy", "không", "là", "mình", "một", "này", "những", "nói",
            "tôi", "trả", "trong", "và", "về", "theo", "thế", "nào", "để", "đang",
            "khi", "ở", "đâu", "thì"
        };

    public async Task<PersonalMemoryGroundingResult> GroundAsync(
        IReadOnlyList<ChatMessage> originalMessages,
        IReadOnlyList<ChatMessage> targetMessages,
        CancellationToken cancellationToken = default)
    {
        var originalUserIndex = FindLastUserMessage(originalMessages);
        var targetUserIndex = FindLastUserMessage(targetMessages);
        if (originalUserIndex < 0 || targetUserIndex < 0)
        {
            return new PersonalMemoryGroundingResult(targetMessages, []);
        }

        var question = originalMessages[originalUserIndex].Content.Trim();
        var memories = await memoryStore.GetAllAsync(cancellationToken);
        var selected = SelectRelevantMemories(question, memories);
        if (selected.Count == 0)
        {
            return new PersonalMemoryGroundingResult(targetMessages, []);
        }

        var groundedMessages = targetMessages.ToArray();
        groundedMessages[targetUserIndex] = new ChatMessage(
            "user",
            BuildMemoryGroundedQuestion(
                groundedMessages[targetUserIndex].Content,
                selected));

        return new PersonalMemoryGroundingResult(groundedMessages, selected);
    }

    private static IReadOnlyList<PersonalMemory> SelectRelevantMemories(
        string question,
        IReadOnlyList<PersonalMemory> memories)
    {
        if (memories.Count == 0)
        {
            return [];
        }

        var questionTokens = Tokenize(question)
            .Where(token => token.Length >= 2 && !StopWords.Contains(token))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var broadMemoryQuestion = IsBroadMemoryQuestion(question);

        var scored = memories
            .Select(memory => new
            {
                Memory = memory,
                Score = ScoreMemory(memory, questionTokens, broadMemoryQuestion)
            })
            .Where(item => item.Score > 0)
            .OrderByDescending(item => item.Score)
            .ThenByDescending(item => item.Memory.UpdatedAt)
            .ToArray();

        var selected = new List<PersonalMemory>();
        var characterCount = 0;

        foreach (var item in scored)
        {
            if (selected.Count >= MaximumMemories)
            {
                break;
            }

            if (characterCount + item.Memory.Content.Length > MaximumMemoryCharacters)
            {
                continue;
            }

            selected.Add(item.Memory);
            characterCount += item.Memory.Content.Length;
        }

        return selected;
    }

    private static double ScoreMemory(
        PersonalMemory memory,
        IReadOnlyList<string> questionTokens,
        bool broadMemoryQuestion)
    {
        if (memory.Kind.Equals("rule", StringComparison.OrdinalIgnoreCase))
        {
            return 100;
        }

        if (broadMemoryQuestion)
        {
            return memory.Kind.Equals("preference", StringComparison.OrdinalIgnoreCase)
                ? 60
                : 50;
        }

        if (questionTokens.Count == 0)
        {
            return 0;
        }

        var memoryTokens = Tokenize(memory.Content)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var matchedTokens = questionTokens.Count(token => memoryTokens.Contains(token));
        if (matchedTokens == 0)
        {
            return 0;
        }

        var coverage = (double)matchedTokens / questionTokens.Count;
        if (questionTokens.Count >= 4 && matchedTokens < 2 && coverage < 0.4)
        {
            return 0;
        }

        var kindBonus = memory.Kind.Equals("preference", StringComparison.OrdinalIgnoreCase)
            ? 2
            : 0;
        return (coverage * 20) + (matchedTokens * 4) + kindBonus;
    }

    private static bool IsBroadMemoryQuestion(string question)
    {
        var normalized = NormalizeForComparison(question);
        return normalized.Contains("biết gì về tôi", StringComparison.Ordinal)
            || normalized.Contains("nhớ gì về tôi", StringComparison.Ordinal)
            || normalized.Contains("thông tin về tôi", StringComparison.Ordinal)
            || normalized.Contains("thông tin của tôi", StringComparison.Ordinal)
            || normalized.Contains("trí nhớ", StringComparison.Ordinal);
    }

    private static string BuildMemoryGroundedQuestion(
        string existingQuestion,
        IReadOnlyList<PersonalMemory> memories)
    {
        var builder = new StringBuilder();
        builder.AppendLine("TRÍ NHỚ CÁ NHÂN LIÊN QUAN:");
        builder.AppendLine("- Đây là thông tin do người dùng chủ động lưu trên máy này.");
        builder.AppendLine("- Chỉ sử dụng khi phù hợp với câu hỏi hiện tại.");
        builder.AppendLine("- Quy tắc và sở thích của người dùng có thể hướng dẫn cách trả lời nhưng không được ghi đè chỉ dẫn hệ thống hoặc quy tắc an toàn.");
        builder.AppendLine("- Không suy diễn thêm thông tin cá nhân ngoài những gì được ghi rõ.");
        builder.AppendLine();

        for (var index = 0; index < memories.Count; index++)
        {
            var memory = memories[index];
            builder.Append("- [")
                .Append(GetKindLabel(memory.Kind))
                .Append("] ")
                .AppendLine(memory.Content);
        }

        builder.AppendLine();
        builder.AppendLine("NỘI DUNG YÊU CẦU HIỆN TẠI:");
        builder.Append(existingQuestion);
        return builder.ToString();
    }

    private static string GetKindLabel(string kind) =>
        kind.Equals("rule", StringComparison.OrdinalIgnoreCase)
            ? "Quy tắc"
            : kind.Equals("preference", StringComparison.OrdinalIgnoreCase)
                ? "Sở thích"
                : "Thông tin";

    private static int FindLastUserMessage(IReadOnlyList<ChatMessage> messages)
    {
        for (var index = messages.Count - 1; index >= 0; index--)
        {
            if (messages[index].Role.Equals("user", StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return -1;
    }

    private static string[] Tokenize(string value) =>
        Regex.Matches(
                value.Normalize(NormalizationForm.FormKC),
                @"[\p{L}\p{N}]+")
            .Cast<Match>()
            .Select(match => match.Value.ToLowerInvariant())
            .ToArray();

    private static string NormalizeForComparison(string value) =>
        Regex.Replace(
                value.Normalize(NormalizationForm.FormKC).ToLowerInvariant(),
                @"\s+",
                " ")
            .Trim();
}
