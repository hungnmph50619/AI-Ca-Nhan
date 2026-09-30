using PersonalAI.Web.Evaluation.Core;

namespace PersonalAI.Web.Evaluation.Contracts;

public interface IEvaluationEngine
{
    IReadOnlyList<string> Categories { get; }
    EvaluationResult Evaluate(EvaluationCase evaluationCase);
    IReadOnlyList<EvaluationResult> EvaluateMany(IReadOnlyList<EvaluationCase> evaluationCases);
}
