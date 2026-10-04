using System.Net.Http.Json;
using System.Text.Json;

namespace PersonalAI.Web.Services;

public sealed record OpenHandsAgentServerOptions(
    bool Enabled,
    string BaseUrl,
    string Model,
    string ApiKey,
    int MaxIterations,
    bool AllowRemoteServer);

public sealed record OpenHandsAgentServerStatus(
    bool Enabled,
    bool Configured,
    bool Reachable,
    bool Ready,
    string BaseUrl,
    string Model,
    string ServerVersion,
    string Detail);

public sealed record OpenHandsConversationStartResult(
    bool Started,
    string ConversationId,
    string ExecutionStatus,
    string Detail);

public interface IOpenHandsAgentServerClient
{
    OpenHandsAgentServerOptions GetOptions();

    Task<OpenHandsAgentServerStatus> GetStatusAsync(
        CancellationToken cancellationToken = default);

    Task<OpenHandsConversationStartResult> StartCodingConversationAsync(
        string workingDirectory,
        string prompt,
        CancellationToken cancellationToken = default);
}

public sealed class OpenHandsAgentServerClient(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration)
    : IOpenHandsAgentServerClient
{
    private const int DefaultMaxIterations = 40;

    public OpenHandsAgentServerOptions GetOptions()
    {
        var section = configuration.GetSection(
            "OpenHands:AgentServer");

        return new(
            section.GetValue<bool>("Enabled"),
            (section["BaseUrl"] ?? string.Empty).Trim(),
            (section["Model"] ?? string.Empty).Trim(),
            section["ApiKey"] ?? string.Empty,
            Math.Clamp(
                section.GetValue<int?>("MaxIterations")
                    ?? DefaultMaxIterations,
                1,
                100),
            section.GetValue<bool>("AllowRemoteServer"));
    }

    public async Task<OpenHandsAgentServerStatus> GetStatusAsync(
        CancellationToken cancellationToken = default)
    {
        var options = GetOptions();

        if (!options.Enabled)
        {
            return new(
                Enabled: false,
                Configured: IsConfigured(options),
                Reachable: false,
                Ready: false,
                options.BaseUrl,
                options.Model,
                string.Empty,
                "OpenHands Agent Server adapter đang tắt.");
        }

        if (!TryValidateServerUri(
                options,
                out var baseUri,
                out var validationReason))
        {
            return new(
                Enabled: true,
                Configured: false,
                Reachable: false,
                Ready: false,
                options.BaseUrl,
                options.Model,
                string.Empty,
                validationReason);
        }

        try
        {
            using var client = CreateClient(baseUri!);

            using var health = await client.GetAsync(
                "health",
                cancellationToken);

            if (!health.IsSuccessStatusCode)
            {
                return new(
                    true,
                    IsConfigured(options),
                    Reachable: true,
                    Ready: false,
                    options.BaseUrl,
                    options.Model,
                    string.Empty,
                    $"OpenHands /health trả HTTP {(int)health.StatusCode}.");
            }

            using var ready = await client.GetAsync(
                "ready",
                cancellationToken);

            var isReady = ready.IsSuccessStatusCode;
            var version = string.Empty;

            try
            {
                var info = await client.GetFromJsonAsync<JsonElement>(
                    "server_info",
                    cancellationToken);

                if (info.ValueKind == JsonValueKind.Object &&
                    info.TryGetProperty(
                        "version",
                        out var versionProperty))
                {
                    version =
                        versionProperty.GetString()
                        ?? string.Empty;
                }
            }
            catch
            {
                // server_info is diagnostic-only; health/readiness remain authoritative.
            }

            return new(
                true,
                IsConfigured(options),
                Reachable: true,
                Ready: isReady,
                options.BaseUrl,
                options.Model,
                version,
                isReady
                    ? "OpenHands Agent Server sẵn sàng."
                    : $"OpenHands reachable nhưng /ready trả HTTP {(int)ready.StatusCode}.");
        }
        catch (Exception exception) when (
            exception is HttpRequestException or
            TaskCanceledException or
            JsonException)
        {
            return new(
                true,
                IsConfigured(options),
                Reachable: false,
                Ready: false,
                options.BaseUrl,
                options.Model,
                string.Empty,
                $"Không kết nối được OpenHands Agent Server: {exception.Message}");
        }
    }

    public async Task<OpenHandsConversationStartResult> StartCodingConversationAsync(
        string workingDirectory,
        string prompt,
        CancellationToken cancellationToken = default)
    {
        var options = GetOptions();

        if (!options.Enabled)
            throw new AgentValidationException(
                "OpenHands Agent Server adapter chưa được bật.");

        if (!TryValidateServerUri(
                options,
                out var baseUri,
                out var validationReason))
        {
            throw new AgentValidationException(
                validationReason);
        }

        if (string.IsNullOrWhiteSpace(options.Model))
        {
            throw new AgentValidationException(
                "OpenHands Agent Server cần cấu hình Model.");
        }

        if (string.IsNullOrWhiteSpace(workingDirectory))
        {
            throw new AgentValidationException(
                "OpenHands cần working directory.");
        }

        if (string.IsNullOrWhiteSpace(prompt))
        {
            throw new AgentValidationException(
                "OpenHands cần coding prompt.");
        }

        using var client = CreateClient(baseUri!);

        var llm = new Dictionary<string, object?>
        {
            ["model"] = options.Model,
            ["temperature"] = 0.0
        };

        if (!string.IsNullOrWhiteSpace(options.ApiKey))
            llm["api_key"] = options.ApiKey;

        var request = new
        {
            agent = new
            {
                llm,
                tools = new object[]
                {
                    new { name = "TerminalTool", params = new { } },
                    new { name = "FileEditorTool", params = new { } },
                    new { name = "TaskTrackerTool", params = new { } }
                },
                include_default_tools = new[]
                {
                    "FinishTool",
                    "ThinkTool"
                },
                kind = "Agent"
            },
            workspace = new
            {
                working_dir = workingDirectory.Trim(),
                kind = "LocalWorkspace"
            },
            confirmation_policy = new
            {
                kind = "AlwaysConfirm"
            },
            initial_message = new
            {
                role = "user",
                content = new object[]
                {
                    new
                    {
                        type = "text",
                        text = prompt.Trim()
                    }
                },
                run = false
            },
            max_iterations = options.MaxIterations,
            stuck_detection = true
        };

        using var response = await client.PostAsJsonAsync(
            "api/conversations",
            request,
            cancellationToken);

        var payload = await response.Content.ReadAsStringAsync(
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new AgentValidationException(
                $"OpenHands không tạo được conversation: HTTP {(int)response.StatusCode}; {Limit(payload)}");
        }

        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;

        var conversationId =
            root.TryGetProperty(
                "id",
                out var idProperty)
                ? idProperty.GetString()
                    ?? string.Empty
                : string.Empty;

        if (conversationId.Length == 0)
        {
            throw new AgentValidationException(
                "OpenHands trả về conversation nhưng thiếu id.");
        }

        using var runResponse = await client.PostAsync(
            $"api/conversations/{Uri.EscapeDataString(conversationId)}/run",
            content: null,
            cancellationToken);

        if (!runResponse.IsSuccessStatusCode &&
            (int)runResponse.StatusCode != 409)
        {
            var runPayload =
                await runResponse.Content.ReadAsStringAsync(
                    cancellationToken);

            throw new AgentValidationException(
                $"OpenHands không khởi chạy được conversation: HTTP {(int)runResponse.StatusCode}; {Limit(runPayload)}");
        }

        var status = root.TryGetProperty(
            "execution_status",
            out var statusProperty)
            ? statusProperty.GetString()
                ?? "idle"
            : "idle";

        return new(
            Started: true,
            conversationId,
            status,
            "OpenHands conversation đã được tạo với AlwaysConfirm + stuck detection; mọi side effect phải chờ xác nhận.");
    }

    private HttpClient CreateClient(
        Uri baseUri)
    {
        var client =
            httpClientFactory.CreateClient(
                "openhands-agent-server");

        client.BaseAddress = baseUri;
        client.Timeout = TimeSpan.FromSeconds(20);
        return client;
    }

    private static bool TryValidateServerUri(
        OpenHandsAgentServerOptions options,
        out Uri? baseUri,
        out string reason)
    {
        baseUri = null;

        if (string.IsNullOrWhiteSpace(options.BaseUrl) ||
            !Uri.TryCreate(
                options.BaseUrl,
                UriKind.Absolute,
                out var parsed) ||
            parsed.Scheme is not ("http" or "https"))
        {
            reason =
                "OpenHands Agent Server BaseUrl chưa hợp lệ.";
            return false;
        }

        var isLoopback =
            parsed.IsLoopback ||
            parsed.Host.Equals(
                "localhost",
                StringComparison.OrdinalIgnoreCase);

        if (!isLoopback &&
            !options.AllowRemoteServer)
        {
            reason =
                "OpenHands Agent Server mặc định chỉ cho loopback; bật AllowRemoteServer rõ ràng nếu thật sự cần remote.";
            return false;
        }

        baseUri = new Uri(
            parsed.AbsoluteUri.EndsWith("/")
                ? parsed.AbsoluteUri
                : parsed.AbsoluteUri + "/");

        reason = string.Empty;
        return true;
    }

    private static bool IsConfigured(
        OpenHandsAgentServerOptions options) =>
        !string.IsNullOrWhiteSpace(options.BaseUrl) &&
        !string.IsNullOrWhiteSpace(options.Model);

    private static string Limit(
        string value)
    {
        value = (value ?? string.Empty).Trim();

        return value.Length <= 800
            ? value
            : value[..800];
    }
}

