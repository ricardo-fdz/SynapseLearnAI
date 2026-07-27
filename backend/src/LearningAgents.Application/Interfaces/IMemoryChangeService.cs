using LearningAgents.Application.Dtos.MemoryChanges;

namespace LearningAgents.Application.Interfaces;

public interface IMemoryChangeService
{
    Task<IReadOnlyList<MemoryChangeResponse>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<MemoryChangeResponse?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<PagedMemoryChangeHistoryResponse?> GetByTutorIdAsync(
        int tutorId,
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default);

    Task<PagedMemoryChangeHistoryResponse?> GetByMemoryEntryIdAsync(
        int memoryEntryId,
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default);
}
