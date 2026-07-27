using System.Text.Json;

namespace LearningAgents.Domain.LLM;

public sealed record LLMFunctionCall(
    string Name,
    JsonElement Args,
    string? Id = null,
    string? ThoughtSignature = null,
    string? RawPartJson = null);
