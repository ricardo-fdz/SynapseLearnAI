using System.Text.Json;
using LearningAgents.Domain.Enums;

namespace LearningAgents.Domain.Memory;

public sealed record MemoryPatch(
    string Key,
    MemoryPatchOperation Operation,
    string Path,
    string? TargetId,
    JsonElement Value,
    string Reason);
