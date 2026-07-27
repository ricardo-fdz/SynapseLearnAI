namespace LearningAgents.Infrastructure.LLM;

public sealed class LlmProfilesOptions
{
    public const string SectionName = "LlmProfiles";

    public string DefaultProfile { get; set; } = "gemini-default";

    public Dictionary<string, LlmProfileOptions> Profiles { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class LlmProfileOptions
{
    public string Provider { get; set; } = "gemini";

    public string Model { get; set; } = string.Empty;

    public string[] FallbackProfiles { get; set; } = [];
}
