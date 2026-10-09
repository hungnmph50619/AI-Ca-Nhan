namespace PersonalAI.Web.Evaluation.Regression;

public sealed record ComputerOperatorRegressionCaseResult(
    string Id,
    string Title,
    bool Passed,
    string Detail,
    double DurationMilliseconds);

public sealed record ComputerOperatorRegressionLabReport(
    string Version,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc,
    int TotalCases,
    int PassedCases,
    int FailedCases,
    double PassRate,
    double DurationMilliseconds,
    IReadOnlyList<ComputerOperatorRegressionCaseResult> Cases);
