namespace LearningAgents.Domain.LLM;

public interface ILLMProviderRouter
{
    Task<LLMResponse> GenerateAsync(string profileName, PromptRequest request, CancellationToken cancellationToken);
}
