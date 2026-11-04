using LearningAgents.Application.Interfaces;
using LearningAgents.Infrastructure.LLM;
using LearningAgents.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace LearningAgents.Tests;

internal static class TestServiceRegistrationExtensions
{
    public static IServiceCollection AddTestInfrastructureBindings(this IServiceCollection services)
    {
        services.TryAddScoped<ILearningAgentsDbContext>(sp => sp.GetRequiredService<LearningAgentsDbContext>());
        services.TryAddSingleton<IOptions<GeminiOptions>>(_ => Options.Create(new GeminiOptions()));
        services.TryAddSingleton<IOptions<LlmProfilesOptions>>(_ => Options.Create(new LlmProfilesOptions()));
        services.TryAddSingleton<ILlmProfileCatalog, LlmProfileCatalog>();
        return services;
    }
}
