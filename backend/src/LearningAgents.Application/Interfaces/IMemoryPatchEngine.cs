using LearningAgents.Application.Dtos.MemoryPatches;
using LearningAgents.Domain.Memory;

namespace LearningAgents.Application.Interfaces;

public interface IMemoryPatchEngine
{
    Task<MemoryPatchResult> ApplyPatchAsync(
        int tutorId,
        MemoryPatch patch,
        int? messageId,
        CancellationToken cancellationToken = default);
}
