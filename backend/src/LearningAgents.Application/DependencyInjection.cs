using LearningAgents.Application.Interfaces;
using LearningAgents.Application.Services;
using Microsoft.Extensions.DependencyInjection;

namespace LearningAgents.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddScoped<ITutorService, TutorService>();
        services.AddScoped<IStudySessionService, StudySessionService>();
        services.AddScoped<IMessageService, MessageService>();
        services.AddScoped<IMemoryEntryService, MemoryEntryService>();
        services.AddScoped<IMemoryChangeService, MemoryChangeService>();
        services.AddScoped<IPromptBuilder, PromptBuilder>();
        services.AddScoped<IConversationService, ConversationService>();
        services.AddScoped<IMemoryPatchEngine, MemoryPatchEngine>();
        services.AddScoped<IMemoryToolHandler, MemoryToolHandler>();

        return services;
    }
}
