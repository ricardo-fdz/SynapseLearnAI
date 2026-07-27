using System.Text.Json;
using LearningAgents.Application;
using LearningAgents.Application.Interfaces;
using LearningAgents.Domain.Entities;
using LearningAgents.Domain.Enums;
using LearningAgents.Domain.Memory;
using LearningAgents.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LearningAgents.Tests;

public sealed class MemoryPatchEngineTests
{
    [Fact]
    public async Task SessionMemory_SetRootField_UpdatesValueAndCreatesChange()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var previousValueJson = (await fixture.Db.MemoryEntries.SingleAsync(entry => entry.TutorId == 1 && entry.Key == "memoria_sesion")).ValueJson;
        var result = await fixture.Engine.ApplyPatchAsync(1, Patch("memoria_sesion", MemoryPatchOperation.Set, "/proximo_paso", null, "\"Practicar DI\"", "Set next step from test"), messageId: 1);

        Assert.Contains("Practicar DI", result.MemoryEntry.ValueJson);
        Assert.Equal("Set", result.MemoryChange.Operation);
        Assert.Equal("/proximo_paso", result.MemoryChange.Path);
        Assert.Equal(previousValueJson, result.MemoryChange.PreviousValueJson);
        Assert.False(string.IsNullOrWhiteSpace(result.MemoryChange.PreviousValueJson));
        Assert.NotEqual(result.MemoryChange.PreviousValueJson, result.MemoryChange.NewValueJson);
        Assert.Equal(result.MemoryEntry.ValueJson, result.MemoryChange.NewValueJson);
        Assert.Equal("Set next step from test", result.MemoryChange.Reason);
        Assert.Equal(1, result.MemoryChange.MessageId);
    }

    [Fact]
    public async Task StudentProfile_SetNestedField_UpdatesValue()
    {
        await using var fixture = await TestFixture.CreateAsync();
        await fixture.Engine.ApplyPatchAsync(1, Patch("perfil_estudiante", MemoryPatchOperation.Set, "/estilo_aprendizaje/ritmo_sesion", null, "\"cortas\""), null);

        var entry = await fixture.Db.MemoryEntries.SingleAsync(entry => entry.TutorId == 1 && entry.Key == "perfil_estudiante");
        Assert.Contains("cortas", entry.ValueJson);
    }

    [Fact]
    public async Task StudentProfile_SetLevelDiagnosis_UpdatesValue()
    {
        await using var fixture = await TestFixture.CreateAsync();
        await fixture.Engine.ApplyPatchAsync(1, Patch(
            "perfil_estudiante",
            MemoryPatchOperation.Set,
            "/diagnostico_nivel",
            null,
            "{\"area\":\"Electronica basica\",\"escala\":\"niveles internos 1-5\",\"nivel\":2,\"resumen\":\"Comprende la relacion V/I/R\",\"evidencias\":[\"Explico Ley de Ohm con sus palabras\"],\"brechas\":[\"Unidades metricas\"],\"siguiente_paso\":\"Practicar mediciones con multimetro\"}"), null);

        var entry = await fixture.Db.MemoryEntries.SingleAsync(entry => entry.TutorId == 1 && entry.Key == "perfil_estudiante");
        Assert.Contains("diagnostico_nivel", entry.ValueJson);
        Assert.Contains("Electronica basica", entry.ValueJson);
        Assert.Contains("siguiente_paso", entry.ValueJson);
    }

    [Fact]
    public async Task StudentProfile_AddNote_AppendsNote()
    {
        await using var fixture = await TestFixture.CreateAsync();
        await fixture.Engine.ApplyPatchAsync(1, Patch("perfil_estudiante", MemoryPatchOperation.Add, "/notas_tutor", null, "{\"fecha\":\"2026-06-25\",\"nota\":\"Buena sesion\"}"), null);

        var entry = await fixture.Db.MemoryEntries.SingleAsync(entry => entry.TutorId == 1 && entry.Key == "perfil_estudiante");
        Assert.Contains("notas_tutor", entry.ValueJson);
        Assert.Contains("Buena sesion", entry.ValueJson);
    }

    [Fact]
    public async Task DomainMap_AddTopic_AppendsTema()
    {
        await using var fixture = await TestFixture.CreateAsync();
        await fixture.Engine.ApplyPatchAsync(1, Patch("mapa_dominio", MemoryPatchOperation.Add, "/temas", null, "{\"id\":\"tema-di\",\"nombre\":\"DI\",\"nivel\":1}"), null);

        var entry = await fixture.Db.MemoryEntries.SingleAsync(entry => entry.TutorId == 1 && entry.Key == "mapa_dominio");
        Assert.Contains("tema-di", entry.ValueJson);
    }

    [Fact]
    public async Task DomainMap_AddDuplicateTopicId_UpsertsFields()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var result1 = await fixture.Engine.ApplyPatchAsync(1, Patch("mapa_dominio", MemoryPatchOperation.Add, "/temas", null, "{\"id\":\"tema-di\",\"nombre\":\"DI\",\"nivel\":1,\"notas\":\"primera version\"}", "First add"), null);
        var result2 = await fixture.Engine.ApplyPatchAsync(1, Patch("mapa_dominio", MemoryPatchOperation.Add, "/temas", null, "{\"id\":\"tema-di\",\"nombre\":\"DI avanzado\",\"nivel\":3,\"ultima_evaluacion\":\"2026-07-06\"}", "Second add"), null);

        var entry = await fixture.Db.MemoryEntries.SingleAsync(entry => entry.TutorId == 1 && entry.Key == "mapa_dominio");
        var doc = System.Text.Json.Nodes.JsonNode.Parse(entry.ValueJson)!;
        var temas = doc["temas"]!.AsArray();

        var tema = Assert.Single(temas)!.AsObject();
        Assert.Equal("tema-di", tema["id"]!.GetValue<string>());
        Assert.Equal("DI avanzado", tema["nombre"]!.GetValue<string>());
        Assert.Equal(3, tema["nivel"]!.GetValue<int>());
        Assert.Equal("primera version", tema["notas"]!.GetValue<string>());
        Assert.Equal("2026-07-06", tema["ultima_evaluacion"]!.GetValue<string>());

        Assert.Equal("Add", result2.MemoryChange.Operation);
        Assert.Contains("upsert", result2.MemoryChange.Reason);
    }

    [Fact]
    public async Task DomainMap_AddDuplicateTopicNameWithDifferentId_UpsertsWithoutChangingId()
    {
        await using var fixture = await TestFixture.CreateAsync();
        await fixture.Engine.ApplyPatchAsync(1, Patch("mapa_dominio", MemoryPatchOperation.Add, "/temas", null, "{\"id\":\"tema-ley-ohm-basica\",\"nombre\":\"Ley de Ohm\",\"nivel\":2,\"notas\":\"primera version\"}", "First add"), null);
        var result2 = await fixture.Engine.ApplyPatchAsync(1, Patch("mapa_dominio", MemoryPatchOperation.Add, "/temas", null, "{\"id\":\"tema-ley-ohm-001\",\"nombre\":\"Ley de Ohm\",\"nivel\":3,\"ultima_evaluacion\":\"2026-07-06\"}", "Second add"), null);

        var entry = await fixture.Db.MemoryEntries.SingleAsync(entry => entry.TutorId == 1 && entry.Key == "mapa_dominio");
        var doc = System.Text.Json.Nodes.JsonNode.Parse(entry.ValueJson)!;
        var temas = doc["temas"]!.AsArray();

        var tema = Assert.Single(temas)!.AsObject();
        Assert.Equal("tema-ley-ohm-basica", tema["id"]!.GetValue<string>());
        Assert.Equal("Ley de Ohm", tema["nombre"]!.GetValue<string>());
        Assert.Equal(3, tema["nivel"]!.GetValue<int>());
        Assert.Equal("primera version", tema["notas"]!.GetValue<string>());
        Assert.Equal("2026-07-06", tema["ultima_evaluacion"]!.GetValue<string>());

        Assert.Equal("Add", result2.MemoryChange.Operation);
        Assert.Contains("mismo nombre", result2.MemoryChange.Reason);
    }

    [Fact]
    public async Task Gaps_AddDuplicateActive_NoOp()
    {
        await using var fixture = await TestFixture.CreateAsync();
        await fixture.Engine.ApplyPatchAsync(1, Patch("lagunas_o_errores", MemoryPatchOperation.Add, "/activas", null, "{\"id\":\"gap-1\",\"concepto\":\"Interfaces\",\"descripcion\":\"Confunde direccion\"}", "First add"), null);
        var result2 = await fixture.Engine.ApplyPatchAsync(1, Patch("lagunas_o_errores", MemoryPatchOperation.Add, "/activas", null, "{\"id\":\"gap-1\",\"concepto\":\"Interfaces v2\",\"descripcion\":\"Otra descripcion\"}", "Second add"), null);

        var entry = await fixture.Db.MemoryEntries.SingleAsync(entry => entry.TutorId == 1 && entry.Key == "lagunas_o_errores");
        var doc = System.Text.Json.Nodes.JsonNode.Parse(entry.ValueJson)!;
        var activas = doc["activas"]!.AsArray();

        var gap = Assert.Single(activas)!.AsObject();
        Assert.Equal("gap-1", gap["id"]!.GetValue<string>());
        Assert.Equal("Interfaces", gap["concepto"]!.GetValue<string>());

        Assert.Equal("Add", result2.MemoryChange.Operation);
        Assert.Contains("no-op", result2.MemoryChange.Reason);
    }

    [Fact]
    public async Task ActivityHistory_AddDuplicateProject_NoOp()
    {
        await using var fixture = await TestFixture.CreateAsync();
        await fixture.Engine.ApplyPatchAsync(1, Patch("historial_actividades", MemoryPatchOperation.Add, "/proyectos", null, "{\"id\":\"p-1\",\"nombre\":\"API\",\"resultado\":\"completado\"}", "First add"), null);
        var result2 = await fixture.Engine.ApplyPatchAsync(1, Patch("historial_actividades", MemoryPatchOperation.Add, "/proyectos", null, "{\"id\":\"p-1\",\"nombre\":\"API v2\",\"resultado\":\"en progreso\"}", "Second add"), null);

        var entry = await fixture.Db.MemoryEntries.SingleAsync(entry => entry.TutorId == 1 && entry.Key == "historial_actividades");
        var doc = System.Text.Json.Nodes.JsonNode.Parse(entry.ValueJson)!;
        var proyectos = doc["proyectos"]!.AsArray();

        var project = Assert.Single(proyectos)!.AsObject();
        Assert.Equal("p-1", project["id"]!.GetValue<string>());
        Assert.Equal("API", project["nombre"]!.GetValue<string>());

        Assert.Equal("Add", result2.MemoryChange.Operation);
        Assert.Contains("no-op", result2.MemoryChange.Reason);
    }

    [Fact]
    public async Task DomainMap_UpdateTopic_UpdatesTargetField()
    {
        await using var fixture = await TestFixture.CreateAsync();
        await fixture.Engine.ApplyPatchAsync(1, Patch("mapa_dominio", MemoryPatchOperation.Add, "/temas", null, "{\"id\":\"tema-di\",\"nombre\":\"DI\",\"nivel\":1}"), null);
        var previousValueJson = (await fixture.Db.MemoryEntries.SingleAsync(entry => entry.TutorId == 1 && entry.Key == "mapa_dominio")).ValueJson;
        var result = await fixture.Engine.ApplyPatchAsync(1, Patch("mapa_dominio", MemoryPatchOperation.Update, "/temas/nivel", "tema-di", "3", "Raise DI level from test"), messageId: null);

        var entry = await fixture.Db.MemoryEntries.SingleAsync(entry => entry.TutorId == 1 && entry.Key == "mapa_dominio");
        Assert.Contains("\"nivel\":3", entry.ValueJson);
        Assert.Equal("Update", result.MemoryChange.Operation);
        Assert.Equal("/temas/nivel", result.MemoryChange.Path);
        Assert.Equal("tema-di", result.MemoryChange.TargetId);
        Assert.Equal(previousValueJson, result.MemoryChange.PreviousValueJson);
        Assert.False(string.IsNullOrWhiteSpace(result.MemoryChange.PreviousValueJson));
        Assert.NotEqual(result.MemoryChange.PreviousValueJson, result.MemoryChange.NewValueJson);
        Assert.Equal(entry.ValueJson, result.MemoryChange.NewValueJson);
        Assert.Equal("Raise DI level from test", result.MemoryChange.Reason);
        Assert.Null(result.MemoryChange.MessageId);
    }

    [Fact]
    public async Task Gaps_AddActive_AppendsActiveGap()
    {
        await using var fixture = await TestFixture.CreateAsync();
        await fixture.Engine.ApplyPatchAsync(1, Patch("lagunas_o_errores", MemoryPatchOperation.Add, "/activas", null, "{\"id\":\"gap-1\",\"concepto\":\"Interfaces\",\"descripcion\":\"Confunde direccion\"}"), null);

        var entry = await fixture.Db.MemoryEntries.SingleAsync(entry => entry.TutorId == 1 && entry.Key == "lagunas_o_errores");
        Assert.Contains("gap-1", entry.ValueJson);
    }

    [Fact]
    public async Task Gaps_Resolve_MovesFromActiveToResolved()
    {
        await using var fixture = await TestFixture.CreateAsync();
        await fixture.Engine.ApplyPatchAsync(1, Patch("lagunas_o_errores", MemoryPatchOperation.Add, "/activas", null, "{\"id\":\"gap-1\",\"concepto\":\"Interfaces\",\"descripcion\":\"Confunde direccion\"}"), null);
        var previousValueJson = (await fixture.Db.MemoryEntries.SingleAsync(entry => entry.TutorId == 1 && entry.Key == "lagunas_o_errores")).ValueJson;
        var result = await fixture.Engine.ApplyPatchAsync(1, Patch("lagunas_o_errores", MemoryPatchOperation.Resolve, "/activas", "gap-1", "{\"fecha_resolucion\":\"2026-06-25\",\"como_se_resolvio\":\"Practica guiada\"}", "Resolve active gap from test"), messageId: 1);

        var entry = await fixture.Db.MemoryEntries.SingleAsync(entry => entry.TutorId == 1 && entry.Key == "lagunas_o_errores");
        Assert.Contains("resueltas", entry.ValueJson);
        Assert.Contains("Practica guiada", entry.ValueJson);
        Assert.Contains("\"activas\":[]", entry.ValueJson);
        Assert.Equal("Resolve", result.MemoryChange.Operation);
        Assert.Equal("/activas", result.MemoryChange.Path);
        Assert.Equal("gap-1", result.MemoryChange.TargetId);
        Assert.Equal(previousValueJson, result.MemoryChange.PreviousValueJson);
        Assert.False(string.IsNullOrWhiteSpace(result.MemoryChange.PreviousValueJson));
        Assert.Contains("\"activas\":[{", result.MemoryChange.PreviousValueJson);
        Assert.Contains("gap-1", result.MemoryChange.PreviousValueJson);
        Assert.NotEqual(result.MemoryChange.PreviousValueJson, result.MemoryChange.NewValueJson);
        Assert.Equal(entry.ValueJson, result.MemoryChange.NewValueJson);
        Assert.Contains("\"activas\":[]", result.MemoryChange.NewValueJson);
        Assert.Contains("\"resueltas\":[{", result.MemoryChange.NewValueJson);
        Assert.Contains("Practica guiada", result.MemoryChange.NewValueJson);
        Assert.Equal("Resolve active gap from test", result.MemoryChange.Reason);
        Assert.Equal(1, result.MemoryChange.MessageId);
    }

    [Fact]
    public async Task ActivityHistory_AddProject_AppendsProject()
    {
        await using var fixture = await TestFixture.CreateAsync();
        await fixture.Engine.ApplyPatchAsync(1, Patch("historial_actividades", MemoryPatchOperation.Add, "/proyectos", null, "{\"id\":\"p-1\",\"nombre\":\"API\",\"resultado\":\"completado\"}"), null);

        var entry = await fixture.Db.MemoryEntries.SingleAsync(entry => entry.TutorId == 1 && entry.Key == "historial_actividades");
        Assert.Contains("p-1", entry.ValueJson);
    }

    [Fact]
    public async Task InvalidOperation_FailsClearly()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var exception = await Assert.ThrowsAsync<InvalidMemoryPatchException>(() =>
            fixture.Engine.ApplyPatchAsync(1, Patch("mapa_dominio", MemoryPatchOperation.Resolve, "/temas", "tema-x", "{}"), null));

        Assert.Contains("mapa_dominio only supports", exception.Message);
    }

    [Fact]
    public async Task MissingTargetId_FailsClearly()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var exception = await Assert.ThrowsAsync<InvalidMemoryPatchException>(() =>
            fixture.Engine.ApplyPatchAsync(1, Patch("mapa_dominio", MemoryPatchOperation.Update, "/temas/nivel", "missing", "2"), null));

        Assert.Contains("TargetId 'missing'", exception.Message);
    }

    [Fact]
    public async Task UnknownPath_FailsClearly()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var exception = await Assert.ThrowsAsync<InvalidMemoryPatchException>(() =>
            fixture.Engine.ApplyPatchAsync(1, Patch("memoria_sesion", MemoryPatchOperation.Set, "/campo_inexistente", null, "\"x\""), null));

        Assert.Contains("not a known memoria_sesion", exception.Message);
    }

    [Fact]
    public async Task InvalidValueShape_FailsClearly()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var exception = await Assert.ThrowsAsync<InvalidMemoryPatchException>(() =>
            fixture.Engine.ApplyPatchAsync(1, Patch("mapa_dominio", MemoryPatchOperation.Add, "/temas", null, "{\"nombre\":\"DI\"}"), null));

        Assert.Contains("requires 'id'", exception.Message);
    }

    [Fact]
    public async Task InvalidKey_FailsClearly()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var exception = await Assert.ThrowsAsync<InvalidMemoryPatchException>(() =>
            fixture.Engine.ApplyPatchAsync(1, Patch("clave_invalida", MemoryPatchOperation.Set, "/x", null, "\"x\""), null));

        Assert.Contains("not a standard memory key", exception.Message);
    }

    private static MemoryPatch Patch(string key, MemoryPatchOperation operation, string path, string? targetId, string valueJson) =>
        Patch(key, operation, path, targetId, valueJson, "test reason");

    private static MemoryPatch Patch(string key, MemoryPatchOperation operation, string path, string? targetId, string valueJson, string reason) =>
        new(key, operation, path, targetId, JsonSerializer.Deserialize<JsonElement>(valueJson), reason);

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
        }

        public LearningAgentsDbContext Db { get; }
        public IMemoryPatchEngine Engine { get; }

        public static async Task<TestFixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();

            var services = new ServiceCollection();
            services.AddDbContext<LearningAgentsDbContext>(options => options.UseSqlite(connection));
            services.AddApplicationServices();
            var provider = services.BuildServiceProvider();
            var db = provider.GetRequiredService<LearningAgentsDbContext>();
            await db.Database.EnsureCreatedAsync();
            db.StudySessions.Add(new StudySession
            {
                Id = 1,
                TutorId = 1,
                Name = "Test Session",
                Goal = "Test memory changes",
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