public sealed record OpenHandsCodingCommand(
    string WorkingDirectory,
    string Prompt);

public sealed class OpenHandsCodingExecutionBackend(
    IOpenHandsAgentServerClient server)
    : ICodingExecutionBackend
{
    public string Engine => "openhands-agent-server";

    public bool TryParse(
        ExecutionAgentRequest request,
        out CodingExecutionCommand? command,
        out double confidence,
        out string reason)
    {
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

        if (!goal.StartsWith(
                "openhands:",
                StringComparison.OrdinalIgnoreCase))
        {
            confidence = 0;
            reason = "Không phải explicit OpenHands request.";
            return false;
        }

        var payload = goal["openhands:".Length..].Trim();
        var separator = payload.IndexOf('|');

        if (separator <= 0 ||
            separator >= payload.Length - 1)
        {
            confidence = 0.20;
            reason =
                "Cú pháp OpenHands: openhands: <workingDir> | <prompt>.";
            return false;
        }

        var workingDir = payload[..separator].Trim();
        var prompt = payload[(separator + 1)..].Trim();

        if (workingDir.Length == 0 ||
            prompt.Length == 0)
        {
            confidence = 0.20;
            reason =
                "OpenHands cần workingDir và prompt.";
            return false;
        }

        command = new(
            "openhands",
            payload,
            "Debug");

        var options = server.GetOptions();

        if (!options.Enabled)
        {
            confidence = 0.45;
            reason =
                "OpenHands request hợp lệ nhưng adapter đang tắt.";
            return false;
        }

        confidence = 0.98;
        reason =
            "Explicit OpenHands coding request.";
        return true;
    }

    public async Task<CodingExecutionBackendResult> ExecuteAsync(
        CodingExecutionCommand command,
        CancellationToken cancellationToken = default)
    {
        if (!command.Operation.Equals(
                "openhands",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new AgentValidationException(
                "OpenHands backend chỉ thực thi operation=openhands.");
        }

        var separator = command.TargetPath.IndexOf('|');

        if (separator <= 0 ||
            separator >= command.TargetPath.Length - 1)
        {
            throw new AgentValidationException(
                "OpenHands command payload không hợp lệ.");
        }

        var workingDir =
            command.TargetPath[..separator].Trim();
        var prompt =
            command.TargetPath[(separator + 1)..].Trim();

        var result =
            await server.StartCodingConversationAsync(
                workingDir,
                prompt,
                cancellationToken);

        return new(
            result.Started,
            $"OpenHands conversation {result.ConversationId} đã khởi chạy ở chế độ confirmation.",
            new[]
            {
                $"conversationId={result.ConversationId}",
                $"executionStatus={result.ExecutionStatus}",
                result.Detail
            },
            ChangedExternalState: true,
            Verified: false,
            Engine);
    }
}

public sealed class CompositeCodingExecutionBackend(
    SafeDevelopmentCodingBackend internalBackend,
    OpenHandsCodingExecutionBackend openHandsBackend)
    : ICodingExecutionBackend
{
    public string Engine => "composite-coding";

    public bool TryParse(
        ExecutionAgentRequest request,
        out CodingExecutionCommand? command,
        out double confidence,
        out string reason)
    {
        if ((request.Goal ?? string.Empty)
            .TrimStart()
            .StartsWith(
                "openhands:",
                StringComparison.OrdinalIgnoreCase))
        {
            return openHandsBackend.TryParse(
                request,
                out command,
                out confidence,
                out reason);
        }

        return internalBackend.TryParse(
            request,
            out command,
            out confidence,
            out reason);
    }

    public Task<CodingExecutionBackendResult> ExecuteAsync(
        CodingExecutionCommand command,
        CancellationToken cancellationToken = default) =>
        command.Operation.Equals(
            "openhands",
            StringComparison.OrdinalIgnoreCase)
            ? openHandsBackend.ExecuteAsync(
                command,
                cancellationToken)
            : internalBackend.ExecuteAsync(
                command,
                cancellationToken);
}
