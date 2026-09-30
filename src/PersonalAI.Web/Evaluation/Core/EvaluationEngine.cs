using PersonalAI.Web.Evaluation.Contracts;

namespace PersonalAI.Web.Evaluation.Core;

/// <summary>
/// Routes evaluation cases to registered evaluators without allowing an evaluator
/// to silently handle another category. The engine is deterministic and local.
/// </summary>
public sealed class EvaluationEngine : IEvaluationEngine
{
    public const int MaximumBatchSize = 100;
    private readonly IReadOnlyDictionary<string, IEvaluator> _evaluators;

    public EvaluationEngine(IEnumerable<IEvaluator> evaluators)
    {
        var map = new Dictionary<string, IEvaluator>(StringComparer.OrdinalIgnoreCase);
        foreach (var evaluator in evaluators)
        {
            var category = Normalize(evaluator.Category, nameof(evaluator.Category));
            if (!map.TryAdd(category, evaluator))
                throw new InvalidOperationException($"Duplicate evaluator category '{category}'.");
        }

        _evaluators = map;
        Categories = map.Keys.OrderBy(static value => value, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public IReadOnlyList<string> Categories { get; }

    public EvaluationResult Evaluate(EvaluationCase evaluationCase)
    {
        ArgumentNullException.ThrowIfNull(evaluationCase);
        var id = Normalize(evaluationCase.Id, nameof(evaluationCase.Id));
        var category = Normalize(evaluationCase.Category, nameof(evaluationCase.Category));

        if (!_evaluators.TryGetValue(category, out var evaluator))
            throw new EvaluationEngineException($"No evaluator is registered for category '{category}'.");

        var result = evaluator.Evaluate(evaluationCase with { Id = id, Category = category });
        if (!string.Equals(result.CaseId, id, StringComparison.Ordinal))
            throw new EvaluationEngineException("Evaluator returned a result for a different case ID.");

        if (!string.Equals(result.Category, category, StringComparison.OrdinalIgnoreCase))
            throw new EvaluationEngineException("Evaluator returned a result for a different category.");

        if (!double.IsFinite(result.Score))
            throw new EvaluationEngineException("Evaluator returned a non-finite score.");

        return result;
    }

    public IReadOnlyList<EvaluationResult> EvaluateMany(IReadOnlyList<EvaluationCase> evaluationCases)
    {
        ArgumentNullException.ThrowIfNull(evaluationCases);
        if (evaluationCases.Count == 0)
            return Array.Empty<EvaluationResult>();
        if (evaluationCases.Count > MaximumBatchSize)
            throw new EvaluationEngineException($"Evaluation batch cannot exceed {MaximumBatchSize} cases.");

        var ids = new HashSet<string>(StringComparer.Ordinal);
        var results = new List<EvaluationResult>(evaluationCases.Count);
        foreach (var evaluationCase in evaluationCases)
        {
            var id = Normalize(evaluationCase.Id, nameof(evaluationCase.Id));
            if (!ids.Add(id))
                throw new EvaluationEngineException($"Duplicate evaluation case ID '{id}'.");
            results.Add(Evaluate(evaluationCase));
        }

        return results;
    }

    private static string Normalize(string? value, string field)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            throw new EvaluationEngineException($"{field} is required.");
        if (normalized.Length > 120)
            throw new EvaluationEngineException($"{field} is too long.");
        return normalized;
    }
}

public sealed class EvaluationEngineException(string message) : Exception(message);
