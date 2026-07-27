namespace LearningAgents.Domain.Entities;

public sealed class StudySession
{
    public int Id { get; set; }
    public int TutorId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Goal { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }

    public Tutor Tutor { get; set; } = null!;
    public ICollection<Message> Messages { get; set; } = [];
}
