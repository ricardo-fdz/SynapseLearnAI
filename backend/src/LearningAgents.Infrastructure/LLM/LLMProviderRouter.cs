using System.Runtime.CompilerServices;
using LearningAgents.Domain.LLM;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LearningAgents.Infrastructure.LLM;

public sealed class LLMProviderRouter(
    GeminiProvider geminiProvider,
    GroqProvider groqProvider,
    OpenRouterProvider openRouterProvider,
    IOptions<LlmProfilesOptions> options,
    ILogger<LLMProviderRouter>? logger = null) : ILLMProviderRouter
{
    private readonly LlmProfilesOptions options = options.Value;

    public async Task<LLMResponse> GenerateAsync(string profileName, PromptRequest request, CancellationToken cancellationToken)
    {
        var attemptedProfiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var profilesToTry = ResolveProfileChain(profileName).ToList();
        if (profilesToTry.Count == 0)
        {
            profilesToTry.Add(new ResolvedProfile("gemini-legacy", "gemini", request.Model, []));
        }

        LLMResponse? lastResponse = null;
        foreach (var profile in profilesToTry)
        {
            if (!attemptedProfiles.Add(profile.Name))
            {
                continue;
            }

            var provider = GetProvider(profile.Provider);
            if (provider is null)
            {
                lastResponse = LLMResponse.Failure($"LLM provider '{profile.Provider}' is not supported.");
                continue;
            }

            var model = string.IsNullOrWhiteSpace(profile.Model) ? request.Model : profile.Model;
            var response = await provider.GenerateAsync(request with { Model = model }, cancellationToken);
            if (response.IsSuccess || !ShouldTryFallback(response.StatusCode, response.ErrorMessage))
            {
                return response;
            }

            logger?.LogWarning(
                "LLM profile {ProfileName} failed transiently: {ErrorMessage}. Trying fallback profile if available.",
                profile.Name,
                response.ErrorMessage);
            lastResponse = response;
        }

        return lastResponse ?? LLMResponse.Failure("No LLM profile could be resolved.");
    }

    public async IAsyncEnumerable<LLMStreamChunk> StreamGenerateAsync(
        string profileName,
        PromptRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var attemptedProfiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var profilesToTry = ResolveProfileChain(profileName).ToList();
        if (profilesToTry.Count == 0)
        {
            profilesToTry.Add(new ResolvedProfile("gemini-legacy", "gemini", request.Model, []));
        }

        LLMStreamChunk? lastFailure = null;
        foreach (var profile in profilesToTry)
        {
            if (!attemptedProfiles.Add(profile.Name))
            {
                continue;
            }

            var provider = GetProvider(profile.Provider);
            if (provider is null)
            {
                lastFailure = LLMStreamChunk.Failure($"LLM provider '{profile.Provider}' is not supported.");
                continue;
            }

            var model = string.IsNullOrWhiteSpace(profile.Model) ? request.Model : profile.Model;
            var emittedSubstantiveChunk = false;
            var tryNextProfile = false;

            await foreach (var chunk in provider.StreamGenerateAsync(request with { Model = model }, cancellationToken))
            {
                if (chunk.IsError)
                {
                    if (!emittedSubstantiveChunk && ShouldTryFallback(chunk.StatusCode, chunk.ErrorMessage))
                    {
                        logger?.LogWarning(
                            "LLM stream profile {ProfileName} failed transiently before emitting content: {ErrorMessage}. Trying fallback profile if available.",
                            profile.Name,
                            chunk.ErrorMessage);
                        lastFailure = chunk;
                        tryNextProfile = true;
                        break;
                    }

                    yield return chunk;
                    yield break;
                }

                if (!string.IsNullOrEmpty(chunk.TextDelta) || chunk.FunctionCalls is { Count: > 0 })
                {
                    emittedSubstantiveChunk = true;
                }

                yield return chunk;
            }

            if (!tryNextProfile)
            {
                yield break;
            }
        }

        yield return lastFailure ?? LLMStreamChunk.Failure("No LLM profile could be resolved.");
    }

    private IEnumerable<ResolvedProfile> ResolveProfileChain(string profileName)
    {
        var rootProfileName = string.IsNullOrWhiteSpace(profileName) ? options.DefaultProfile : profileName;
        if (!TryResolveProfile(rootProfileName, out var rootProfile))
        {
            if (!TryResolveProfile(options.DefaultProfile, out rootProfile))
            {
                yield break;
            }
        }

        yield return rootProfile;
        foreach (var fallbackName in rootProfile.FallbackProfiles)
        {
            if (TryResolveProfile(fallbackName, out var fallbackProfile))
            {
                yield return fallbackProfile;
            }
        }
    }

    private bool TryResolveProfile(string profileName, out ResolvedProfile profile)
    {
        profile = default!;
        if (options.Profiles.TryGetValue(profileName, out var configuredProfile))
        {
            profile = new ResolvedProfile(
                profileName,
                configuredProfile.Provider,
                configuredProfile.Model,
                configuredProfile.FallbackProfiles);
            return true;
        }

        return false;
    }

    private ILLMProvider? GetProvider(string providerName) => providerName.ToLowerInvariant() switch
    {
        "gemini" => geminiProvider,
        "groq" => groqProvider,
        "openrouter" => openRouterProvider,
        _ => null
    };

    private static bool ShouldTryFallback(int? statusCode, string? errorMessage) =>
        statusCode is 402 or 404 or 408 or 429 or 503
        || (errorMessage?.Contains("timed out", StringComparison.OrdinalIgnoreCase) ?? false)
        || (errorMessage?.Contains("did not contain generated text", StringComparison.OrdinalIgnoreCase) ?? false)
        || (errorMessage?.Contains("request failed after", StringComparison.OrdinalIgnoreCase) ?? false);

    private sealed record ResolvedProfile(
        string Name,
        string Provider,
        string Model,
        IReadOnlyList<string> FallbackProfiles);
}
