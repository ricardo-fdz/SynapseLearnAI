using System.Net;
using System.Text;
using System.Text.Json;
using LearningAgents.Application;
using LearningAgents.Application.Dtos.Conversations;
using LearningAgents.Application.Enums;
using LearningAgents.Application.Interfaces;
using LearningAgents.Domain.Entities;
using LearningAgents.Domain.LLM;
using LearningAgents.Infrastructure.LLM;
using LearningAgents.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace LearningAgents.Tests;

public sealed class StreamingConversationTests
{
    [Fact]
    public async Task GeminiProvider_StreamGenerateAsync_EmitsDeltasAndUsage()
    {
        var ssePayload = string.Join("\n", [
            """data: {"candidates":[{"content":{"parts":[{"text":"Hola "}]}}]}""",
            "",
            """data: {"candidates":[{"content":{"parts":[{"text":"estudiante!"}]}}],"usageMetadata":{"promptTokenCount":50,"candidatesTokenCount":12,"totalTokenCount":62}}""",
            ""
        ]);

        var provider = new GeminiProvider(
            new HttpClient(new StubSseHandler(ssePayload)),
            Options.Create(new GeminiOptions { ApiKey = "test-key", DefaultModel = "gemini-2.5-flash" }));

        var chunks = new List<LLMStreamChunk>();
        await foreach (var chunk in provider.StreamGenerateAsync(
            new PromptRequest("gemini-2.5-flash", "sys", [new LLMMessage("user", "hola")])))
        {
            chunks.Add(chunk);
        }

        Assert.Equal("Hola estudiante!", string.Concat(chunks.Select(c => c.TextDelta)));
        var usageChunk = Assert.Single(chunks, c => c.Usage is not null);
        Assert.Equal(62, usageChunk.Usage!.TotalTokens);
    }

    [Fact]
    public async Task GroqProvider_StreamGenerateAsync_AccumulatesStreamedToolCallsAndDeltas()
    {
        var ssePayload = string.Join("\n", [
            """data: {"choices":[{"delta":{"tool_calls":[{"index":0,"id":"call_abc","function":{"name":"leer_memoria","arguments":"{\"key\":"}}]}}]}""",
            "",
            """data: {"choices":[{"delta":{"tool_calls":[{"index":0,"function":{"arguments":"\"mapa_dominio\"}"}}]}}],"usage":{"prompt_tokens":40,"completion_tokens":10,"total_tokens":50}}""",
            "",
            "data: [DONE]",
            ""
        ]);

        var groq = new GroqProvider(
            new HttpClient(new StubSseHandler(ssePayload)),
            Options.Create(new GroqOptions { ApiKey = "groq-key" }));

        var chunks = new List<LLMStreamChunk>();
        await foreach (var chunk in groq.StreamGenerateAsync(
            new PromptRequest("qwen-qwq-32b", "sys", [new LLMMessage("user", "hola")])))
        {
            chunks.Add(chunk);
        }

        var toolChunk = Assert.Single(chunks);
        var call = Assert.Single(toolChunk.FunctionCalls!);
        Assert.Equal("leer_memoria", call.Name);
        Assert.Equal("call_abc", call.Id);
        Assert.Equal("mapa_dominio", call.Args.GetProperty("key").GetString());
        Assert.Equal(50, toolChunk.Usage!.TotalTokens);
    }

    [Fact]
    public async Task ConversationService_StreamMessageAsync_EmitsToolEvents_TokenDeltas_AndPersistsFinalMessage()
    {
        await using var conn = new SqliteConnection("Data Source=:memory:");
        await conn.OpenAsync();

        var saveArgs = JsonSerializer.Deserialize<JsonElement>("""
        {
          "patch": {
            "key": "memoria_sesion",
            "operation": "Set",
            "path": "/proximo_paso",
            "value": "Practicar recursividad",
            "reason": "Avance en sesion"
          }
        }
        """);

        var router = new ScriptedStreamRouter([
            [LLMStreamChunk.ToolCalls([new LLMFunctionCall("guardar_memoria", saveArgs, Id: "c1")])],
            [
                LLMStreamChunk.Delta("Muy bien, "),
                LLMStreamChunk.Delta("pasemos a recursividad."),
                LLMStreamChunk.Completed(new LLMTokenUsage(100, 20, 120))
            ]
        ]);

        var services = new ServiceCollection();
        services.AddDbContext<LearningAgentsDbContext>(o => o.UseSqlite(conn));
        services.AddApplicationServices();
        services.AddTestInfrastructureBindings();
        services.AddSingleton<ILLMProviderRouter>(router);

        await using var sp = services.BuildServiceProvider();
        var db = sp.GetRequiredService<LearningAgentsDbContext>();
        await db.Database.EnsureCreatedAsync();

        var now = DateTime.UtcNow.AddMinutes(-5);
        db.StudySessions.Add(new StudySession
        {
            Id = 20,
            TutorId = 1,
            Name = "Recursividad",
            Goal = "Aprender recursión",
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        });
        await db.SaveChangesAsync();

        var conversationService = sp.GetRequiredService<IConversationService>();
        var events = new List<ConversationStreamEvent>();
        await foreach (var evt in conversationService.StreamMessageAsync(
            20,
            new CreateConversationMessageRequest("Listo para el siguiente tema", ContextLoadProfile.Standard)))
        {
            events.Add(evt);
        }

        Assert.Equal(
            ["user_message", "tool_call_started", "tool_call_completed", "token_delta", "token_delta", "turn_completed"],
            events.Select(e => e.Type).ToArray());

        var completed = Assert.IsType<ConversationMessageResponse>(events[^1].Payload);
        Assert.Equal("Muy bien, pasemos a recursividad.", completed.AssistantMessage.Content);
        Assert.Single(completed.AppliedMemoryChanges);

        var persistedMessages = await db.Messages.AsNoTracking().Where(m => m.SessionId == 20).OrderBy(m => m.Id).ToListAsync();
        Assert.Equal(2, persistedMessages.Count);
        Assert.Equal("Muy bien, pasemos a recursividad.", persistedMessages[1].Content);
    }

    private sealed class StubSseHandler(string sseBody) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(sseBody, Encoding.UTF8, "text/event-stream")
            });
    }

    private sealed class ScriptedStreamRouter(IReadOnlyList<IReadOnlyList<LLMStreamChunk>> turns) : ILLMProviderRouter
    {
        private int index;

        public Task<LLMResponse> GenerateAsync(string profileName, PromptRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public async IAsyncEnumerable<LLMStreamChunk> StreamGenerateAsync(
            string profileName,
            PromptRequest request,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var chunks = turns[Math.Min(index, turns.Count - 1)];
            index++;
            foreach (var chunk in chunks)
            {
                await Task.Yield();
                yield return chunk;
            }
        }
    }
}
