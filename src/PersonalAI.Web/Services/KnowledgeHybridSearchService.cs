using System.Text;
using System.Text.RegularExpressions;
using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public interface IKnowledgeHybridSearchService
{
    Task<KnowledgeHybridSearchResponse> SearchAsync(
        string query,
        int limit = 5,
        CancellationToken cancellationToken = default);
}

public sealed class KnowledgeHybridSearchService(
    IKnowledgeDocumentStore knowledgeStore,
    IKnowledgeEmbeddingIndex embeddingIndex) : IKnowledgeHybridSearchService
{
    private const int CandidateLimit = 10;
    private const int MaximumQueryVariants = 8;
    private const int MaximumSearchLength = 500;
    private const int MaximumChunksPerDocument = 3;
    private const double ReciprocalRankWeight = 1_200;
    private const double ReciprocalRankConstant = 60;
    private const double MinimumSemanticSimilarity = 0.05;
    private const double MinimumRelativeScore = 0.35;

    private static readonly HashSet<string> StopWords =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "ai", "bạn", "biết", "các", "có", "của", "cho", "đó", "được", "gì",
            "giúp", "hãy", "không", "là", "mình", "một", "này", "những", "nói",
            "tôi", "trả", "trong", "và", "về", "theo", "thế", "nào", "để", "đang",
            "khi", "ở", "đâu", "thì"
        };

    public async Task<KnowledgeHybridSearchResponse> SearchAsync(
        string query,
        int limit = 5,
        CancellationToken cancellationToken = default)
    {
        var normalizedQuery = (query ?? string.Empty).Trim();
        if (normalizedQuery.Length < 2)
        {
            throw new KnowledgeDocumentValidationException(
                "Hãy nhập ít nhất 2 ký tự để tìm kết hợp trong dữ liệu.");
        }

        if (normalizedQuery.Length > MaximumSearchLength)
        {
            throw new KnowledgeDocumentValidationException(
                $"Nội dung tìm kiếm kết hợp không được dài hơn {MaximumSearchLength} ký tự.");
        }

        var safeLimit = Math.Clamp(limit, 1, 10);
        var candidates = new Dictionary<string, CandidateState>(StringComparer.Ordinal);

        var keywordQueries = BuildKeywordQueries(normalizedQuery);
        foreach (var keywordQuery in keywordQueries)
        {
            try
            {
                var keywordResults = await knowledgeStore.SearchAsync(
                    keywordQuery,
                    CandidateLimit,
                    cancellationToken);

                for (var index = 0; index < keywordResults.Results.Count; index++)
                {
                    AddKeywordCandidate(
                        candidates,
                        keywordResults.Results[index],
                        index + 1);
                }
            }
            catch (KnowledgeDocumentValidationException)
            {
                // A widened query variant can be too short after normalization. Ignore it.
            }
        }

        var semanticResults = await embeddingIndex.SearchAsync(
            normalizedQuery,
            CandidateLimit,
            cancellationToken);
        for (var index = 0; index < semanticResults.Results.Count; index++)
        {
            AddSemanticCandidate(
                candidates,
                semanticResults.Results[index],
                index + 1);
        }

        var queryTokens = GetMeaningfulTokens(normalizedQuery);
        var normalizedForComparison = NormalizeForComparison(ExpandQueryAliases(normalizedQuery));

        var scored = candidates.Values
            .Select(candidate => ScoreCandidate(
                candidate,
                queryTokens,
                normalizedForComparison))
            .Where(candidate => candidate.IsRelevant)
            .OrderByDescending(candidate => candidate.RawScore)
            .ThenByDescending(candidate => candidate.SemanticSimilarity)
            .ThenBy(candidate => candidate.Result.FileName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(candidate => candidate.Result.ChunkIndex)
            .ToArray();

        if (scored.Length == 0)
        {
            return new KnowledgeHybridSearchResponse(
                normalizedQuery,
                0,
                "keyword+local-vector+deterministic-rerank-v1",
                []);
        }

        var bestScore = scored[0].RawScore;
        var relativeCutoff = bestScore * MinimumRelativeScore;
        var selected = new List<KnowledgeSearchResult>();
        var perDocumentCounts = new Dictionary<Guid, int>();

        foreach (var candidate in scored)
        {
            if (selected.Count >= safeLimit)
            {
                break;
            }

            if (candidate.RawScore < relativeCutoff)
            {
                continue;
            }

            perDocumentCounts.TryGetValue(candidate.Result.DocumentId, out var documentCount);
            if (documentCount >= MaximumChunksPerDocument)
            {
                continue;
            }

            perDocumentCounts[candidate.Result.DocumentId] = documentCount + 1;
            var normalizedScore = bestScore <= double.Epsilon
                ? 0
                : Math.Round((candidate.RawScore / bestScore) * 100, 2);

            selected.Add(candidate.Result with
            {
                SimilarityScore = candidate.SemanticSimilarity,
                KeywordRank = candidate.KeywordRank,
                SemanticRank = candidate.SemanticRank,
                HybridScore = normalizedScore,
                MatchSource = candidate.MatchSource
            });
        }

        return new KnowledgeHybridSearchResponse(
            normalizedQuery,
            selected.Count,
            "keyword+local-vector+deterministic-rerank-v1",
            selected);
    }

    private static void AddKeywordCandidate(
        IDictionary<string, CandidateState> candidates,
        KnowledgeSearchResult result,
        int rank)
    {
        var key = BuildKey(result);
        if (!candidates.TryGetValue(key, out var candidate))
        {
            candidate = new CandidateState(result);
            candidates[key] = candidate;
        }

        candidate.KeywordRank = candidate.KeywordRank.HasValue
            ? Math.Min(candidate.KeywordRank.Value, rank)
            : rank;
    }

    private static void AddSemanticCandidate(
        IDictionary<string, CandidateState> candidates,
        KnowledgeSearchResult result,
        int rank)
    {
        var key = BuildKey(result);
        if (!candidates.TryGetValue(key, out var candidate))
        {
            candidate = new CandidateState(result);
            candidates[key] = candidate;
        }

        candidate.SemanticRank = candidate.SemanticRank.HasValue
            ? Math.Min(candidate.SemanticRank.Value, rank)
            : rank;
        candidate.SemanticSimilarity = Math.Max(
            candidate.SemanticSimilarity,
            result.SimilarityScore ?? 0);
    }

    private static ScoredCandidate ScoreCandidate(
        CandidateState candidate,
        IReadOnlyList<string> queryTokens,
        string normalizedQuestion)
    {
        var result = candidate.Result;
        var contentTokens = Tokenize(result.Content)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var headingTokens = Tokenize($"{result.Heading} {result.Section}")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var fileTokens = Tokenize(result.FileName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var matchedContentTokens = queryTokens.Count(contentTokens.Contains);
        var matchedHeadingTokens = queryTokens.Count(headingTokens.Contains);
        var matchedFileTokens = queryTokens.Count(fileTokens.Contains);
        var matchedAnyTokens = queryTokens.Count(token =>
            contentTokens.Contains(token)
            || headingTokens.Contains(token)
            || fileTokens.Contains(token));
        var coverage = queryTokens.Count == 0
            ? 0
            : (double)matchedAnyTokens / queryTokens.Count;

        var searchableText = NormalizeForComparison(
            $"{result.Heading} {result.Section} {result.Content}");
        var exactPhrase = normalizedQuestion.Length >= 5
            && searchableText.Contains(normalizedQuestion, StringComparison.Ordinal);

        var keywordRankScore = candidate.KeywordRank.HasValue
            ? ReciprocalRankWeight / (ReciprocalRankConstant + candidate.KeywordRank.Value)
            : 0;
        var semanticRankScore = candidate.SemanticRank.HasValue
            ? ReciprocalRankWeight / (ReciprocalRankConstant + candidate.SemanticRank.Value)
            : 0;
        var semanticScore = Math.Max(0, candidate.SemanticSimilarity) * 35;
        var lexicalScore = (coverage * 25)
            + Math.Min(10, matchedContentTokens * 2)
            + Math.Min(8, matchedHeadingTokens * 4)
            + Math.Min(3, matchedFileTokens)
            + (exactPhrase ? 8 : 0);
        var dualRetrievalBonus = candidate.KeywordRank.HasValue && candidate.SemanticRank.HasValue
            ? 6
            : 0;

        var rawScore = keywordRankScore
            + semanticRankScore
            + semanticScore
            + lexicalScore
            + dualRetrievalBonus;

        var hasKeywordSignal = candidate.KeywordRank.HasValue;
        var hasSemanticSignal = candidate.SemanticRank.HasValue
            && candidate.SemanticSimilarity >= MinimumSemanticSimilarity;
        var isRelevant = hasKeywordSignal
            || (hasSemanticSignal
                && (candidate.SemanticSimilarity >= 0.10 || coverage > 0));

        var matchSource = candidate.KeywordRank.HasValue && candidate.SemanticRank.HasValue
            ? "keyword+semantic"
            : candidate.KeywordRank.HasValue
                ? "keyword"
                : "semantic";

        return new ScoredCandidate(
            result,
            rawScore,
            candidate.KeywordRank,
            candidate.SemanticRank,
            candidate.SemanticSimilarity,
            isRelevant,
            matchSource);
    }

    private static IReadOnlyList<string> BuildKeywordQueries(string question)
    {
        var queries = new List<string>();
        AddQuery(queries, question);

        var expanded = ExpandQueryAliases(question);
        AddQuery(queries, expanded);

        var tokens = GetMeaningfulTokens(expanded)
            .Take(8)
            .ToArray();

        if (tokens.Length > 1)
        {
            AddQuery(queries, string.Join(" ", tokens.Take(5)));
        }

        for (var index = 0; index + 1 < tokens.Length && queries.Count < MaximumQueryVariants; index++)
        {
            AddQuery(queries, $"{tokens[index]} {tokens[index + 1]}");
        }

        foreach (var token in tokens)
        {
            if (queries.Count >= MaximumQueryVariants)
            {
                break;
            }

            AddQuery(queries, token);
        }

        return queries;
    }

    private static string[] GetMeaningfulTokens(string value)
    {
        var expanded = ExpandQueryAliases(value);
        var tokens = Tokenize(expanded)
            .Where(token => token.Length >= 2 && !StopWords.Contains(token))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return tokens.Length > 0
            ? tokens
            : Tokenize(expanded)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
    }

    private static string ExpandQueryAliases(string question)
    {
        var expanded = question;
        expanded = Regex.Replace(
            expanded,
            @"\bchịu\s+trách\s+nhiệm\b",
            "phụ trách",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        expanded = Regex.Replace(
            expanded,
            @"\bproject\b",
            "dự án",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        expanded = Regex.Replace(
            expanded,
            @"\bbuổi\s+họp\b",
            "cuộc họp",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        expanded = Regex.Replace(
            expanded,
            @"\bmeeting\b",
            "cuộc họp",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return expanded;
    }

    private static void AddQuery(List<string> queries, string value)
    {
        var query = value.Trim();
        if (query.Length >= 2
            && !queries.Contains(query, StringComparer.OrdinalIgnoreCase))
        {
            queries.Add(query);
        }
    }

    private static string[] Tokenize(string value) =>
        Regex.Matches(
                (value ?? string.Empty).Normalize(NormalizationForm.FormKC),
                @"[\p{L}\p{N}]+")
            .Cast<Match>()
            .Select(match => match.Value.ToLowerInvariant())
            .ToArray();

    private static string NormalizeForComparison(string value) =>
        Regex.Replace(
                (value ?? string.Empty).Normalize(NormalizationForm.FormKC).ToLowerInvariant(),
                @"\s+",
                " ")
            .Trim();

    private static string BuildKey(KnowledgeSearchResult result) =>
        $"{result.DocumentId:D}:{result.ChunkIndex}";

    private sealed class CandidateState(KnowledgeSearchResult result)
    {
        public KnowledgeSearchResult Result { get; } = result;
        public int? KeywordRank { get; set; }
        public int? SemanticRank { get; set; }
        public double SemanticSimilarity { get; set; }
    }

    private sealed record ScoredCandidate(
        KnowledgeSearchResult Result,
        double RawScore,
        int? KeywordRank,
        int? SemanticRank,
        double SemanticSimilarity,
        bool IsRelevant,
        string MatchSource);
}
