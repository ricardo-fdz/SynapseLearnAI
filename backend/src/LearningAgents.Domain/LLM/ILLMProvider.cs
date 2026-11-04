using System.Runtime.CompilerServices;

namespace LearningAgents.Domain.LLM;

public interface ILLMProvider
{
    Task<LLMResponse> GenerateAsync(PromptRequest request, CancellationToken cancellationToken);

    async IAsyncEnumerable<LLMStreamChunk> StreamGenerateAsync(
        PromptRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var response = await GenerateAsync(request, cancellationToken);
        if (!response.IsSuccess)
        {
            yield return LLMStreamChunk.Failure(response.ErrorMessage ?? "LLM request failed.", response.StatusCode);
            yield break;
        }

        if (response.FunctionCalls is { Count: > 0 } calls)
        {
            yield return LLMStreamChunk.ToolCalls(calls, response.Usage);
            yield break;
        }

        if (!string.IsNullOrEmpty(response.Content))
        {
            yield return LLMStreamChunk.Delta(response.Content);
        }

        if (response.Usage is not null)
        {
            yield return LLMStreamChunk.Completed(response.Usage);
        }
    }
}
