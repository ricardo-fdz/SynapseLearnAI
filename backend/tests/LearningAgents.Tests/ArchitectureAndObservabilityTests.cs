using System.Net;
using System.Text;
using System.Text.Json;
using LearningAgents.Api.Controllers;
using LearningAgents.Application;
using LearningAgents.Application.Dtos.Tutors;
using LearningAgents.Application.Enums;
using LearningAgents.Application.Interfaces;
using LearningAgents.Domain.Entities;
using LearningAgents.Domain.Enums;
using LearningAgents.Domain.LLM;
using LearningAgents.Domain.Memory;
using LearningAgents.Infrastructure.LLM;
using LearningAgents.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace LearningAgents.Tests;

public sealed class ArchitectureAndObservabilityTests
{
    [Fact]
    public void ApplicationAssembly_DoesNotReferenceInfrastructure_AndControllersDoNotInjectInfrastructureTypes()
    {
        var applicationAssembly = typeof(ITutorService).Assembly;
        var referencedNames = applicationAssembly.GetReferencedAssemblies().Select(a => a.Name).ToArray();
        Assert.DoesNotContain(referencedNames, name => string.Equals(name, "LearningAgents.Infrastructure", StringComparison.OrdinalIgnoreCase));

        var apiAssembly = typeof(TutorsController).Assembly;
        var controllers = apiAssembly.GetTypes().Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract);
        foreach (var controller in controllers)
        {
            foreach (var ctor in controller.GetConstructors())
            {
                foreach (var param in ctor.GetParameters())
                {
                    Assert.DoesNotContain(
                        "LearningAgents.Infrastructure",
                        param.ParameterType.FullName ?? string.Empty,
                        StringComparison.Ordinal);
                }
            }
        }
    }

    [Fact]
    public async Task GeminiAndGroqProviders_ParseTokenUsageMetadata()
    {
        var geminiHandler = new StubHttpMessageHandler("""
        {
          "candidates": [{ "content": { "parts": [{ "text": "Hola mundo" }] } }],
          "usageMetadata": { "promptTokenCount": 120, "candidatesTokenCount": 35, "totalTokenCount": 155 }
        }
        """);
        var gemini = new GeminiProvider(
            new HttpClient(geminiHandler),
            Options.Create(new GeminiOptions { ApiKey = "test-key", DefaultModel = "gemini-2.5-flash" }));

        var geminiRes = await gemini.GenerateAsync(
            new PromptRequest("gemini-2.5-flash", "sys", [new LLMMessage("user", "hi")]),
            CancellationToken.None);

        Assert.True(geminiRes.IsSuccess);
        Assert.NotNull(geminiRes.Usage);
        Assert.Equal(120, geminiRes.Usage!.PromptTokens);
        Assert.Equal(35, geminiRes.Usage.CompletionTokens);
        Assert.Equal(155, geminiRes.Usage.TotalTokens);

        var groqHandler = new StubHttpMessageHandler("""
        {
          "choices": [{ "message": { "role": "assistant", "content": "Respuesta Groq" } }],
          "usage": { "prompt_tokens": 80, "completion_tokens": 22, "total_tokens": 102 }
        }
        """);
        var groq = new GroqProvider(
            new HttpClient(groqHandler),
            Options.Create(new GroqOptions { ApiKey = "groq-key" }));

        var groqRes = await groq.GenerateAsync(
            new PromptRequest("qwen-qwq-32b", "sys", [new LLMMessage("user", "hi")]),
            CancellationToken.None);

        Assert.True(groqRes.IsSuccess);
        Assert.NotNull(groqRes.Usage);
        Assert.Equal(80, groqRes.Usage!.PromptTokens);
        Assert.Equal(22, groqRes.Usage.CompletionTokens);
        Assert.Equal(102, groqRes.Usage.TotalTokens);
    }

    [Fact]
    public async Task PromptBuilder_CapsDomainMapAt40Items_AndInvalidatesTutorCacheOnUpdate()
    {
        await using var conn = new SqliteConnection("Data Source=:memory:");
        await conn.OpenAsync();
        var services = new ServiceCollection();
        services.AddDbContext<LearningAgentsDbContext>(o => o.UseSqlite(conn));
        services.AddApplicationServices();
        services.AddTestInfrastructureBindings();
        await using var sp = services.BuildServiceProvider();
        var db = sp.GetRequiredService<LearningAgentsDbContext>();
        await db.Database.EnsureCreatedAsync();

        var topics = Enumerable.Range(1, 50)
            .Select(i => new { id = $"t-{i}", nombre = $"Tema {i}", nivel = 3 })
            .ToArray();
        var domainEntry = await db.MemoryEntries.SingleAsync(e => e.TutorId == 1 && e.Key == MemoryKeys.DomainMap);
        domainEntry.ValueJson = JsonSerializer.Serialize(new { temas = topics });
        await db.SaveChangesAsync();

        var builder = sp.GetRequiredService<IPromptBuilder>();
        var tutorService = sp.GetRequiredService<ITutorService>();

        var prompt1 = await builder.BuildSystemPromptAsync(1, ContextLoadProfile.Standard);
        Assert.Contains("Mostrando 40 de 50 temas", prompt1);
        Assert.Contains("Tema 50", prompt1);
        Assert.DoesNotContain("- **Tema 1**:", prompt1);

        await tutorService.UpdateAsync(1, new UpdateTutorRequest("Programming Tutor", "Desc", "NUEVO SYSTEM PROMPT DINAMICO"));
        var prompt2 = await builder.BuildSystemPromptAsync(1, ContextLoadProfile.Minimal);
        Assert.Contains("NUEVO SYSTEM PROMPT DINAMICO", prompt2);
    }

    [Fact]
    public async Task GlobalListEndpoints_EnforceLimitParameter()
    {
        await using var conn = new SqliteConnection("Data Source=:memory:");
        await conn.OpenAsync();
        var services = new ServiceCollection();
        services.AddDbContext<LearningAgentsDbContext>(o => o.UseSqlite(conn));
        services.AddApplicationServices();
        services.AddTestInfrastructureBindings();
        await using var sp = services.BuildServiceProvider();
        var db = sp.GetRequiredService<LearningAgentsDbContext>();
        await db.Database.EnsureCreatedAsync();

        var now = DateTime.UtcNow;
        db.StudySessions.Add(new StudySession { Id = 1, TutorId = 1, Name = "S1", Goal = "G1", CreatedAtUtc = now, UpdatedAtUtc = now });
        for (var i = 1; i <= 6; i++)
        {
            db.Messages.Add(new Message { Id = i, SessionId = 1, Role = MessageRole.User, Content = $"m{i}", CreatedAtUtc = now.AddSeconds(i) });
        }
        await db.SaveChangesAsync();

        var messageService = sp.GetRequiredService<IMessageService>();
        var limited = await messageService.GetAllAsync(limit: 3);
        Assert.Equal(3, limited.Count);
        Assert.Equal([6, 5, 4], limited.Select(m => m.Id).ToArray());
    }

    private sealed class StubHttpMessageHandler(string jsonResponse) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(jsonResponse, Encoding.UTF8, "application/json")
            });
    }
}
