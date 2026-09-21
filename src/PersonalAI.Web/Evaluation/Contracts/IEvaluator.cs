using PersonalAI.Web.Evaluation.Core;

namespace PersonalAI.Web.Evaluation.Contracts;

/// <summary>
/// Contract for future evaluation modules.
/// Minimap/Vision can implement this without changing the Evaluation engine.
/// </summary>
public interface IEvaluator
{
    string Category { get; }
    EvaluationResult Evaluate(EvaluationCase evaluationCase);
}
