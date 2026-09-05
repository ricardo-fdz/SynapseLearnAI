namespace LearningAgents.Domain.Entities;

public sealed class MemoryEntry
{
    public int Id { get; set; }
    public int TutorId { get; set; }
    public string Key { get; set; } = string.Empty;
    public string ValueJson { get; set; } = string.Empty;
    public int SchemaVersion { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public byte[]? RowVersion { get; set; }

    public Tutor Tutor { get; set; } = null!;
    public ICollection<MemoryChange> MemoryChanges { get; set; } = [];
}
