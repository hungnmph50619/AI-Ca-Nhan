namespace PersonalAI.Web.Evaluation.Core;

/// <summary>
/// Standard output returned by an evaluator.
/// </summary>
public sealed record EvaluationResult
{
    public required string CaseId { get; init; }
    public required string Category { get; init; }
    public double Score { get; init; }
    public IDictionary<string, double>? Metrics { get; init; }
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();
}
