using LearningAgents.Application.Enums;

namespace LearningAgents.Application.Interfaces;

public interface IPromptBuilder
{
    Task<string> BuildSystemPromptAsync(
        int tutorId,
        ContextLoadProfile profile,
        CancellationToken cancellationToken = default);

    Task<string> BuildSystemPromptAsync(
        int tutorId,
        ContextLoadProfile profile,
        string? sessionGoal,
        CancellationToken cancellationToken = default);
}
