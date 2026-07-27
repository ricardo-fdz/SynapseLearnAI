using System.Text.Json;
using LearningAgents.Domain.LLM;

namespace LearningAgents.Application.Interfaces;

public interface IMemoryToolHandler
{
    Task<LLMFunctionResponse> HandleAsync(
        int tutorId,
        string toolName,
        JsonElement args,
        int? messageId,
        CancellationToken cancellationToken = default);
}
