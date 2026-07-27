namespace LearningAgents.Domain.LLM;

public sealed record PromptRequest(
    string Model,
    string SystemPrompt,
    IReadOnlyList<LLMMessage> Messages,
    IReadOnlyList<LLMToolDeclaration>? Tools = null);
