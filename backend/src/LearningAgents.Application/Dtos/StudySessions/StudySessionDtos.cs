using System.ComponentModel.DataAnnotations;

namespace LearningAgents.Application.Dtos.StudySessions;

public sealed record StudySessionResponse(
    int Id,
    int TutorId,
    string Name,
    string Goal,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public sealed record CreateStudySessionRequest(
    [param: Range(1, int.MaxValue)] int TutorId,
    [param: Required, StringLength(200)] string Name,
    [param: Required, StringLength(1000)] string Goal);

public sealed record UpdateStudySessionRequest(
    [param: Range(1, int.MaxValue)] int TutorId,
    [param: Required, StringLength(200)] string Name,
    [param: Required, StringLength(1000)] string Goal);
