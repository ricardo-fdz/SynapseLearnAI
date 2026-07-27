using System.ComponentModel.DataAnnotations;

namespace LearningAgents.Application.Dtos.MemoryEntries;

public sealed record MemoryEntryResponse(
    int Id,
    int TutorId,
    string Key,
    string ValueJson,
    int SchemaVersion,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public sealed record CreateMemoryEntryRequest(
    [param: Range(1, int.MaxValue)] int TutorId,
    [param: Required, StringLength(100)] string Key,
    [param: Required] string ValueJson,
    [param: Range(1, int.MaxValue)] int SchemaVersion);

public sealed record UpdateMemoryEntryRequest(
    [param: Range(1, int.MaxValue)] int TutorId,
    [param: Required, StringLength(100)] string Key,
    [param: Required] string ValueJson,
    [param: Range(1, int.MaxValue)] int SchemaVersion);
