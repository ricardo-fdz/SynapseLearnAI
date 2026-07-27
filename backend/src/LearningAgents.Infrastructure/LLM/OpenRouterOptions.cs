namespace LearningAgents.Infrastructure.LLM;

public sealed class OpenRouterOptions
{
    public const string SectionName = "OpenRouter";

    public string ApiKey { get; set; } = string.Empty;

    public string HttpReferer { get; set; } = "http://localhost:5017";

    public string Title { get; set; } = "LearningAgents";
}
