namespace PersonalAI.Web.Options;

public sealed class OpenAiOptions
{
    public const string SectionName = "OpenAI";

    public string Model { get; set; } = "gpt-5.6-luna";
    public int MaxOutputTokens { get; set; } = 2048;
    public string ApiKey { get; set; } = string.Empty;
}
