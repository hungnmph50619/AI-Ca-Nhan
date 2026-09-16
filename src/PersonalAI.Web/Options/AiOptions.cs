namespace PersonalAI.Web.Options;

public sealed class AiOptions
{
    public const string SectionName = "AI";

    public string Provider { get; set; } = "Gemini";
}
