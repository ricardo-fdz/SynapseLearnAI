using System.Text.Json;

namespace LearningAgents.Domain.LLM;

public sealed record LLMFunctionResponse(string Name, JsonElement Response, string? Id = null);
