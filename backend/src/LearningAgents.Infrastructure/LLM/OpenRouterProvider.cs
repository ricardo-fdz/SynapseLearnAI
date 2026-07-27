using Microsoft.Extensions.Options;

namespace LearningAgents.Infrastructure.LLM;

public sealed class OpenRouterProvider(HttpClient httpClient, IOptions<OpenRouterOptions> options)
    : OpenAICompatibleProvider(httpClient)
{
    private readonly OpenRouterOptions options = options.Value;

    protected override string ProviderName => "OpenRouter";

    protected override string Endpoint => "https://openrouter.ai/api/v1/chat/completions";

    protected override string ApiKey => options.ApiKey;

    protected override void AddProviderHeaders(HttpRequestMessage request)
    {
        if (!string.IsNullOrWhiteSpace(options.HttpReferer))
        {
            request.Headers.TryAddWithoutValidation("HTTP-Referer", options.HttpReferer);
        }

        if (!string.IsNullOrWhiteSpace(options.Title))
        {
            request.Headers.TryAddWithoutValidation("X-Title", options.Title);
        }
    }
}
