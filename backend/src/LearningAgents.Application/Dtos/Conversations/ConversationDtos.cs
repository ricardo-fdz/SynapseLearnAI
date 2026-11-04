using LearningAgents.Application.Dtos.Messages;
using LearningAgents.Application.Enums;

namespace LearningAgents.Application.Dtos.Conversations;

public sealed record CreateConversationMessageRequest(
    string? Content = null,
    ContextLoadProfile Profile = ContextLoadProfile.Standard,
    int? RetryUserMessageId = null);

public sealed record AppliedMemoryChangeSummary(
    int MemoryChangeId,
    string Key,
    string Operation,
    string Path,
    string? TargetId,
    string Reason);

public sealed record ConversationMessageResponse(
    MessageResponse UserMessage,
    MessageResponse AssistantMessage,
    IReadOnlyList<AppliedMemoryChangeSummary> AppliedMemoryChanges);

public sealed record ConversationFailureDetails(
    MessageResponse? UserMessage,
    IReadOnlyList<AppliedMemoryChangeSummary> AppliedMemoryChanges);
