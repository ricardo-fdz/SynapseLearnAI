using LearningAgents.Application;
using LearningAgents.Application.Dtos.Tutors;
using LearningAgents.Application.Interfaces;
using LearningAgents.Domain.Memory;
using LearningAgents.Infrastructure.LLM;
using LearningAgents.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace LearningAgents.Tests;

public sealed class TutorServiceTests
{
    [Fact]
    public async Task CreateAsync_ProvisionsStandardMemoryEntries()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var request = new CreateTutorRequest(
            "Writing Tutor",
            "Helps with writing.",
            "Act as a writing tutor.");

        var tutor = await fixture.TutorService.CreateAsync(request);

        var entries = await fixture.Db.MemoryEntries
            .AsNoTracking()
            .Where(entry => entry.TutorId == tutor.Id)
            .OrderBy(entry => entry.Key)
            .ToListAsync();

        Assert.Equal(5, entries.Count);
        Assert.Equal(MemoryKeys.All.OrderBy(key => key), entries.Select(entry => entry.Key));
        Assert.Contains(entries, entry => entry.Key == MemoryKeys.SessionMemory && entry.ValueJson == MemoryEntryDefaults.SessionMemoryJson);
        Assert.Contains(entries, entry => entry.Key == MemoryKeys.StudentProfile && entry.ValueJson == MemoryEntryDefaults.StudentProfileJson);
        Assert.Contains(entries, entry => entry.Key == MemoryKeys.DomainMap && entry.ValueJson == MemoryEntryDefaults.DomainMapJsonTemas);
        Assert.Contains(entries, entry => entry.Key == MemoryKeys.GapsOrErrors && entry.ValueJson == MemoryEntryDefaults.GapsOrErrorsJson);
        Assert.Contains(entries, entry => entry.Key == MemoryKeys.ActivityHistory && entry.ValueJson == MemoryEntryDefaults.ActivityHistoryJson);
        Assert.All(entries, entry => Assert.Equal(1, entry.SchemaVersion));
        Assert.Equal("gemini-test-default", tutor.GeminiModel);
    }

    [Fact]
    public async Task CreateAsync_WithInitialStudentProfile_StoresProfileInInitialMemoryEntry()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var request = new CreateTutorRequest(
            "Networking Tutor",
            "Helps with networking.",
            "Act as a networking tutor.",
            InitialStudentProfile: new InitialStudentProfileRequest(
                Alias: "Ricardo",
                LenguajePrincipal: "Redes",
                ObjetivoDeclarado: "Complementar habilidades utiles para el desarrollo",
                EstiloAprendizaje: new InitialLearningStyleRequest(
                    Prefiere: "analogias",
                    RitmoSesion: "largas_progresivas",
                    ReaccionAnteErrores: "se_frustra_rapido",
                    NivelAutonomia: "necesita_mucha_guia"),
                PreferenciasComunicacion: new InitialCommunicationPreferencesRequest(
                    Idioma: "espanol",
                    TonoTutor: "estricto_directo")));

        var tutor = await fixture.TutorService.CreateAsync(request);

        var profile = await fixture.Db.MemoryEntries
            .AsNoTracking()
            .SingleAsync(entry => entry.TutorId == tutor.Id && entry.Key == MemoryKeys.StudentProfile);

        Assert.Contains("\"alias\":\"Ricardo\"", profile.ValueJson);
        Assert.Contains("\"lenguaje_principal\":\"Redes\"", profile.ValueJson);
        Assert.Contains("\"objetivo_declarado\":\"Complementar habilidades utiles para el desarrollo\"", profile.ValueJson);
        Assert.Contains("\"estilo_aprendizaje\"", profile.ValueJson);
        Assert.Contains("\"ritmo_sesion\":\"largas_progresivas\"", profile.ValueJson);
        Assert.Contains("\"nivel_autonomia\":\"necesita_mucha_guia\"", profile.ValueJson);
        Assert.Contains("\"prefiere\":\"analogias\"", profile.ValueJson);
        Assert.Contains("\"reaccion_ante_errores\":\"se_frustra_rapido\"", profile.ValueJson);
        Assert.Contains("\"preferencias_comunicacion\"", profile.ValueJson);
        Assert.Contains("\"idioma\":\"espanol\"", profile.ValueJson);
        Assert.Contains("\"tono_tutor\":\"estricto_directo\"", profile.ValueJson);
    }

    private sealed class TestFixture : IAsyncDisposable
    {
        private readonly SqliteConnection connection;
        private readonly ServiceProvider serviceProvider;

        private TestFixture(SqliteConnection connection, ServiceProvider serviceProvider)
        {
            this.connection = connection;
            this.serviceProvider = serviceProvider;
            Db = serviceProvider.GetRequiredService<LearningAgentsDbContext>();
            TutorService = serviceProvider.GetRequiredService<ITutorService>();
        }

        public LearningAgentsDbContext Db { get; }
        public ITutorService TutorService { get; }

        public static async Task<TestFixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();

            var services = new ServiceCollection();
            services.AddDbContext<LearningAgentsDbContext>(options => options.UseSqlite(connection));
            services.AddSingleton(Options.Create(new GeminiOptions { DefaultModel = "gemini-test-default" }));
            services.AddSingleton(Options.Create(new LlmProfilesOptions { DefaultProfile = "gemini-test-default-profile" }));
            services.AddApplicationServices();
            var provider = services.BuildServiceProvider();
            var db = provider.GetRequiredService<LearningAgentsDbContext>();
            await db.Database.EnsureCreatedAsync();

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
