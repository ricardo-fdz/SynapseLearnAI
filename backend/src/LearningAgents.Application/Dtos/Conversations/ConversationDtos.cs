using System.ComponentModel.DataAnnotations;
using LearningAgents.Application.Dtos.Messages;
using LearningAgents.Application.Enums;

namespace LearningAgents.Application.Dtos.Conversations;

public sealed record CreateConversationMessageRequest(
    [param: Required] string Content,
    ContextLoadProfile Profile = ContextLoadProfile.Standard);

public sealed record ConversationMessageResponse(MessageResponse AssistantMessage);
