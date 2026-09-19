using System.Text;

namespace PersonalAI.Web.Services;

public sealed record KnowledgeChunkDraft(
    string Content,
    int? PageNumber,
    string? Heading,
    string? Section,
    int TokenEstimate);

public sealed class KnowledgeDocumentChunker
{
    private const int TargetChunkSize = 1_200;
    private const int MaximumChunkSize = 1_500;
    private const int MinimumChunkSize = 450;
    private const int ChunkOverlap = 160;

    public IReadOnlyList<KnowledgeChunkDraft> CreateChunks(ExtractedKnowledgeDocument document)
    {
        var chunks = new List<KnowledgeChunkDraft>();
        var buffer = new StringBuilder();
        int? currentPage = null;
        string? currentHeading = null;
        string? currentSection = null;

        void Flush()
        {
            if (buffer.Length == 0)
            {
                return;
            }

            var body = buffer.ToString().Trim();
            buffer.Clear();
            if (body.Length == 0)
            {
                return;
            }

            chunks.Add(CreateDraft(body, currentPage, currentHeading, currentSection));
        }

        foreach (var block in document.Blocks)
        {
            var text = Normalize(block.Text);
            if (text.Length == 0)
            {
                continue;
            }

            var sameStructure = buffer.Length > 0
                && currentPage == block.PageNumber
                && string.Equals(currentHeading, block.Heading, StringComparison.Ordinal)
                && string.Equals(currentSection, block.Section, StringComparison.Ordinal);

            if (text.Length > MaximumChunkSize)
            {
                Flush();
                foreach (var part in SplitLongText(text))
                {
                    chunks.Add(CreateDraft(
                        part,
                        block.PageNumber,
                        block.Heading,
                        block.Section));
                }
                currentPage = null;
                currentHeading = null;
                currentSection = null;
                continue;
            }

            if (buffer.Length == 0)
            {
                currentPage = block.PageNumber;
                currentHeading = block.Heading;
                currentSection = block.Section;
                buffer.Append(text);
                continue;
            }

            var combinedLength = buffer.Length + 2 + text.Length;
            if (!sameStructure || combinedLength > TargetChunkSize)
            {
                Flush();
                currentPage = block.PageNumber;
                currentHeading = block.Heading;
                currentSection = block.Section;
                buffer.Append(text);
                continue;
            }

            buffer.AppendLine().AppendLine().Append(text);
        }

        Flush();

        if (chunks.Count == 0 && !string.IsNullOrWhiteSpace(document.Text))
        {
            foreach (var part in SplitLongText(Normalize(document.Text)))
            {
                if (part.Length > 0)
                {
                    chunks.Add(CreateDraft(part, null, null, null));
                }
            }
        }

        return chunks;
    }

    private static KnowledgeChunkDraft CreateDraft(
        string body,
        int? pageNumber,
        string? heading,
        string? section)
    {
        var content = BuildIndexableContent(body, heading);
        return new KnowledgeChunkDraft(
            content,
            pageNumber,
            CleanMetadata(heading),
            CleanMetadata(section),
            EstimateTokens(content));
    }

    private static string BuildIndexableContent(string body, string? heading)
    {
        var cleanHeading = CleanMetadata(heading);
        if (string.IsNullOrWhiteSpace(cleanHeading))
        {
            return body;
        }

        if (body.StartsWith(cleanHeading, StringComparison.OrdinalIgnoreCase))
        {
            return body;
        }

        return $"{cleanHeading}\n\n{body}";
    }

    private static IEnumerable<string> SplitLongText(string source)
    {
        var text = Normalize(source);
        var start = 0;

        while (start < text.Length)
        {
            var desiredEnd = Math.Min(start + TargetChunkSize, text.Length);
            var end = desiredEnd < text.Length
                ? FindChunkBoundary(text, start, desiredEnd)
                : text.Length;

            if (end <= start)
            {
                end = Math.Min(start + MaximumChunkSize, text.Length);
            }

            var chunk = text[start..end].Trim();
            if (chunk.Length > 0)
            {
                yield return chunk;
            }

            if (end >= text.Length)
            {
                yield break;
            }

            start = Math.Max(start + 1, end - ChunkOverlap);
        }
    }

    private static int FindChunkBoundary(string text, int start, int desiredEnd)
    {
        var maximumEnd = Math.Min(start + MaximumChunkSize, text.Length);
        for (var index = desiredEnd; index < maximumEnd; index++)
        {
            if (IsStrongBoundary(text, index))
            {
                return index + 1;
            }
        }

        var minimumEnd = Math.Min(start + MinimumChunkSize, desiredEnd);
        for (var index = desiredEnd; index > minimumEnd; index--)
        {
            if (IsStrongBoundary(text, index - 1))
            {
                return index;
            }
        }

        for (var index = desiredEnd; index > minimumEnd; index--)
        {
            if (char.IsWhiteSpace(text[index - 1]))
            {
                return index;
            }
        }

        return desiredEnd;
    }

    private static bool IsStrongBoundary(string text, int index)
    {
        var character = text[index];
        return character == '\n'
            || ((character is '.' or '?' or '!')
                && (index + 1 >= text.Length || char.IsWhiteSpace(text[index + 1])));
    }

    private static int EstimateTokens(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0;
        }

        return Math.Max(1, (int)Math.Ceiling(text.Length / 4.0));
    }

    private static string Normalize(string value) =>
        value.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Trim();

    private static string? CleanMetadata(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }
}
