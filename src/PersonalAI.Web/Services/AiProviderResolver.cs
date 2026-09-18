namespace PersonalAI.Web.Services;

public interface IAiProviderResolver
{
    IAiProvider GetActive();
    IAiProvider GetByName(string provider);
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

    public IAiProvider GetActive() => GetByName(_settingsStore.ActiveProvider);

    public IAiProvider GetByName(string provider) =>
        provider.Equals("OpenAI", StringComparison.OrdinalIgnoreCase)
            ? _openAi
            : provider.Equals("Gemini", StringComparison.OrdinalIgnoreCase)
                ? _gemini
                : throw new InvalidOperationException(
                    "Nhà cung cấp AI của native tool call không còn được hỗ trợ.");
}
