using System.Text.Json;
using LearningAgents.Application;
using LearningAgents.Application.Dtos.Conversations;
using LearningAgents.Application.Enums;
using LearningAgents.Application.Interfaces;
using LearningAgents.Domain.Entities;
using LearningAgents.Domain.Enums;
using LearningAgents.Domain.LLM;
using LearningAgents.Infrastructure.LLM;
using LearningAgents.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace LearningAgents.Tests;

public sealed class ConversationServiceTests
{
    [Fact]
    public async Task SendMessageAsync_UpdatesSessionUpdatedAtUtc_AndReturnsAppliedMemoryChanges()
    {
        var toolArgs = JsonSerializer.Deserialize<JsonElement>("""
        {
          "patch": {
            "key": "memoria_sesion",
            "operation": "Set",
            "path": "/proximo_paso",
            "value": "Practicar closures",
            "reason": "Actualizar siguiente paso tras ejercicio"
          }
        }
        """);

        await using var fixture = await ConversationTestFixture.CreateAsync([
            LLMResponse.ToolCalls([new LLMFunctionCall("guardar_memoria", toolArgs, Id: "call_1")]),
            LLMResponse.Success("Excelente avance con closures.")
        ]);

        var initialSession = await fixture.Db.StudySessions.AsNoTracking().SingleAsync(s => s.Id == 10);
        var result = await fixture.ConversationService.SendMessageAsync(
            10,
            new CreateConversationMessageRequest("Hola, ya terminé el ejercicio", ContextLoadProfile.Standard));

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Data);
        Assert.Equal("user", result.Data!.UserMessage.Role);
        Assert.Equal("Hola, ya terminé el ejercicio", result.Data.UserMessage.Content);
        Assert.Equal("assistant", result.Data.AssistantMessage.Role);
        Assert.Equal("Excelente avance con closures.", result.Data.AssistantMessage.Content);

        var appliedChange = Assert.Single(result.Data.AppliedMemoryChanges);
        Assert.Equal("memoria_sesion", appliedChange.Key);
        Assert.Equal("Set", appliedChange.Operation);
        Assert.Equal("/proximo_paso", appliedChange.Path);

        var updatedSession = await fixture.Db.StudySessions.AsNoTracking().SingleAsync(s => s.Id == 10);
        Assert.True(updatedSession.UpdatedAtUtc > initialSession.UpdatedAtUtc);
    }

    [Fact]
    public async Task SendMessageAsync_Transient429_DoesNotPersistFakeAssistantMessage_AndRetryReusesUserMessage()
    {
        await using var fixture = await ConversationTestFixture.CreateAsync([
            LLMResponse.Failure("Gemini overloaded", statusCode: 429),
            LLMResponse.Success("Respuesta real tras reintento.")
        ]);

        var firstAttempt = await fixture.ConversationService.SendMessageAsync(
            10,
            new CreateConversationMessageRequest("Explica event loop", ContextLoadProfile.Standard));

        Assert.False(firstAttempt.IsSuccess);
        Assert.Equal(429, firstAttempt.StatusCode);
        var failureDetails = Assert.IsType<ConversationFailureDetails>(firstAttempt.ErrorDetails);
        Assert.NotNull(failureDetails.UserMessage);

        var messagesAfterFailure = await fixture.Db.Messages
            .AsNoTracking()
            .Where(m => m.SessionId == 10)
            .ToListAsync();
        var persistedUser = Assert.Single(messagesAfterFailure);
        Assert.Equal(MessageRole.User, persistedUser.Role);
        Assert.Equal("Explica event loop", persistedUser.Content);

        var retryAttempt = await fixture.ConversationService.SendMessageAsync(
            10,
            new CreateConversationMessageRequest(
                Content: null,
                Profile: ContextLoadProfile.Standard,
                RetryUserMessageId: persistedUser.Id));

        Assert.True(retryAttempt.IsSuccess);
        Assert.Equal(persistedUser.Id, retryAttempt.Data!.UserMessage.Id);
        Assert.Equal("Respuesta real tras reintento.", retryAttempt.Data.AssistantMessage.Content);

        var messagesAfterRetry = await fixture.Db.Messages
            .AsNoTracking()
            .Where(m => m.SessionId == 10)
            .OrderBy(m => m.Id)
            .ToListAsync();
        Assert.Equal(2, messagesAfterRetry.Count);
        Assert.Equal(MessageRole.User, messagesAfterRetry[0].Role);
        Assert.Equal(MessageRole.Assistant, messagesAfterRetry[1].Role);

        // Second retry on an already-answered message must fail with 409 Conflict
        var duplicateRetry = await fixture.ConversationService.SendMessageAsync(
            10,
            new CreateConversationMessageRequest(RetryUserMessageId: persistedUser.Id));
        Assert.False(duplicateRetry.IsSuccess);
        Assert.Equal(409, duplicateRetry.StatusCode);
    }

    private sealed class ConversationTestFixture : IAsyncDisposable
    {
        private readonly SqliteConnection connection;
        private readonly ServiceProvider serviceProvider;

        private ConversationTestFixture(SqliteConnection connection, ServiceProvider serviceProvider)
        {
            this.connection = connection;
            this.serviceProvider = serviceProvider;
            Db = serviceProvider.GetRequiredService<LearningAgentsDbContext>();
            ConversationService = serviceProvider.GetRequiredService<IConversationService>();
        }

        public LearningAgentsDbContext Db { get; }
        public IConversationService ConversationService { get; }

        public static async Task<ConversationTestFixture> CreateAsync(IReadOnlyList<LLMResponse> responses)
        {
            var conn = new SqliteConnection("Data Source=:memory:");
            await conn.OpenAsync();
            var services = new ServiceCollection();
            services.AddDbContext<LearningAgentsDbContext>(o => o.UseSqlite(conn));
            services.AddApplicationServices();
            services.AddTestInfrastructureBindings();
            services.AddSingleton<ILLMProviderRouter>(new QueuedRouter(responses));
            services.AddSingleton(Options.Create(new GeminiOptions { DefaultModel = "test-model" }));
            services.AddSingleton(Options.Create(new LlmProfilesOptions { DefaultProfile = "test-profile" }));

            var sp = services.BuildServiceProvider();
            var db = sp.GetRequiredService<LearningAgentsDbContext>();
            await db.Database.EnsureCreatedAsync();

            var oldTime = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc);
            db.StudySessions.Add(new StudySession
            {
                Id = 10,
                TutorId = 1,
                Name = "Sesión Closures",
                Goal = "Dominar closures",
                CreatedAtUtc = oldTime,
                UpdatedAtUtc = oldTime
            });
            await db.SaveChangesAsync();

            return new ConversationTestFixture(conn, sp);
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await serviceProvider.DisposeAsync();
            await connection.DisposeAsync();
        }

        private sealed class QueuedRouter(IReadOnlyList<LLMResponse> responses) : ILLMProviderRouter
        {
            private int index;

            public Task<LLMResponse> GenerateAsync(string profileName, PromptRequest request, CancellationToken cancellationToken)
            {
                var response = responses[Math.Min(index, responses.Count - 1)];
                index++;
                return Task.FromResult(response);
            }
        }
    }
}
