using LearningAgents.Application.Common;
using LearningAgents.Application.Dtos.Messages;

namespace LearningAgents.Application.Interfaces;

public interface IMessageService
{
    Task<IReadOnlyList<MessageResponse>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<MessageResponse?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<PagedResult<MessageResponse>?> GetBySessionPagedAsync(
        int sessionId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);
    Task<ServiceResult<MessageResponse>> CreateAsync(CreateMessageRequest request, CancellationToken cancellationToken = default);
}
