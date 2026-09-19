using System.Text;
using System.Text.Json;
using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public static class PlannerAgentLimits
{
    private static readonly Lazy<bool> ParserSelfTest =
        new(PlannerPlanParser.RunSelfTest);

    public const int MinimumSteps = 2;
    public const int MaximumSteps = 12;
    public const int MaximumOpenQuestions = 8;
    public const int MaximumAssumptions = 8;
    public const int MaximumRisks = 8;
    public const int MaximumCapabilitiesPerStep = 8;

    public static PlannerAgentStatusResponse GetStatus() =>
        new(
            PersonalAiRelease.Version,
            PlannerFrameworkAgent.AgentId,
            MinimumSteps,
            MaximumSteps,
            MaximumOpenQuestions,
            MaximumAssumptions,
            MaximumRisks,
            PlannerSuggestedRoles.All,
            StructuredOutput: true,
            ParserSelfTestPassed: ParserSelfTest.Value,
            CreatesTasks: false,
            DispatchesAgents: false,
            PersistsPlans: false,
            ToolExecutionEnabled: false,
            RequiresExplicitInvocation: true,
            NextStage: "v2.2.2-orchestration-reliability");
}

public sealed class PlannerOutputException : InvalidOperationException
{
    public PlannerOutputException(string message)
        : base(message)
    {
    }

    public PlannerOutputException(
        string message,
        Exception innerException)
        : base(message, innerException)
    {
    }
}

public sealed class PlannerFrameworkAgent(
    IChatTurnService chat) : IAgent
{
    public const string AgentId = "planning.planner";

    private const string PlannerPrompt = """
        AGENT FRAMEWORK v2.1.1
        Agent: Planner Agent
        Role: planner

        NHIỆM VỤ:
        Nhận một goal của người dùng và chia goal đó thành một kế hoạch có cấu trúc.

        BOUNDARY:
        - Chỉ lập kế hoạch.
        - Không chạy tool.
        - Không tạo Task Engine task.
        - Không gọi hoặc giao việc cho agent khác.
        - Không tạo automation.
        - Không tự xác nhận side effect.
        - suggestedRole chỉ là gợi ý metadata cho các phiên bản orchestration sau.
        - Mọi dependency phải chỉ trỏ tới bước đứng trước nó.
        - Không được tạo dependency vòng.
        - Tạo từ 2 đến 12 bước.

        suggestedRole chỉ được dùng một trong:
        general-assistant, research, developer, office, operator, reviewer, security, user

        Chỉ trả về MỘT JSON object hợp lệ, không markdown, không code fence, không giải thích ngoài JSON.

        Schema:
        {
          "summary": "Tóm tắt ngắn cách tiếp cận",
          "steps": [
            {
              "step": 1,
              "title": "Tên bước",
              "description": "Việc cần làm",
              "dependsOn": [],
              "suggestedRole": "general-assistant",
              "requiredCapabilities": ["capability-name"],
              "requiresUserInput": false,
              "expectedOutcome": "Kết quả mong đợi"
            }
          ],
          "openQuestions": [],
          "assumptions": [],
          "risks": []
        }
        """;

    public AgentDefinition Definition { get; } = new(
        AgentId,
        "Planner Agent",
        "planner",
        "Nhận goal và chia thành các bước có dependency, outcome và role/capability gợi ý; không tự dispatch hoặc tạo task.",
        [
            "goal-decomposition",
            "step-sequencing",
            "dependency-analysis",
            "capability-mapping",
            "risk-identification",
            "context-grounded-planning"
        ],
        [],
        [
            AgentContextKinds.Memory,
            AgentContextKinds.Knowledge,
            AgentContextKinds.Tasks,
            AgentContextKinds.LifeContext
        ],
        UsesAiProvider: true,
        ToolExecutionEnabled: false,
        RequiresExplicitInvocation: true);

    public async Task<AgentResult> ExecuteAsync(
        AgentExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        var request = context.Request;
        var messages = new List<ChatMessage>();

        if (request.Conversation is not null)
        {
            messages.AddRange(request.Conversation);
        }

        messages.Add(new ChatMessage(
            "user",
            PlannerPrompt
                + Environment.NewLine
                + Environment.NewLine
                + "MỤC TIÊU:"
                + Environment.NewLine
                + (request.Goal ?? string.Empty)));

        var response = await chat.ExecuteAsync(
            new ChatRequest(
                messages,
                request.UseKnowledge,
                "normal",
                request.UseMemory,
                UseTools: false,
                UseTaskContext: request.UseTaskContext,
                UseLifeContext: request.UseLifeContext),
            allowToolProposal: false,
            auditAgent: Definition.Id,
            auditReason: "planner-agent-v2.1.1-explicit-invocation",
            cancellationToken);

        var plan = PlannerPlanParser.Parse(
            request.Goal ?? string.Empty,
            response.Message,
            DateTimeOffset.UtcNow);

        return new AgentResult(
            PlannerPlanRenderer.Render(plan),
            response.Provider,
            response.Model,
            response.Sources,
            response.Context,
            plan);
    }
}

