using LearningAgents.Application.Dtos.Messages;

namespace LearningAgents.Application.Dtos.Conversations;

public sealed record ConversationStreamEvent(
    string Type,
    object Payload);

public sealed record StreamToolActivityPayload(
    int Iteration,
    string ToolName,
    string? Key = null,
    string? Operation = null,
    bool? Success = null);

public sealed record StreamTokenDeltaPayload(string Delta);

public sealed record StreamErrorPayload(
    int StatusCode,
    string Message,
    MessageResponse? UserMessage = null,
    IReadOnlyList<AppliedMemoryChangeSummary>? AppliedMemoryChanges = null);
