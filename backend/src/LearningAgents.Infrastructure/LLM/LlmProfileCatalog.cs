using LearningAgents.Application.Dtos.LlmProfiles;
using LearningAgents.Application.Interfaces;
using Microsoft.Extensions.Options;

namespace LearningAgents.Infrastructure.LLM;

public sealed class LlmProfileCatalog(
    IOptions<GeminiOptions> geminiOptions,
    IOptions<LlmProfilesOptions> llmProfilesOptions) : ILlmProfileCatalog
{
    private readonly GeminiOptions gemini = geminiOptions.Value;
    private readonly LlmProfilesOptions profiles = llmProfilesOptions.Value;

    public string DefaultModel =>
        string.IsNullOrWhiteSpace(gemini.DefaultModel) ? "gemini-2.5-flash" : gemini.DefaultModel;

    public string DefaultProfile =>
        string.IsNullOrWhiteSpace(profiles.DefaultProfile) ? "gemini-default" : profiles.DefaultProfile;

    public bool IsKnownProfile(string profileName) =>
        !string.IsNullOrWhiteSpace(profileName) && profiles.Profiles.ContainsKey(profileName.Trim());

    public LlmProfilesCatalogResponse GetCatalog() => new(
        DefaultProfile,
        profiles.Profiles
            .OrderBy(profile => profile.Key)
            .Select(profile => new LlmProfileInfoResponse(
                profile.Key,
                profile.Value.Provider,
                profile.Value.Model,
                profile.Value.FallbackProfiles))
            .ToList());
}
