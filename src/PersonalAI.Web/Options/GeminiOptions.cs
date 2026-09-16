namespace PersonalAI.Web.Options;

public sealed class GeminiOptions
{
    public const string SectionName = "Gemini";

    public string Model { get; set; } = "gemini-3.1-flash-lite";
    public int MaxOutputTokens { get; set; } = 2048;
    public string ApiKey { get; set; } = string.Empty;
}
