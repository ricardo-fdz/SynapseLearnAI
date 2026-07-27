using LearningAgents.Application.Common;
using LearningAgents.Application.Dtos.Conversations;

namespace LearningAgents.Application.Interfaces;

public interface IConversationService
{
    Task<ServiceResult<ConversationMessageResponse>> SendMessageAsync(
        int sessionId,
        CreateConversationMessageRequest request,
        CancellationToken cancellationToken = default);
}
