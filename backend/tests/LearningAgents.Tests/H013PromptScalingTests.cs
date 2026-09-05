using System.Text.Json;
using LearningAgents.Application.Enums;
using LearningAgents.Application.Interfaces;
using LearningAgents.Domain.Entities;
using LearningAgents.Domain.Enums;
using LearningAgents.Domain.LLM;
using LearningAgents.Domain.Memory;
using LearningAgents.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using LearningAgents.Application;
using LearningAgents.Infrastructure.LLM;
using Xunit;

namespace LearningAgents.Tests;

public sealed class H013PromptScalingTests
{
    [Fact]
    public async Task ActivityHistory_TruncatesToLast5()
    {
        await using var fixture = await Fixture.CreateAsync();
        var many = Enumerable.Range(1, 10).Select(i => new { titulo = $"Act {i}", tipo = "proyecto", notas = new string('x', 600) });
        var json = JsonSerializer.Serialize(new { proyectos = many });
        var entry = await fixture.Db.MemoryEntries.FirstAsync(e => e.TutorId == 1 && e.Key == MemoryKeys.ActivityHistory);
        entry.ValueJson = json;
        await fixture.Db.SaveChangesAsync();
        var prompt = await fixture.Builder.BuildSystemPromptAsync(1, ContextLoadProfile.Project);

        // Should show truncation notice and only last 5
        Assert.Contains("Mostrando 5 de 10", prompt);
        Assert.Contains("Act 10", prompt);
        Assert.Contains("Act 6", prompt);
        Assert.DoesNotContain("Act 1\n", prompt); // naive check: Act 1 should be truncated (but "Act 10" contains "Act 1")
        // More precise: ensure Act 5 not present as standalone entry
        Assert.DoesNotContain("- **Act 5**", prompt);
        // Long field truncated with …
        Assert.Contains("…", prompt);
    }

    [Fact]
    public async Task ActivityHistory_SupportsProyectosKey()
    {
        await using var fixture = await Fixture.CreateAsync();
        var json = JsonSerializer.Serialize(new { proyectos = new[] { new { titulo = "P1", tipo = "taller" } } });
        var entry = await fixture.Db.MemoryEntries.FirstAsync(e => e.TutorId == 1 && e.Key == MemoryKeys.ActivityHistory);
        entry.ValueJson = json;
        await fixture.Db.SaveChangesAsync();
        var prompt = await fixture.Builder.BuildSystemPromptAsync(1, ContextLoadProfile.Project);
        Assert.Contains("P1", prompt);
    }

    [Fact]
    public async Task ConversationService_CapsHistoryAt30()
    {
        await using var fixture = await ConversationFixture.CreateAsync(messageCount: 40);
        var captured = fixture.CapturedRequests;
        var result = await fixture.ConversationService.SendMessageAsync(10, new LearningAgents.Application.Dtos.Conversations.CreateConversationMessageRequest("hola cap test", ContextLoadProfile.Standard));

        Assert.True(result.IsSuccess);
        Assert.Single(captured);
        var sentMessages = captured[0].Messages;
        // Should be capped to 30 history + 1 resumen + 1 current user = 32
        Assert.True(sentMessages.Count <= 32, $"Expected <=32 messages sent to LLM, got {sentMessages.Count}");
        // The oldest of the 40 should have been dropped
        Assert.DoesNotContain(sentMessages, m => m.Content == "msg 1");
        Assert.Contains(sentMessages, m => m.Content == "msg 40");
    }

    [Fact]
    public async Task ConversationService_ShortHistory_NotTruncated()
    {
        await using var fixture = await ConversationFixture.CreateAsync(messageCount: 5);
        var captured = fixture.CapturedRequests;
        var result = await fixture.ConversationService.SendMessageAsync(10, new LearningAgents.Application.Dtos.Conversations.CreateConversationMessageRequest("hola", ContextLoadProfile.Standard));
        Assert.True(result.IsSuccess);
        Assert.Single(captured);
        // 5 history + 1 current = 6
        Assert.Equal(6, captured[0].Messages.Count);
    }

    private static MemoryPatch Patch(string key, MemoryPatchOperation op, string path, string valueJson) =>
        new(key, op, path, null, JsonSerializer.Deserialize<JsonElement>(valueJson), "test");

