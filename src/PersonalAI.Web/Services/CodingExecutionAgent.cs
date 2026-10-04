using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public static class CodingExecutionOperations
{
    public const string Inspect = "inspect";
    public const string GitStatus = "git-status";
    public const string GitDiff = "git-diff";
    public const string DotnetRestore = "dotnet-restore";
    public const string DotnetBuild = "dotnet-build";
    public const string DotnetTest = "dotnet-test";
}

public sealed record CodingExecutionCommand(
    string Operation,
    string TargetPath = ".",
    string Configuration = "Debug");

public sealed record CodingExecutionBackendResult(
    bool Success,
    string Summary,
    IReadOnlyList<string> Evidence,
    bool ChangedExternalState,
    bool Verified,
    string Engine);

public interface ICodingExecutionBackend
{
    string Engine { get; }

    bool TryParse(
        ExecutionAgentRequest request,
        out CodingExecutionCommand? command,
        out double confidence,
        out string reason);

    Task<CodingExecutionBackendResult> ExecuteAsync(
        CodingExecutionCommand command,
        CancellationToken cancellationToken = default);
}

public sealed class SafeDevelopmentCodingBackend(
    IDevelopmentAgentService development)
    : ICodingExecutionBackend
{
    public string Engine => "internal-development-safe";

    public bool TryParse(
        ExecutionAgentRequest request,
        out CodingExecutionCommand? command,
        out double confidence,
        out string reason)
    {
        ArgumentNullException.ThrowIfNull(request);

        command = null;

        if (!request.Channel.Equals(
                ExecutionAgentChannels.Coding,
                StringComparison.OrdinalIgnoreCase))
        {
            confidence = 0;
            reason = "Backend chỉ nhận channel coding.";
            return false;
        }

        var goal = (request.Goal ?? string.Empty).Trim();
        if (goal.Length == 0)
        {
            confidence = 0;
            reason = "Goal trống.";
            return false;
        }

        var separator = goal.IndexOf(':');
        var operation = separator < 0
            ? goal
            : goal[..separator];
        var argument = separator < 0
            ? string.Empty
            : goal[(separator + 1)..].Trim();

        operation = operation
            .Trim()
            .ToLowerInvariant()
            .Replace(' ', '-');

        switch (operation)
        {
            case CodingExecutionOperations.Inspect:
                command = new(
                    CodingExecutionOperations.Inspect);
                break;

            case CodingExecutionOperations.GitStatus:
                command = new(
                    CodingExecutionOperations.GitStatus,
                    NormalizePath(argument));
                break;

            case CodingExecutionOperations.GitDiff:
                command = new(
                    CodingExecutionOperations.GitDiff,
                    NormalizePath(argument));
                break;

            case CodingExecutionOperations.DotnetRestore:
            case CodingExecutionOperations.DotnetBuild:
            case CodingExecutionOperations.DotnetTest:
                if (string.IsNullOrWhiteSpace(argument))
                {
                    confidence = 0;
                    reason =
                        $"{operation} yêu cầu targetPath sau dấu ':'.";
                    return false;
                }

                command = new(
                    operation,
                    argument,
                    "Debug");
                break;

            default:
                confidence = 0.30;
                reason =
                    "Coding backend v3.9.0 chỉ nhận command explicit: inspect, git-status, git-diff, dotnet-restore, dotnet-build, dotnet-test.";
                return false;
        }

        confidence = 0.99;
        reason =
            $"Đã parse coding command explicit: {command.Operation}.";
        return true;
    }

    public async Task<CodingExecutionBackendResult> ExecuteAsync(
        CodingExecutionCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        switch (command.Operation)
        {
            case CodingExecutionOperations.Inspect:
            {
                var result = development.InspectWorkspace();
                var evidence = result.Projects
                    .Take(20)
                    .Select(project =>
                        $"{project.Kind}: {project.Path}")
                    .ToArray();

                return new(
                    true,
                    $"Đã inspect workspace; projects={result.Projects.Count}.",
                    evidence,
                    ChangedExternalState: false,
                    Verified: true,
                    Engine);
            }

            case CodingExecutionOperations.GitStatus:
            {
                var result = await development.GitStatusAsync(
                    command.TargetPath,
                    cancellationToken);

                return FromGit(
                    "git status",
                    result,
                    changedExternalState: false);
            }

            case CodingExecutionOperations.GitDiff:
            {
                var result = await development.GitDiffAsync(
                    command.TargetPath,
                    staged: false,
                    cancellationToken);

                return FromGit(
                    "git diff",
                    result,
                    changedExternalState: false);
            }

            case CodingExecutionOperations.DotnetRestore:
            {
                var result = await development.DotnetRestoreAsync(
                    command.TargetPath,
                    cancellationToken);

                return FromProcess(
                    "dotnet restore",
                    result,
                    changedExternalState: true);
            }

            case CodingExecutionOperations.DotnetBuild:
            {
                var result = await development.DotnetBuildAsync(
                    command.TargetPath,
                    command.Configuration,
                    cancellationToken);

                return FromProcess(
                    "dotnet build",
                    result,
                    changedExternalState: true);
            }

            case CodingExecutionOperations.DotnetTest:
            {
                var result = await development.DotnetTestAsync(
                    command.TargetPath,
                    command.Configuration,
                    cancellationToken);

                return FromProcess(
                    "dotnet test",
                    result,
                    changedExternalState: true);
            }

            default:
                throw new AgentValidationException(
                    $"Coding operation không hỗ trợ: {command.Operation}.");
        }
    }

    private CodingExecutionBackendResult FromGit(
        string operation,
        DevelopmentGitResult result,
        bool changedExternalState)
    {
        var evidence = new[]
        {
            $"exitCode={result.ExitCode}",
            Limit(result.StdOut),
            Limit(result.StdErr)
        }
        .Where(value =>
            !string.IsNullOrWhiteSpace(value))
        .ToArray();

        return new(
            result.Succeeded,
            $"{operation}: exitCode={result.ExitCode}.",
            evidence,
            changedExternalState,
            Verified: result.Succeeded,
            Engine);
    }

    private CodingExecutionBackendResult FromProcess(
        string operation,
        DevelopmentProcessResult result,
        bool changedExternalState)
    {
        var evidence = new[]
        {
            $"exitCode={result.ExitCode}; timedOut={result.TimedOut}",
            Limit(result.StdOut),
            Limit(result.StdErr)
        }
        .Where(value =>
            !string.IsNullOrWhiteSpace(value))
        .ToArray();

        return new(
            result.Succeeded,
            $"{operation}: exitCode={result.ExitCode}; timedOut={result.TimedOut}.",
            evidence,
            changedExternalState,
            Verified:
                result.Succeeded &&
                !result.TimedOut,
            Engine);
    }

    private static string NormalizePath(
        string value) =>
        string.IsNullOrWhiteSpace(value)
            ? "."
            : value.Trim();

    private static string Limit(
        string? value)
    {
        var text = (value ?? string.Empty).Trim();

        return text.Length <= 2_000
            ? text
            : text[..2_000];
    }
}

