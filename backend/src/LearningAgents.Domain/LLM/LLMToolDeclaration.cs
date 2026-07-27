using System.Text.Json;

namespace LearningAgents.Domain.LLM;

public sealed record LLMToolDeclaration(
    string Name,
    string Description,
    JsonElement Parameters);
