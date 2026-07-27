using System.ComponentModel.DataAnnotations;

namespace LearningAgents.Application.Dtos.Messages;

public sealed record MessageResponse(
    int Id,
    int SessionId,
    string Role,
    string Content,
    DateTime CreatedAtUtc);

public sealed record CreateMessageRequest(
    [param: Range(1, int.MaxValue)] int SessionId,
    [param: Required] string Role,
    [param: Required] string Content);
