namespace LearningAgents.Application.Dtos.LlmProfiles;

public sealed record LlmProfileInfoResponse(
    string Name,
    string Provider,
    string Model,
    IReadOnlyList<string> FallbackProfiles);

public sealed record LlmProfilesCatalogResponse(
    string DefaultProfile,
    IReadOnlyList<LlmProfileInfoResponse> Profiles);
