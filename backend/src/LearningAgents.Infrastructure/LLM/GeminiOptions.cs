namespace LearningAgents.Infrastructure.LLM;

public sealed class GeminiOptions
{
    public const string SectionName = "Gemini";

    public string ApiKey { get; set; } = string.Empty;

    public string DefaultModel { get; set; } = "gemini-2.5-flash";

    public string FallbackModel { get; set; } = string.Empty;
}
