using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace PersonalAI.Web.Services;

public interface IKnowledgeEmbeddingService
{
    string ModelId { get; }
    int Dimensions { get; }
    float[] Embed(string text);
    double CosineSimilarity(ReadOnlySpan<float> left, ReadOnlySpan<float> right);
}

/// <summary>
/// Deterministic, zero-network vector representation used as the first local embedding backend.
/// It combines normalized word, word-bigram and character n-gram features through feature hashing.
/// The interface is intentionally provider-agnostic so a neural local/cloud embedding model can
/// replace this implementation later without changing the vector index contract.
/// </summary>
public sealed class LocalFeatureHashEmbeddingService : IKnowledgeEmbeddingService
{
    public const string CurrentModelId = "local-feature-hash-v1";
    public const int CurrentDimensions = 384;

    private const ulong FnvOffsetBasis = 14695981039346656037UL;
    private const ulong FnvPrime = 1099511628211UL;

    private static readonly Regex TokenRegex = new(
        @"[\p{L}\p{N}]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public string ModelId => CurrentModelId;
    public int Dimensions => CurrentDimensions;

    public float[] Embed(string text)
    {
        var vector = new float[Dimensions];
        var normalized = NormalizeForFeatures(text ?? string.Empty);
        var tokens = TokenRegex.Matches(normalized)
            .Cast<Match>()
            .Select(match => match.Value)
            .Where(token => token.Length > 0)
            .Take(2_000)
            .ToArray();

        if (tokens.Length == 0)
        {
            return vector;
        }

        for (var index = 0; index < tokens.Length; index++)
        {
            var token = tokens[index];
            AddFeature(vector, $"w:{token}", 1.0f);

            if (index > 0)
            {
                AddFeature(vector, $"b:{tokens[index - 1]}_{token}", 0.65f);
            }

            if (token.Length >= 3)
            {
                for (var start = 0; start <= token.Length - 3; start++)
                {
                    AddFeature(vector, $"c3:{token.Substring(start, 3)}", 0.22f);
                }
            }

            if (token.Length >= 4)
            {
                for (var start = 0; start <= token.Length - 4; start++)
                {
                    AddFeature(vector, $"c4:{token.Substring(start, 4)}", 0.12f);
                }
            }
        }

        NormalizeVector(vector);
        return vector;
    }

    public double CosineSimilarity(ReadOnlySpan<float> left, ReadOnlySpan<float> right)
    {
        if (left.Length == 0 || left.Length != right.Length)
        {
            return 0;
        }

        double dot = 0;
        double leftNorm = 0;
        double rightNorm = 0;
        for (var index = 0; index < left.Length; index++)
        {
            var leftValue = left[index];
            var rightValue = right[index];
            dot += leftValue * rightValue;
            leftNorm += leftValue * leftValue;
            rightNorm += rightValue * rightValue;
        }

        if (leftNorm <= double.Epsilon || rightNorm <= double.Epsilon)
        {
            return 0;
        }

        return dot / Math.Sqrt(leftNorm * rightNorm);
    }

    private static void AddFeature(float[] vector, string feature, float weight)
    {
        var hash = StableHash(feature);
        var vectorIndex = (int)(hash % (ulong)vector.Length);
        var sign = (hash & (1UL << 63)) == 0 ? 1f : -1f;
        vector[vectorIndex] += sign * weight;
    }

    private static ulong StableHash(string value)
    {
        var hash = FnvOffsetBasis;
        foreach (var rune in value.EnumerateRunes())
        {
            hash ^= (uint)rune.Value;
            hash *= FnvPrime;
        }
        return hash;
    }

    private static void NormalizeVector(float[] vector)
    {
        double squaredLength = 0;
        foreach (var value in vector)
        {
            squaredLength += value * value;
        }

        if (squaredLength <= double.Epsilon)
        {
            return;
        }

        var inverseLength = 1.0 / Math.Sqrt(squaredLength);
        for (var index = 0; index < vector.Length; index++)
        {
            vector[index] = (float)(vector[index] * inverseLength);
        }
    }

    private static string NormalizeForFeatures(string value)
    {
        var decomposed = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            builder.Append(character switch
            {
                'đ' or 'Đ' => 'd',
                _ => char.ToLowerInvariant(character)
            });
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }
}
