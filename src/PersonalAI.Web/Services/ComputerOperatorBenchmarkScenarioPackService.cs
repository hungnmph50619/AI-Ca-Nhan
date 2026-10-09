using PersonalAI.Web.Evaluation.Benchmarks;
using PersonalAI.Web.Evaluation.Regression;

namespace PersonalAI.Web.Services;

public sealed record InstallComputerOperatorBenchmarkScenarioPackRequest(
    bool Confirmed);

public sealed record ComputerOperatorBenchmarkScenarioPackInstallResult(
    string Version,
    int TotalScenarios,
    int CreatedCases,
    int ExistingCases,
    IReadOnlyList<string> CreatedCaseIds,
    IReadOnlyList<string> ExistingCaseIds);

public sealed class ComputerOperatorBenchmarkScenarioPackException(
    string message)
    : Exception(message);

public interface IComputerOperatorBenchmarkScenarioPackService
{
    ComputerOperatorBenchmarkScenarioPack GetPack();

    ComputerOperatorBenchmarkScenarioPackInstallResult Install(
        bool confirmed);
}

public sealed class ComputerOperatorBenchmarkScenarioPackService(
    IRegressionDatasetStore regressionStore)
    : IComputerOperatorBenchmarkScenarioPackService
{
    public ComputerOperatorBenchmarkScenarioPack GetPack() =>
        ComputerOperatorBenchmarkScenarioCatalog.GetPack();

    public ComputerOperatorBenchmarkScenarioPackInstallResult Install(
        bool confirmed)
    {
        EnsureConfirmed(
            confirmed);

        var requests =
            ComputerOperatorBenchmarkScenarioCatalog
                .BuildRegressionCases();

        var created =
            new List<string>();

        var existing =
            new List<string>();

        foreach (var request in requests)
        {
            var current =
                regressionStore.Get(
                    request.Id);

            if (current is not null)
            {
                existing.Add(
                    request.Id);
                continue;
            }

            regressionStore.Create(
                request);

            created.Add(
                request.Id);
        }

        return new(
            ComputerOperatorBenchmarkScenarioCatalog.PackVersion,
            requests.Count,
            created.Count,
            existing.Count,
            created,
            existing);
    }

    internal static void EnsureConfirmedForAcceptance(
        bool confirmed) =>
        EnsureConfirmed(
            confirmed);

    private static void EnsureConfirmed(
        bool confirmed)
    {
        if (!confirmed)
            throw new ComputerOperatorBenchmarkScenarioPackException(
                "Phải xác nhận trước khi cài Scenario Pack vào Regression Dataset.");
    }
}
