using System.Text.Json;
using LearningAgents.Application;
using LearningAgents.Application.Enums;
using LearningAgents.Application.Interfaces;
using LearningAgents.Domain.Entities;
using LearningAgents.Domain.Enums;
using LearningAgents.Domain.Memory;
using LearningAgents.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using LearningAgents.Infrastructure.LLM;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace LearningAgents.Tests;

public sealed class PromptBuilderTests
{
    [Fact]
    public async Task RenderStudentProfile_Empty_ShowsSinPerfil()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var prompt = await fixture.Builder.BuildSystemPromptAsync(1, ContextLoadProfile.FullReview);

        Assert.Contains($"Fecha actual (UTC): {DateTime.UtcNow:yyyy-MM-dd}", prompt);
        Assert.Contains("no inventes fechas pasadas o futuras", prompt);
        Assert.Contains("## Memoria: perfil_estudiante", prompt);
        Assert.Contains("Sin perfil registrado.", prompt);
    }

    [Fact]
    public async Task RenderStudentProfile_AliasOnly_ShowsAlias()
    {
        await using var fixture = await TestFixture.CreateAsync();
        await fixture.Engine.ApplyPatchAsync(1,
            Patch("perfil_estudiante", MemoryPatchOperation.Set, "/alias", "\"Juan\""),
            null);
        var prompt = await fixture.Builder.BuildSystemPromptAsync(1, ContextLoadProfile.FullReview);

        Assert.Contains("## Memoria: perfil_estudiante", prompt);
        Assert.Contains("- **Alias:** Juan", prompt);
        Assert.DoesNotContain("Sin perfil registrado.", prompt);
    }

    [Fact]
    public async Task RenderStudentProfile_DiagnosticoNivel_ShowsDiagnosis()
    {
        await using var fixture = await TestFixture.CreateAsync();
        await fixture.Engine.ApplyPatchAsync(1,
            Patch("perfil_estudiante", MemoryPatchOperation.Set, "/diagnostico_nivel",
                """{"area":"Programacion","escala":"1-5","nivel":2,"resumen":"Conceptos basicos claros","evidencias":["Explico variables"],"brechas":["Bucles"],"siguiente_paso":"Practicar loops"}"""),
            null);
        var prompt = await fixture.Builder.BuildSystemPromptAsync(1, ContextLoadProfile.FullReview);

        Assert.Contains("## Memoria: perfil_estudiante", prompt);
        Assert.Contains("- **Diagnostico de nivel:", prompt);
        Assert.DoesNotContain("Sin perfil registrado.", prompt);
    }

    [Fact]
    public async Task RenderStudentProfile_AllFields_RendersAll()
    {
        await using var fixture = await TestFixture.CreateAsync();

        await fixture.Engine.ApplyPatchAsync(1,
            Patch("perfil_estudiante", MemoryPatchOperation.Set, "/alias", "\"Carlos\""),
            null);
        await fixture.Engine.ApplyPatchAsync(1,
            Patch("perfil_estudiante", MemoryPatchOperation.Set, "/objetivo_declarado", "\"Aprender C#\""),
            null);
        await fixture.Engine.ApplyPatchAsync(1,
            Patch("perfil_estudiante", MemoryPatchOperation.Set, "/lenguaje_principal", "\"JavaScript\""),
            null);
        await fixture.Engine.ApplyPatchAsync(1,
            Patch("perfil_estudiante", MemoryPatchOperation.Add, "/notas_tutor",
                """{"fecha":"2026-07-06","nota":"Buena actitud"}"""),
            null);

        var prompt = await fixture.Builder.BuildSystemPromptAsync(1, ContextLoadProfile.FullReview);

        Assert.Contains("- **Alias:** Carlos", prompt);
        Assert.Contains("- **Objetivo declarado:** Aprender C#", prompt);
        Assert.Contains("- **Lenguaje principal:** JavaScript", prompt);
        Assert.Contains("- **Notas del tutor:", prompt);
        Assert.DoesNotContain("Sin perfil registrado.", prompt);
    }

    [Fact]
    public async Task RenderSessionMemory_ProximoPaso_ShowsSavedState()
    {
        await using var fixture = await TestFixture.CreateAsync();
        await fixture.Engine.ApplyPatchAsync(1,
            Patch("memoria_sesion", MemoryPatchOperation.Set, "/proximo_paso", "\"Practicar multimetro\""),
            null);
        await fixture.Engine.ApplyPatchAsync(1,
            Patch("memoria_sesion", MemoryPatchOperation.Set, "/fecha_ultima_sesion", $"\"{DateTime.UtcNow:yyyy-MM-dd}\""),
            null);

        var prompt = await fixture.Builder.BuildSystemPromptAsync(1, ContextLoadProfile.FullReview);

        Assert.Contains("## Memoria: memoria_sesion", prompt);
        Assert.Contains("- **Proximo paso:** Practicar multimetro", prompt);
        Assert.Contains($"- **Fecha ultima sesion:** {DateTime.UtcNow:yyyy-MM-dd}", prompt);
        Assert.DoesNotContain("Sin estado de sesion registrado.", prompt);
    }

    [Fact]
    public async Task BuildSystemPrompt_WithGoal_InjectsMetaDeclarada()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var promptWithout = await fixture.Builder.BuildSystemPromptAsync(1, ContextLoadProfile.Standard, (string?)null);
        var promptWith = await fixture.Builder.BuildSystemPromptAsync(1, ContextLoadProfile.Standard, "Repaso de closures para backend");

        Assert.DoesNotContain("Meta declarada", promptWithout);
        Assert.Contains("Meta declarada de esta sesión: \"Repaso de closures para backend\"", promptWith);
        Assert.Contains("Es una guía, no un límite", promptWith);
    }

    private static MemoryPatch Patch(string key, MemoryPatchOperation operation, string path, string valueJson) =>
        new(key, operation, path, null, JsonSerializer.Deserialize<JsonElement>(valueJson), "test reason");

    private sealed class TestFixture : IAsyncDisposable
    {
        private readonly SqliteConnection connection;
        private readonly ServiceProvider serviceProvider;

        private TestFixture(SqliteConnection connection, ServiceProvider serviceProvider)
        {
            this.connection = connection;
            this.serviceProvider = serviceProvider;
            Db = serviceProvider.GetRequiredService<LearningAgentsDbContext>();
            Engine = serviceProvider.GetRequiredService<IMemoryPatchEngine>();
            Builder = serviceProvider.GetRequiredService<IPromptBuilder>();
        }

        public LearningAgentsDbContext Db { get; }
        public IMemoryPatchEngine Engine { get; }
        public IPromptBuilder Builder { get; }

        public static async Task<TestFixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();

            var services = new ServiceCollection();
            services.AddDbContext<LearningAgentsDbContext>(options => options.UseSqlite(connection));
            services.AddApplicationServices();
            services.AddSingleton<IOptions<GeminiOptions>>(_ => Options.Create(new GeminiOptions()));
            services.AddSingleton<IOptions<LlmProfilesOptions>>(_ => Options.Create(new LlmProfilesOptions()));
            var provider = services.BuildServiceProvider();
            var db = provider.GetRequiredService<LearningAgentsDbContext>();
            await db.Database.EnsureCreatedAsync();
            db.StudySessions.Add(new StudySession
            {
                Id = 1,
                TutorId = 1,
                Name = "Test Session",
                Goal = "Test prompt builder",
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            });
            db.Messages.Add(new Message
            {
                Id = 1,
                SessionId = 1,
                Role = MessageRole.User,
                Content = "Test message",
                CreatedAtUtc = DateTime.UtcNow
            });
            await db.SaveChangesAsync();

            return new TestFixture(connection, provider);
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await serviceProvider.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}
