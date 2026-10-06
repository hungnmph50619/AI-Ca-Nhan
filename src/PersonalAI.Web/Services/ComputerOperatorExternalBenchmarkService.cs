using System.Text.Json;
using PersonalAI.Web.Evaluation.Benchmarks;
using PersonalAI.Web.Evaluation.Regression;
using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public interface IComputerOperatorExternalBenchmarkService
{
    ComputerOperatorExternalBenchmarkPlan Prepare(
        PrepareComputerOperatorExternalBenchmarkRequest request);
}

public sealed class ComputerOperatorExternalBenchmarkService(
    IRegressionDatasetStore regressionStore,
    IComputerOperatorRegressionReadinessGate readiness,
    IWorkspaceContextAccessor workspace)
    : IComputerOperatorExternalBenchmarkService
{
    public const string BenchmarkCategory =
        "computer-operator.external-benchmark";

    public const int DefaultMaximumCases = 10;
    public const int HardMaximumCases = 20;
    public const int HardMaximumSteps = 64;

    public ComputerOperatorExternalBenchmarkPlan Prepare(
        PrepareComputerOperatorExternalBenchmarkRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var gate = readiness.Evaluate();
        EnsureReadiness(
            gate);

        var maximumCases =
            request.MaximumCases ?? DefaultMaximumCases;

        if (maximumCases is < 1 or > HardMaximumCases)
            throw new ComputerOperatorExternalBenchmarkValidationException(
                $"Số benchmark case mỗi lần phải từ 1 đến {HardMaximumCases}.");

        var selectedIds =
            NormalizeIds(
                request.CaseIds);

        var available =
            regressionStore.GetAll()
                .Where(item =>
                    item.Enabled &&
                    string.Equals(
                        item.Category,
                        BenchmarkCategory,
                        StringComparison.OrdinalIgnoreCase))
                .Where(item =>
                    selectedIds is null ||
                    selectedIds.Contains(item.Id))
                .OrderBy(
                    item => item.Id,
                    StringComparer.Ordinal)
                .Take(maximumCases)
                .ToArray();

        if (available.Length == 0)
            throw new ComputerOperatorExternalBenchmarkValidationException(
                "Không có regression case computer-operator.external-benchmark đang bật phù hợp để chuẩn bị.");

        if (selectedIds is not null)
        {
            var found =
                available
                    .Select(item => item.Id)
                    .ToHashSet(StringComparer.Ordinal);

            var missing =
                selectedIds
                    .Where(id => !found.Contains(id))
                    .ToArray();

            if (missing.Length > 0)
                throw new ComputerOperatorExternalBenchmarkValidationException(
                    "Một hoặc nhiều benchmark case được yêu cầu không tồn tại, bị tắt hoặc sai category.");
        }

        var plans =
            available
                .Select(ParseCase)
                .ToArray();

        return new(
            PersonalAiRelease.Version,
            workspace.CurrentWorkspaceId,
            DateTimeOffset.UtcNow,
            BenchmarkCategory,
            available.Length,
            plans.Length,
            ReadyForExecution: true,
            plans);
    }

    internal static void EnsureReadinessForAcceptance(
        ComputerOperatorRegressionReadinessGateResult gate) =>
        EnsureReadiness(
            gate);

    private static void EnsureReadiness(
        ComputerOperatorRegressionReadinessGateResult gate)
    {
        ArgumentNullException.ThrowIfNull(gate);

        if (gate.Passed)
            return;

        var detail =
            gate.Reasons.Count == 0
                ? "Regression readiness gate chưa đạt."
                : string.Join(" ", gate.Reasons);

        throw new ComputerOperatorExternalBenchmarkValidationException(
            $"Chưa được phép chuẩn bị external benchmark: {detail}");
    }

    internal static ComputerOperatorExternalBenchmarkCasePlan ParseCaseForAcceptance(
        RegressionDatasetItem item) =>
        ParseCase(
            item);

    private static IReadOnlySet<string>? NormalizeIds(
        IReadOnlyList<string>? ids)
    {
        if (ids is null || ids.Count == 0)
            return null;

        if (ids.Count > HardMaximumCases)
            throw new ComputerOperatorExternalBenchmarkValidationException(
                $"Chỉ được chọn tối đa {HardMaximumCases} benchmark case mỗi lần.");

        var normalized =
            new HashSet<string>(
                StringComparer.Ordinal);

        foreach (var value in ids)
        {
            var id =
                (value ?? string.Empty)
                    .Trim();

            if (id.Length == 0 ||
                !normalized.Add(id))
                throw new ComputerOperatorExternalBenchmarkValidationException(
                    "Danh sách benchmark case ID có giá trị rỗng hoặc trùng.");
        }

        return normalized;
    }

    private static ComputerOperatorExternalBenchmarkCasePlan ParseCase(
        RegressionDatasetItem item)
    {
        if (item.Input.ValueKind != JsonValueKind.Object)
            throw new ComputerOperatorExternalBenchmarkValidationException(
                $"{item.Id}: input phải là object.");

        var goal =
            RequiredString(
                item.Input,
                "goal",
                item.Id,
                maximumLength: 1200);

        var expectedEffect =
            RequiredString(
                item.Input,
                "expectedEffect",
                item.Id,
                maximumLength: 1000);

        var maximumSteps =
            OptionalInteger(
                item.Input,
                "maximumSteps",
                HardMaximumSteps);

        if (maximumSteps is < 1 or > HardMaximumSteps)
            throw new ComputerOperatorExternalBenchmarkValidationException(
                $"{item.Id}: maximumSteps phải từ 1 đến {HardMaximumSteps}.");

        var requiresInteractiveDesktop =
            OptionalBoolean(
                item.Input,
                "requiresInteractiveDesktop",
                fallback: true);

        if (item.Expected is not { ValueKind: JsonValueKind.Object })
            throw new ComputerOperatorExternalBenchmarkValidationException(
                $"{item.Id}: expected phải là object.");

        return new(
            item.Id,
            item.Title,
            goal,
            expectedEffect,
            maximumSteps,
            requiresInteractiveDesktop);
    }

    private static string RequiredString(
        JsonElement source,
        string name,
        string caseId,
        int maximumLength)
    {
        if (!source.TryGetProperty(
                name,
                out var value) ||
            value.ValueKind != JsonValueKind.String)
            throw new ComputerOperatorExternalBenchmarkValidationException(
                $"{caseId}: thiếu {name} hợp lệ.");

        var result =
            value.GetString()?.Trim() ??
            string.Empty;

        if (result.Length is < 2 ||
            result.Length > maximumLength)
            throw new ComputerOperatorExternalBenchmarkValidationException(
                $"{caseId}: {name} phải có từ 2 đến {maximumLength} ký tự.");

        return result;
    }

    private static int OptionalInteger(
        JsonElement source,
        string name,
        int fallback)
    {
        if (!source.TryGetProperty(
                name,
                out var value))
            return fallback;

        if (!value.TryGetInt32(
                out var result))
            throw new ComputerOperatorExternalBenchmarkValidationException(
                $"{name} phải là số nguyên.");

        return result;
    }

    private static bool OptionalBoolean(
        JsonElement source,
        string name,
        bool fallback)
    {
        if (!source.TryGetProperty(
                name,
                out var value))
            return fallback;

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw new ComputerOperatorExternalBenchmarkValidationException(
                $"{name} phải là boolean.")
        };
    }
}
