namespace LearningAgents.Domain.Entities;

public sealed class Tutor
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string SystemPromptContent { get; set; } = string.Empty;
    public string GeminiModel { get; set; } = string.Empty;
    public string LlmProfile { get; set; } = "gemini-default";
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }

    public ICollection<StudySession> StudySessions { get; set; } = [];
    public ICollection<MemoryEntry> MemoryEntries { get; set; } = [];
}
