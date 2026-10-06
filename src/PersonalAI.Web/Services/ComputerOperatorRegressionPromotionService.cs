using System.Text.Json;
using PersonalAI.Web.Evaluation.Regression;

namespace PersonalAI.Web.Services;

public sealed record PromoteComputerOperatorRegressionRequest(
    bool ConfirmedReview);

public sealed record ComputerOperatorRegressionPromotionResult(
    string CandidateId,
    bool Created,
    RegressionDatasetItem RegressionCase,
    ComputerOperatorRegressionPromotionDraft Draft);

public sealed class ComputerOperatorRegressionPromotionException(
    string message)
    : Exception(message);

public interface IComputerOperatorRegressionPromotionService
{
    ComputerOperatorRegressionPromotionResult Promote(
        string candidateId,
        bool confirmedReview);
}

public sealed class ComputerOperatorRegressionPromotionService(
    IComputerOperatorRegressionCandidateStore candidates,
    IRegressionDatasetStore regressionStore)
    : IComputerOperatorRegressionPromotionService
{
    public const string RuntimeRegressionCategory =
        "computer-operator.runtime-regression";

    public ComputerOperatorRegressionPromotionResult Promote(
        string candidateId,
        bool confirmedReview)
    {
        if (!confirmedReview)
            throw new ComputerOperatorRegressionPromotionException(
                "Phải xác nhận đã review regression draft trước khi promote.");

        var draft =
            candidates.CreateDraft(candidateId) ??
            throw new ComputerOperatorRegressionPromotionException(
                "Không tìm thấy regression candidate.");

        var existing =
            regressionStore.Get(
                draft.Test.Id);

        if (existing is not null)
        {
            return new(
                draft.CandidateId,
                Created: false,
                existing,
                draft);
        }

        var request =
            BuildCreateRequest(
                draft);

        var created =
            regressionStore.Create(
                request);

        return new(
            draft.CandidateId,
            Created: true,
            created,
            draft);
    }

    internal static CreateRegressionDatasetItemRequest BuildCreateRequestForAcceptance(
        ComputerOperatorRegressionPromotionDraft draft) =>
        BuildCreateRequest(
            draft);

    private static CreateRegressionDatasetItemRequest BuildCreateRequest(
        ComputerOperatorRegressionPromotionDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);

        var input =
            JsonSerializer.SerializeToElement(
                new
                {
                    candidateId = draft.CandidateId,
                    outcome = draft.Test.Outcome,
                    currentStage = draft.Test.CurrentStage,
                    failureSignals = draft.Test.FailureSignals
                });

        var expected =
            JsonSerializer.SerializeToElement(
                new
                {
                    behavior = draft.Test.ExpectedBehavior,
                    mustNotRepeatSignals = draft.Test.FailureSignals
                });

        var metadata =
            new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase)
            {
                ["source"] = "computer-operator-runtime",
                ["reviewed"] = "true",
                ["severity"] = draft.Incident.Severity,
                ["origin"] = draft.Incident.Origin,
                ["incidentId"] = draft.Incident.Id
            };

        return new(
            Id: draft.Test.Id,
            Title: draft.Incident.Title,
            Category: RuntimeRegressionCategory,
            Input: input,
            Expected: expected,
            Metadata: metadata,
            Enabled: true);
    }
}
