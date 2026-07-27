using LearningAgents.Application.Common;
using LearningAgents.Application.Dtos.MemoryEntries;

namespace LearningAgents.Application.Interfaces;

public interface IMemoryEntryService
{
    Task<IReadOnlyList<MemoryEntryResponse>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MemoryEntryResponse>> GetByTutorIdAndKeysAsync(
        int tutorId,
        IReadOnlyCollection<string> keys,
        CancellationToken cancellationToken = default);
    Task<MemoryEntryResponse?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<ServiceResult<MemoryEntryResponse>> CreateAsync(CreateMemoryEntryRequest request, CancellationToken cancellationToken = default);
    Task<ServiceResult<bool>> UpdateAsync(int id, UpdateMemoryEntryRequest request, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default);
}
