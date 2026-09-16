namespace PersonalAI.Web.Models;

public sealed record AiSettingsResponse(
    string Provider,
    string Model,
    bool HasApiKey,
    string? MaskedApiKey);

public sealed record UpdateAiSettingsRequest(
    string Provider,
    string Model,
    string? ApiKey);

public sealed record AiConnectionTestResponse(bool Success, string Message);
