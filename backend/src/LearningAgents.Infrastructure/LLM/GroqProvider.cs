using Microsoft.Extensions.Options;

namespace LearningAgents.Infrastructure.LLM;

public sealed class GroqProvider(HttpClient httpClient, IOptions<GroqOptions> options)
    : OpenAICompatibleProvider(httpClient)
{
    private readonly GroqOptions options = options.Value;

    protected override string ProviderName => "Groq";

    protected override string Endpoint => "https://api.groq.com/openai/v1/chat/completions";

    protected override string ApiKey => options.ApiKey;
}
