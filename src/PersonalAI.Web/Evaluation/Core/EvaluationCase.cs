namespace PersonalAI.Web.Evaluation.Core;

/// <summary>
/// A generic evaluation input that can be reused by Vision, RAG, Tool and Agent evaluators.
/// </summary>
public sealed record EvaluationCase
{
    public required string Id { get; init; }
    public required string Category { get; init; }
    public object? Input { get; init; }
    public object? Expected { get; init; }
    public IDictionary<string, string>? Metadata { get; init; }
}
