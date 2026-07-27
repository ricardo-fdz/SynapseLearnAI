namespace LearningAgents.Domain.LLM;

public interface ILLMProvider
{
    Task<LLMResponse> GenerateAsync(PromptRequest request, CancellationToken cancellationToken);
}