public static class PlannerPlanParser
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true,
            AllowTrailingCommas = true
        };

    private static readonly HashSet<string> AllowedRoles =
        new(
            PlannerSuggestedRoles.All,
            StringComparer.OrdinalIgnoreCase);

    public static bool RunSelfTest()
    {
        const string sample = """
            {
              "summary": "Self-test plan",
              "steps": [
                {
                  "step": 1,
                  "title": "Inspect",
                  "description": "Inspect context.",
                  "dependsOn": [],
                  "suggestedRole": "research",
                  "requiredCapabilities": ["context-grounding"],
                  "requiresUserInput": false,
                  "expectedOutcome": "Context understood."
                },
                {
                  "step": 2,
                  "title": "Prepare",
                  "description": "Prepare next work.",
                  "dependsOn": [1],
                  "suggestedRole": "developer",
                  "requiredCapabilities": ["planning"],
                  "requiresUserInput": false,
                  "expectedOutcome": "Prepared work."
                }
              ],
              "openQuestions": [],
              "assumptions": [],
              "risks": []
            }
            """;

        try
        {
            var plan = Parse(
                "planner-self-test",
                sample,
                DateTimeOffset.UnixEpoch);

            return plan.Status == PlannerPlanStatuses.Prepared
                && plan.Steps.Count == 2
                && plan.Steps[1].DependsOn.SequenceEqual([1])
                && !plan.CreatesTasks
                && !plan.DispatchesAgents
                && !plan.PersistsAutomatically;
        }
        catch
        {
            return false;
        }
    }

    public static PlannerPlan Parse(
        string goal,
        string rawOutput,
        DateTimeOffset createdAt)
    {
        var json = ExtractJson(rawOutput);
        PlannerAiPlan? parsed;

        try
        {
            parsed = JsonSerializer.Deserialize<PlannerAiPlan>(
                json,
                JsonOptions);
        }
        catch (JsonException exception)
        {
            throw new PlannerOutputException(
                "Planner Agent trả về JSON không hợp lệ.",
                exception);
        }

        if (parsed is null)
        {
            throw new PlannerOutputException(
                "Planner Agent không trả về plan hợp lệ.");
        }

        var summary = NormalizeRequired(
            parsed.Summary,
            1_200,
            "summary");

        if (parsed.Steps is null
            || parsed.Steps.Count is < PlannerAgentLimits.MinimumSteps
                or > PlannerAgentLimits.MaximumSteps)
        {
            throw new PlannerOutputException(
                "Planner Agent phải tạo từ "
                + PlannerAgentLimits.MinimumSteps
                + " đến "
                + PlannerAgentLimits.MaximumSteps
                + " bước.");
        }

        var steps = new List<PlannerPlanStep>(
            parsed.Steps.Count);

        for (var index = 0; index < parsed.Steps.Count; index++)
        {
            var rawStep = parsed.Steps[index]
                ?? throw new PlannerOutputException(
                    "Planner Agent chứa step rỗng.");
            var expectedStep = index + 1;

            if (rawStep.Step != expectedStep)
            {
                throw new PlannerOutputException(
                    "Planner Agent phải đánh số step liên tục từ 1. Step "
                    + expectedStep
                    + " không hợp lệ.");
            }

            var dependencies = NormalizeDependencies(
                rawStep.DependsOn,
                expectedStep);

            var role = NormalizeRole(
                rawStep.SuggestedRole);

            steps.Add(
                new PlannerPlanStep(
                    expectedStep,
                    NormalizeRequired(
                        rawStep.Title,
                        180,
                        "step title"),
                    NormalizeRequired(
                        rawStep.Description,
                        1_500,
                        "step description"),
                    dependencies,
                    role,
                    NormalizeCapabilities(
                        rawStep.RequiredCapabilities),
                    rawStep.RequiresUserInput,
                    NormalizeRequired(
                        rawStep.ExpectedOutcome,
                        900,
                        "expected outcome")));
        }

        return new PlannerPlan(
            Guid.NewGuid(),
            NormalizeRequired(
                goal,
                AgentFrameworkLimits.MaximumGoalCharacters,
                "goal"),
            summary,
            PlannerPlanStatuses.Prepared,
            steps,
            NormalizeList(
                parsed.OpenQuestions,
                PlannerAgentLimits.MaximumOpenQuestions,
                500),
            NormalizeList(
                parsed.Assumptions,
                PlannerAgentLimits.MaximumAssumptions,
                500),
            NormalizeList(
                parsed.Risks,
                PlannerAgentLimits.MaximumRisks,
                500),
            createdAt,
            CreatesTasks: false,
            DispatchesAgents: false,
            PersistsAutomatically: false);
    }

    private static string ExtractJson(
        string rawOutput)
    {
        var value = (rawOutput ?? string.Empty).Trim();
        if (value.Length == 0)
        {
            throw new PlannerOutputException(
                "Planner Agent trả về nội dung trống.");
        }

        const string fence = "\u0060\u0060\u0060";
        if (value.StartsWith(
            fence,
            StringComparison.Ordinal))
        {
            var firstLineEnd = value.IndexOf('\n');
            if (firstLineEnd >= 0)
            {
                value = value[(firstLineEnd + 1)..];
            }

            var closingFence = value.LastIndexOf(
                fence,
                StringComparison.Ordinal);
            if (closingFence >= 0)
            {
                value = value[..closingFence];
            }

            value = value.Trim();
        }

        var start = value.IndexOf('{');
        var end = value.LastIndexOf('}');
        if (start < 0
            || end < start)
        {
            throw new PlannerOutputException(
                "Planner Agent không trả về JSON object.");
        }

        return value[start..(end + 1)];
    }

    private static IReadOnlyList<int> NormalizeDependencies(
        IReadOnlyList<int>? values,
        int currentStep)
    {
        if (values is null
            || values.Count == 0)
        {
            return [];
        }

        var normalized = values
            .Distinct()
            .Order()
            .ToArray();

        foreach (var dependency in normalized)
        {
            if (dependency < 1
                || dependency >= currentStep)
            {
                throw new PlannerOutputException(
                    "Step "
                    + currentStep
                    + " có dependency không hợp lệ: "
                    + dependency
                    + ". Dependency chỉ được trỏ tới step trước đó.");
            }
        }

        return normalized;
    }

    private static string NormalizeRole(
        string? value)
    {
        var normalized = (value ?? string.Empty)
            .Trim()
            .ToLowerInvariant();

        return AllowedRoles.Contains(normalized)
            ? normalized
            : "general-assistant";
    }

    private static IReadOnlyList<string> NormalizeCapabilities(
        IReadOnlyList<string>? values)
    {
        if (values is null)
        {
            return [];
        }

        return values
            .Where(value =>
                !string.IsNullOrWhiteSpace(value))
            .Select(value =>
                value.Trim())
            .Distinct(
                StringComparer.OrdinalIgnoreCase)
            .Take(
                PlannerAgentLimits.MaximumCapabilitiesPerStep)
            .Select(value =>
                value.Length <= 80
                    ? value
                    : value[..80])
            .ToArray();
    }

    private static IReadOnlyList<string> NormalizeList(
        IReadOnlyList<string>? values,
        int maximumItems,
        int maximumCharacters)
    {
        if (values is null)
        {
            return [];
        }

        return values
            .Where(value =>
                !string.IsNullOrWhiteSpace(value))
            .Select(value =>
                value.Trim())
            .Distinct(
                StringComparer.OrdinalIgnoreCase)
            .Take(maximumItems)
            .Select(value =>
                value.Length <= maximumCharacters
                    ? value
                    : value[..maximumCharacters])
            .ToArray();
    }

    private static string NormalizeRequired(
        string? value,
        int maximumCharacters,
        string field)
    {
        var normalized = (value ?? string.Empty)
            .Trim();

        if (normalized.Length == 0)
        {
            throw new PlannerOutputException(
                "Planner Agent thiếu trường bắt buộc: "
                + field
                + ".");
        }

        return normalized.Length <= maximumCharacters
            ? normalized
            : normalized[..maximumCharacters];
    }

    private sealed record PlannerAiPlan(
        string? Summary,
        IReadOnlyList<PlannerAiStep?>? Steps,
        IReadOnlyList<string>? OpenQuestions,
        IReadOnlyList<string>? Assumptions,
        IReadOnlyList<string>? Risks);

    private sealed record PlannerAiStep(
        int Step,
        string? Title,
        string? Description,
        IReadOnlyList<int>? DependsOn,
        string? SuggestedRole,
        IReadOnlyList<string>? RequiredCapabilities,
        bool RequiresUserInput,
        string? ExpectedOutcome);
}

public static class PlannerPlanRenderer
{
    public static string Render(
        PlannerPlan plan)
    {
        var builder = new StringBuilder();
        builder.AppendLine(plan.Summary);
        builder.AppendLine();

        foreach (var step in plan.Steps)
        {
            builder.Append(step.Step)
                .Append(". ")
                .AppendLine(step.Title);
            builder.Append("   ")
                .AppendLine(step.Description);
            builder.Append("   Kết quả: ")
                .AppendLine(step.ExpectedOutcome);

            if (step.DependsOn.Count > 0)
            {
                builder.Append("   Phụ thuộc: ")
                    .AppendLine(
                        string.Join(
                            ", ",
                            step.DependsOn));
            }

            builder.Append("   Role gợi ý: ")
                .AppendLine(step.SuggestedRole);
        }

        if (plan.OpenQuestions.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("Cần làm rõ:");
            foreach (var question in plan.OpenQuestions)
            {
                builder.Append("- ")
                    .AppendLine(question);
            }
        }

        return builder
            .ToString()
            .Trim();
    }
}
