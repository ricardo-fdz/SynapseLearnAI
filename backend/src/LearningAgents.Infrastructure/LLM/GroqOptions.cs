namespace LearningAgents.Infrastructure.LLM;

public sealed class GroqOptions
{
    public const string SectionName = "Groq";

    public string ApiKey { get; set; } = string.Empty;
}
