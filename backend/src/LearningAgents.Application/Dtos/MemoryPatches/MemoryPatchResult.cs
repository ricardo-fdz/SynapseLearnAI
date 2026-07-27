using LearningAgents.Application.Dtos.MemoryChanges;
using LearningAgents.Application.Dtos.MemoryEntries;

namespace LearningAgents.Application.Dtos.MemoryPatches;

public sealed record MemoryPatchResult(
    MemoryEntryResponse MemoryEntry,
    MemoryChangeResponse MemoryChange);
