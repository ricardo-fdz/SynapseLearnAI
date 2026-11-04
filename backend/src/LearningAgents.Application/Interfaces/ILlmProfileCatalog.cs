using LearningAgents.Application.Dtos.LlmProfiles;

namespace LearningAgents.Application.Interfaces;

public interface ILlmProfileCatalog
{
    string DefaultModel { get; }
    string DefaultProfile { get; }
    bool IsKnownProfile(string profileName);
    LlmProfilesCatalogResponse GetCatalog();
}
