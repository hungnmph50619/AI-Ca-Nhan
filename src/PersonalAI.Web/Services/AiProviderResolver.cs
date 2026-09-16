namespace PersonalAI.Web.Services;

public interface IAiProviderResolver
{
    IAiProvider GetActive();
}

public sealed class AiProviderResolver : IAiProviderResolver
{
    private readonly IAiSettingsStore _settingsStore;
    private readonly GeminiChatService _gemini;
    private readonly OpenAiChatService _openAi;

    public AiProviderResolver(
        IAiSettingsStore settingsStore,
        GeminiChatService gemini,
        OpenAiChatService openAi)
    {
        _settingsStore = settingsStore;
        _gemini = gemini;
        _openAi = openAi;
    }

    public IAiProvider GetActive() =>
        _settingsStore.ActiveProvider.Equals("OpenAI", StringComparison.OrdinalIgnoreCase)
            ? _openAi
            : _gemini;
}