public sealed class CodingExecutionAgent(
    ICodingExecutionBackend backend)
    : IExecutionAgent
{
    public const string AgentId =
        "execution.coding-agent";

    public ExecutionAgentDefinition Definition { get; } = new(
        AgentId,
        "Coding Agent",
        "Execution agent cho tác vụ phát triển phần mềm qua backend được kiểm soát. v3.9.0 dùng command explicit và không nhận arbitrary shell.",
        [
            "workspace-inspection",
            "git-read",
            "dotnet-restore",
            "dotnet-build",
            "dotnet-test",
            "verification"
        ],
        [ExecutionAgentChannels.Coding],
        HasSideEffects: true,
        RequiresExplicitInvocation: true,
        SupportsVerification: true,
        SupportsRecovery: false);

    public bool CanHandle(
        ExecutionAgentRequest request,
        out double confidence,
        out string reason) =>
        backend.TryParse(
            request,
            out _,
            out confidence,
            out reason);

    public async Task<ExecutionAgentResult> ExecuteAsync(
        ExecutionAgentRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!backend.TryParse(
                request,
                out var command,
                out _,
                out var reason) ||
            command is null)
        {
            throw new AgentValidationException(reason);
        }

        var result = await backend.ExecuteAsync(
            command,
            cancellationToken);

        return new(
            AgentId,
            result.Success
                ? AgentExecutionStatuses.Succeeded
                : AgentExecutionStatuses.Failed,
            result.Summary,
            result.Evidence,
            result.ChangedExternalState,
            result.Verified,
            Provider: "coding",
            Model: result.Engine);
    }
}
