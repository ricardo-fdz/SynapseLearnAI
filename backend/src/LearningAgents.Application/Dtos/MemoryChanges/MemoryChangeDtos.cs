namespace LearningAgents.Application.Dtos.MemoryChanges;

public sealed record MemoryChangeResponse(
    int Id,
    int MemoryEntryId,
    int? MessageId,
    string Operation,
    string Path,
    string TargetId,
    string PreviousValueJson,
    string NewValueJson,
    string Reason,
    DateTime CreatedAtUtc);

public sealed record MemoryChangeHistoryResponse(
    int Id,
    int MemoryEntryId,
    string MemoryEntryKey,
    int? MessageId,
    string Operation,
    string Path,
    string TargetId,
    string PreviousValueJson,
    string NewValueJson,
    string Reason,
    DateTime CreatedAtUtc);

public sealed record PagedMemoryChangeHistoryResponse(
    int Page,
    int PageSize,
    int TotalCount,
    IReadOnlyList<MemoryChangeHistoryResponse> Items);
