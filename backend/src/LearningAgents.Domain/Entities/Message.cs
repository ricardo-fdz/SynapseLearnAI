using LearningAgents.Domain.Enums;

namespace LearningAgents.Domain.Entities;

public sealed class Message
{
    public int Id { get; set; }
    public int SessionId { get; set; }
    public MessageRole Role { get; set; }
    public string Content { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }

    public StudySession Session { get; set; } = null!;
    public ICollection<MemoryChange> MemoryChanges { get; set; } = [];
}
