using LearningAgents.Application.Interfaces;
using LearningAgents.Domain.LLM;
using LearningAgents.Infrastructure.LLM;
using LearningAgents.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LearningAgents.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructureServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<GeminiOptions>(configuration.GetSection(GeminiOptions.SectionName));
        services.Configure<GroqOptions>(configuration.GetSection(GroqOptions.SectionName));
        services.Configure<OpenRouterOptions>(configuration.GetSection(OpenRouterOptions.SectionName));
        services.Configure<LlmProfilesOptions>(configuration.GetSection(LlmProfilesOptions.SectionName));
        services.AddScoped<ILearningAgentsDbContext>(sp => sp.GetRequiredService<LearningAgentsDbContext>());
        services.AddSingleton<ILlmProfileCatalog, LlmProfileCatalog>();
        services.AddHttpClient<GeminiProvider>();
        services.AddHttpClient<GroqProvider>();
        services.AddHttpClient<OpenRouterProvider>();
        services.AddHttpClient<ITtsService, Services.GeminiTtsService>();
        services.AddScoped<ILLMProviderRouter, LLMProviderRouter>();

        return services;
    }
}