    private sealed class Fixture : IAsyncDisposable
    {
        public static async Task<Fixture> CreateAsync()
        {
            var conn = new SqliteConnection("Data Source=:memory:");
            await conn.OpenAsync();
            var services = new ServiceCollection();
            services.AddDbContext<LearningAgentsDbContext>(o => o.UseSqlite(conn));
            services.AddApplicationServices();
            services.AddSingleton<IOptions<GeminiOptions>>(_ => Options.Create(new GeminiOptions()));
            services.AddSingleton<IOptions<LlmProfilesOptions>>(_ => Options.Create(new LlmProfilesOptions()));
            var sp = services.BuildServiceProvider();
            var db = sp.GetRequiredService<LearningAgentsDbContext>();
            await db.Database.EnsureCreatedAsync();
            db.StudySessions.Add(new StudySession { Id = 1, TutorId = 1, Name = "S", Goal = "G", CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow });
            await db.SaveChangesAsync();
            return new Fixture(conn, sp, db, sp.GetRequiredService<IMemoryPatchEngine>(), sp.GetRequiredService<IPromptBuilder>());
        }
        private Fixture(SqliteConnection c, ServiceProvider sp, LearningAgentsDbContext db, IMemoryPatchEngine e, IPromptBuilder b)
        { Conn = c; Sp = sp; Db = db; Engine = e; Builder = b; }
        public SqliteConnection Conn { get; }
        public ServiceProvider Sp { get; }
        public LearningAgentsDbContext Db { get; }
        public IMemoryPatchEngine Engine { get; }
        public IPromptBuilder Builder { get; }
        public async ValueTask DisposeAsync() { await Db.DisposeAsync(); await Sp.DisposeAsync(); await Conn.DisposeAsync(); }
    }

    private sealed class ConversationFixture : IAsyncDisposable
    {
        public List<PromptRequest> CapturedRequests { get; } = new();
        public IConversationService ConversationService { get; }
        private readonly SqliteConnection conn;
        private readonly ServiceProvider sp;
        public static async Task<ConversationFixture> CreateAsync(int messageCount)
        {
            var conn = new SqliteConnection("Data Source=:memory:");
            await conn.OpenAsync();
            var captured = new List<PromptRequest>();
            var fake = new CapturingProvider(captured);
            var services = new ServiceCollection();
            services.AddDbContext<LearningAgentsDbContext>(o => o.UseSqlite(conn));
            services.AddApplicationServices();
            // Override router to use capturing provider for any profile
            services.AddSingleton<ILLMProviderRouter>(_ => new CapturingRouter(fake));
            services.AddSingleton<IOptions<GeminiOptions>>(_ => Options.Create(new GeminiOptions { DefaultModel = "test-model" }));
            services.AddSingleton<IOptions<LlmProfilesOptions>>(_ => Options.Create(new LlmProfilesOptions { DefaultProfile = "test" }));
            var sp = services.BuildServiceProvider();
            var db = sp.GetRequiredService<LearningAgentsDbContext>();
            await db.Database.EnsureCreatedAsync();
            var baseTime = new DateTime(2026, 8, 27, 12, 0, 0, DateTimeKind.Utc);
            db.StudySessions.Add(new StudySession { Id = 10, TutorId = 1, Name = "S10", Goal = "G", CreatedAtUtc = baseTime, UpdatedAtUtc = baseTime });
            for (int i = 1; i <= messageCount; i++)
                db.Messages.Add(new Message { Id = i, SessionId = 10, Role = i % 2 == 1 ? MessageRole.User : MessageRole.Assistant, Content = $"msg {i}", CreatedAtUtc = baseTime.AddMinutes(i) });
            await db.SaveChangesAsync();
            var svc = sp.GetRequiredService<IConversationService>();
            return new ConversationFixture(conn, sp, svc, captured);
        }
        private ConversationFixture(SqliteConnection c, ServiceProvider sp, IConversationService svc, List<PromptRequest> cap)
        { conn = c; this.sp = sp; ConversationService = svc; CapturedRequests = cap; }
        public async ValueTask DisposeAsync() { await sp.DisposeAsync(); await conn.DisposeAsync(); }

        private sealed class CapturingProvider(List<PromptRequest> outList) : ILLMProvider
        {
            public Task<LLMResponse> GenerateAsync(PromptRequest request, CancellationToken ct)
            {
                outList.Add(request);
                return Task.FromResult(new LLMResponse(true, "ok captured", null, 200, null));
            }
        }
        private sealed class CapturingRouter(ILLMProvider p) : ILLMProviderRouter
        {
            public Task<LLMResponse> GenerateAsync(string profile, PromptRequest request, CancellationToken ct) => p.GenerateAsync(request, ct);
            public ILLMProvider GetProvider(string profile) => p;
        }
    }
}
