using LearningAgents.Domain.Enums;

namespace LearningAgents.Domain.Entities;

public sealed class MemoryChange
{
    public int Id { get; set; }
    public int MemoryEntryId { get; set; }
    public int? MessageId { get; set; }
    public MemoryPatchOperation Operation { get; set; }
    public string Path { get; set; } = string.Empty;
    public string TargetId { get; set; } = string.Empty;
    public string PreviousValueJson { get; set; } = string.Empty;
    public string NewValueJson { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }

    public MemoryEntry MemoryEntry { get; set; } = null!;
    public Message? Message { get; set; }
}
