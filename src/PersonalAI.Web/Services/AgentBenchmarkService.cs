using System.Diagnostics;
using System.Text.Json;
using PersonalAI.Web.Evaluation.Benchmarks;
using PersonalAI.Web.Evaluation.Regression;
using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public interface IAgentBenchmarkService
{
    Task<AgentBenchmarkReport> RunAsync(
        RunAgentBenchmarkRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class AgentBenchmarkService(
    IRegressionDatasetStore regressionStore,
    IAgentFrameworkService agents,
    IWorkspaceContextAccessor workspace,
    IAuditRecorder audit) : IAgentBenchmarkService
{
    public const string BenchmarkCategory = "agent.benchmark";
    public const int DefaultMaximumCases = 10;
    public const int HardMaximumCases = 20;

    public async Task<AgentBenchmarkReport> RunAsync(
        RunAgentBenchmarkRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!request.ConfirmExternalExecution)
            throw new AgentBenchmarkValidationException(
                "Phải xác nhận rõ trước khi benchmark gọi các agent và có thể sử dụng AI provider.");

        var maximumCases = request.MaximumCases ?? DefaultMaximumCases;
        if (maximumCases is < 1 or > HardMaximumCases)
            throw new AgentBenchmarkValidationException(
                $"Số case mỗi lần phải từ 1 đến {HardMaximumCases}.");

        var selectedIds = NormalizeIds(request.CaseIds);
        var available = regressionStore.GetAll()
            .Where(item =>
                item.Enabled &&
                string.Equals(
                    item.Category,
                    BenchmarkCategory,
                    StringComparison.OrdinalIgnoreCase))
            .Where(item =>
                selectedIds is null || selectedIds.Contains(item.Id))
            .OrderBy(item => item.Id, StringComparer.Ordinal)
            .Take(maximumCases)
            .ToArray();

        if (available.Length == 0)
            throw new AgentBenchmarkValidationException(
                "Không có regression case agent.benchmark đang bật phù hợp để chạy.");

        if (selectedIds is not null)
        {
            var found = available.Select(item => item.Id)
                .ToHashSet(StringComparer.Ordinal);
            var missing = selectedIds.Where(id => !found.Contains(id)).ToArray();
            if (missing.Length > 0)
                throw new AgentBenchmarkValidationException(
                    "Một hoặc nhiều case được yêu cầu không tồn tại, bị tắt hoặc không thuộc category agent.benchmark.");
        }

        var startedAt = DateTimeOffset.UtcNow;
        var results = new List<AgentBenchmarkCaseResult>(available.Length);

        audit.Record(
            AuditAgents.User,
            "evaluation.agent-benchmark.run",
            $"benchmark:{workspace.CurrentWorkspaceId}",
            "explicit-user-confirmation",
            AuditResults.Prepared);

        foreach (var item in available)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var spec = ParseCase(item);
            var stopwatch = Stopwatch.StartNew();
            try
            {
                var response = await agents.ExecuteAsync(
                    spec.AgentId,
                    new AgentExecutionRequest(
                        spec.Goal,
                        Conversation: null,
                        UseKnowledge: spec.UseKnowledge,
                        UseMemory: spec.UseMemory,
                        UseTaskContext: spec.UseTaskContext,
                        UseLifeContext: spec.UseLifeContext),
                    cancellationToken);
                stopwatch.Stop();

                var failures = EvaluateExpected(
                    response.Message ?? string.Empty,
                    item.Expected);
                var passed = failures.Count == 0;
                results.Add(new AgentBenchmarkCaseResult(
                    item.Id,
                    item.Title,
                    spec.AgentId,
                    passed,
                    passed ? 1d : 0d,
                    stopwatch.Elapsed.TotalMilliseconds,
                    response.Provider,
                    response.Model,
                    failures));
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                audit.Record(
                    AuditAgents.User,
                    "evaluation.agent-benchmark.run",
                    $"benchmark:{workspace.CurrentWorkspaceId}",
                    "request-cancelled",
                    AuditResults.Cancelled);
                throw;
            }
            catch (Exception exception)
            {
                stopwatch.Stop();
                results.Add(new AgentBenchmarkCaseResult(
                    item.Id,
                    item.Title,
                    spec.AgentId,
                    Passed: false,
                    Score: 0d,
                    LatencyMilliseconds: stopwatch.Elapsed.TotalMilliseconds,
                    Provider: null,
                    Model: null,
                    Failures: [$"execution-error:{exception.GetType().Name}"]));
            }
        }

        var completedAt = DateTimeOffset.UtcNow;
        var passedCases = results.Count(result => result.Passed);
        var report = new AgentBenchmarkReport(
            PersonalAiRelease.Version,
            workspace.CurrentWorkspaceId,
            startedAt,
            completedAt,
            available.Length,
            results.Count,
            passedCases,
            results.Count - passedCases,
            results.Count == 0 ? 0d : (double)passedCases / results.Count,
            results.Count == 0 ? 0d : results.Average(result => result.LatencyMilliseconds),
            results);

        audit.Record(
            AuditAgents.User,
            "evaluation.agent-benchmark.run",
            $"benchmark:{workspace.CurrentWorkspaceId}",
            "explicit-user-confirmation",
            AuditResults.Succeeded);

        return report;
    }

    private static IReadOnlySet<string>? NormalizeIds(IReadOnlyList<string>? ids)
    {
        if (ids is null || ids.Count == 0)
            return null;
        if (ids.Count > HardMaximumCases)
            throw new AgentBenchmarkValidationException(
                $"Chỉ được chọn tối đa {HardMaximumCases} case mỗi lần.");

        var normalized = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in ids)
        {
            var id = (value ?? string.Empty).Trim();
            if (id.Length == 0 || !normalized.Add(id))
                throw new AgentBenchmarkValidationException(
                    "Danh sách case ID có giá trị rỗng hoặc trùng.");
        }
        return normalized;
    }

    private static BenchmarkSpec ParseCase(RegressionDatasetItem item)
    {
        if (item.Input.ValueKind != JsonValueKind.Object)
            throw new AgentBenchmarkValidationException(
                $"{item.Id}: input phải là object.");

        var agentId = RequiredString(item.Input, "agentId", item.Id);
        var goal = RequiredString(item.Input, "goal", item.Id);
        if (goal.Length > AgentFrameworkLimits.MaximumGoalCharacters)
            throw new AgentBenchmarkValidationException(
                $"{item.Id}: goal vượt quá giới hạn agent.");

        return new BenchmarkSpec(
            agentId,
            goal,
            OptionalBoolean(item.Input, "useKnowledge", true),
            OptionalBoolean(item.Input, "useMemory", true),
            OptionalBoolean(item.Input, "useTaskContext", true),
            OptionalBoolean(item.Input, "useLifeContext", true));
    }

    private static IReadOnlyList<string> EvaluateExpected(
        string output,
        JsonElement? expected)
    {
        if (expected is null || expected.Value.ValueKind != JsonValueKind.Object)
            throw new AgentBenchmarkValidationException(
                "Agent benchmark case phải có expected object.");

        var failures = new List<string>();
        var spec = expected.Value;

        if (spec.TryGetProperty("contains", out var contains))
        {
            if (contains.ValueKind != JsonValueKind.Array)
                throw new AgentBenchmarkValidationException(
                    "expected.contains phải là mảng chuỗi.");
            foreach (var element in contains.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.String)
                    throw new AgentBenchmarkValidationException(
                        "expected.contains chỉ được chứa chuỗi.");
                var value = element.GetString() ?? string.Empty;
                if (!output.Contains(value, StringComparison.OrdinalIgnoreCase))
                    failures.Add($"missing:{value}");
            }
        }

        if (spec.TryGetProperty("notContains", out var notContains))
        {
            if (notContains.ValueKind != JsonValueKind.Array)
                throw new AgentBenchmarkValidationException(
                    "expected.notContains phải là mảng chuỗi.");
            foreach (var element in notContains.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.String)
                    throw new AgentBenchmarkValidationException(
                        "expected.notContains chỉ được chứa chuỗi.");
                var value = element.GetString() ?? string.Empty;
                if (output.Contains(value, StringComparison.OrdinalIgnoreCase))
                    failures.Add($"forbidden:{value}");
            }
        }

        if (spec.TryGetProperty("minimumLength", out var minimumLength))
        {
            if (!minimumLength.TryGetInt32(out var value) || value is < 0 or > 20_000)
                throw new AgentBenchmarkValidationException(
                    "expected.minimumLength phải là số nguyên 0–20000.");
            if (output.Length < value)
                failures.Add($"too-short:{output.Length}<{value}");
        }

        return failures;
    }

    private static string RequiredString(
        JsonElement source,
        string name,
        string caseId)
    {
        if (!source.TryGetProperty(name, out var value) ||
            value.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(value.GetString()))
            throw new AgentBenchmarkValidationException(
                $"{caseId}: thiếu {name} hợp lệ.");
        return value.GetString()!.Trim();
    }

    private static bool OptionalBoolean(
        JsonElement source,
        string name,
        bool fallback)
    {
        if (!source.TryGetProperty(name, out var value))
            return fallback;
        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw new AgentBenchmarkValidationException(
                $"{name} phải là boolean.")
        };
    }

    private sealed record BenchmarkSpec(
        string AgentId,
        string Goal,
        bool UseKnowledge,
        bool UseMemory,
        bool UseTaskContext,
        bool UseLifeContext);
}
