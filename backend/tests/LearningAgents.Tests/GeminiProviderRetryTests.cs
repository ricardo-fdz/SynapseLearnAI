using System.Net;
using System.Text.Json;
using LearningAgents.Domain.LLM;
using LearningAgents.Infrastructure.LLM;
using Microsoft.Extensions.Options;
using Xunit;

namespace LearningAgents.Tests;

public sealed class GeminiProviderRetryTests
{
    [Fact]
    public async Task GenerateAsync_RetriesTransient429_ThenReturnsSuccess()
    {
        var handler = new QueueMessageHandler(
            new HttpResponseMessage(HttpStatusCode.TooManyRequests)
            {
                Content = new StringContent("""{"error":{"message":"rate limited"}}""")
            },
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                {
                  "candidates": [
                    {
                      "content": {
                        "parts": [
                          { "text": "ok after retry" }
                        ]
                      }
                    }
                  ]
                }
                """)
            });
        handler.Responses[0].Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.Zero);

        var provider = new GeminiProvider(
            new HttpClient(handler),
            Options.Create(new GeminiOptions { ApiKey = "test-key" }));

        var response = await provider.GenerateAsync(new PromptRequest(
            "gemini-test",
            "system",
            [new LLMMessage("user", "hello")],
            Tools: null),
            CancellationToken.None);

        Assert.True(response.IsSuccess);
        Assert.Equal("ok after retry", response.Content);
        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task GenerateAsync_UsesFallbackModel_WhenPrimaryModelExhaustsTransientRetries()
    {
        var handler = new QueueMessageHandler(
            CreateRateLimitResponse(),
            CreateRateLimitResponse(),
            CreateRateLimitResponse(),
            CreateRateLimitResponse(),
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                {
                  "candidates": [
                    {
                      "content": {
                        "parts": [
                          { "text": "ok from fallback" }
                        ]
                      }
                    }
                  ]
                }
                """)
            });

        foreach (var rateLimitResponse in handler.Responses.Where(response => response.StatusCode == HttpStatusCode.TooManyRequests))
        {
            rateLimitResponse.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.Zero);
        }

        var provider = new GeminiProvider(
            new HttpClient(handler),
            Options.Create(new GeminiOptions
            {
                ApiKey = "test-key",
                FallbackModel = "gemini-fallback"
            }));

        var response = await provider.GenerateAsync(new PromptRequest(
            "gemini-primary",
            "system",
            [new LLMMessage("user", "hello")],
            Tools: null),
            CancellationToken.None);

        Assert.True(response.IsSuccess);
        Assert.Equal("ok from fallback", response.Content);
        Assert.Equal(5, handler.RequestUris.Count);
        Assert.All(handler.RequestUris.Take(4), uri => Assert.Contains("models/gemini-primary:generateContent", uri));
        Assert.Contains("models/gemini-fallback:generateContent", handler.RequestUris[4]);
    }

    [Fact]
    public async Task GenerateAsync_ExtractsThoughtSignatureFromFunctionCalls()
    {
        var handler = new QueueMessageHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""
            {
              "candidates": [
                {
                  "content": {
                    "parts": [
                      {
                        "thoughtSignature": "thought-signature-123",
                        "functionCall": {
                          "name": "guardar_memoria",
                          "args": { "patch": { "key": "perfil_estudiante" } }
                        }
                      }
                    ]
                  }
                }
              ]
            }
            """)
        });
        var provider = new GeminiProvider(
            new HttpClient(handler),
            Options.Create(new GeminiOptions { ApiKey = "test-key" }));

        var response = await provider.GenerateAsync(new PromptRequest(
            "gemini-test",
            "system",
            [new LLMMessage("user", "hello")],
            Tools: [new LLMToolDeclaration("guardar_memoria", "Guarda memoria", JsonSerializer.Deserialize<JsonElement>("{\"type\":\"object\"}"))]),
            CancellationToken.None);

        Assert.True(response.IsSuccess);
        var call = Assert.Single(response.FunctionCalls!);
        Assert.Equal("guardar_memoria", call.Name);
        Assert.Equal("thought-signature-123", call.ThoughtSignature);
    }

    [Fact]
    public async Task GenerateAsync_SendsThoughtSignatureWhenReplayingFunctionCall()
    {
        var handler = new QueueMessageHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""
            {
              "candidates": [
                {
                  "content": {
                    "parts": [
                      { "text": "ok" }
                    ]
                  }
                }
              ]
            }
            """)
        });
        var provider = new GeminiProvider(
            new HttpClient(handler),
            Options.Create(new GeminiOptions { ApiKey = "test-key" }));
        var args = JsonSerializer.Deserialize<JsonElement>("{\"patch\":{\"key\":\"perfil_estudiante\"}}");

        var response = await provider.GenerateAsync(new PromptRequest(
            "gemini-test",
            "system",
            [LLMMessage.ForFunctionCall(new LLMFunctionCall("guardar_memoria", args, ThoughtSignature: "thought-signature-123"))],
            Tools: null),
            CancellationToken.None);

        Assert.True(response.IsSuccess);
        using var document = JsonDocument.Parse(handler.RequestBodies.Single());
        var part = document.RootElement
            .GetProperty("contents")[0]
            .GetProperty("parts")[0];
        Assert.Equal("thought-signature-123", part.GetProperty("thoughtSignature").GetString());
        Assert.Equal("guardar_memoria", part.GetProperty("functionCall").GetProperty("name").GetString());
    }

    [Fact]
    public async Task GenerateAsync_GroupsMultipleFunctionCallsAndResponsesInSingleGeminiContents()
    {
        var handler = new QueueMessageHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""
            {
              "candidates": [
                {
                  "content": {
                    "parts": [
                      { "text": "ok" }
                    ]
                  }
                }
              ]
            }
            """)
        });
        var provider = new GeminiProvider(
            new HttpClient(handler),
            Options.Create(new GeminiOptions { ApiKey = "test-key" }));
        var firstArgs = JsonSerializer.Deserialize<JsonElement>("{\"key\":\"perfil_estudiante\"}");
        var secondArgs = JsonSerializer.Deserialize<JsonElement>("{\"patch\":{\"key\":\"perfil_estudiante\"}}");
        var firstResponse = JsonSerializer.Deserialize<JsonElement>("{\"success\":true}");
        var secondResponse = JsonSerializer.Deserialize<JsonElement>("{\"success\":true}");

        var response = await provider.GenerateAsync(new PromptRequest(
            "gemini-test",
            "system",
            [
                LLMMessage.ForFunctionCalls([
                    new LLMFunctionCall(
                        "leer_memoria",
                        firstArgs,
                        ThoughtSignature: "sig-1",
                        RawPartJson: """{"thoughtSignature":"sig-1","functionCall":{"name":"leer_memoria","args":{"key":"perfil_estudiante"}}}"""),
                    new LLMFunctionCall(
                        "guardar_memoria",
                        secondArgs,
                        ThoughtSignature: "sig-2",
                        RawPartJson: """{"thoughtSignature":"sig-2","functionCall":{"name":"guardar_memoria","args":{"patch":{"key":"perfil_estudiante"}}}}""")
                ]),
                LLMMessage.ForFunctionResponses([
                    new LLMFunctionResponse("leer_memoria", firstResponse),
                    new LLMFunctionResponse("guardar_memoria", secondResponse)
                ])
            ],
            Tools: null),
            CancellationToken.None);

        Assert.True(response.IsSuccess);
        using var document = JsonDocument.Parse(handler.RequestBodies.Single());
        var contents = document.RootElement.GetProperty("contents");
        Assert.Equal("model", contents[0].GetProperty("role").GetString());
        Assert.Equal(2, contents[0].GetProperty("parts").GetArrayLength());
        Assert.Equal("sig-1", contents[0].GetProperty("parts")[0].GetProperty("thoughtSignature").GetString());
        Assert.Equal("sig-2", contents[0].GetProperty("parts")[1].GetProperty("thoughtSignature").GetString());
        Assert.Equal("user", contents[1].GetProperty("role").GetString());
        Assert.Equal(2, contents[1].GetProperty("parts").GetArrayLength());
        Assert.Equal("leer_memoria", contents[1].GetProperty("parts")[0].GetProperty("functionResponse").GetProperty("name").GetString());
        Assert.Equal("guardar_memoria", contents[1].GetProperty("parts")[1].GetProperty("functionResponse").GetProperty("name").GetString());
    }

    private static HttpResponseMessage CreateRateLimitResponse() => new(HttpStatusCode.TooManyRequests)
    {
        Content = new StringContent("""{"error":{"message":"rate limited"}}""")
    };

    private sealed class QueueMessageHandler(params HttpResponseMessage[] responses) : HttpMessageHandler
    {
        private int index;

        public IReadOnlyList<HttpResponseMessage> Responses { get; } = responses;

        public List<string> RequestUris { get; } = [];

        public List<string> RequestBodies { get; } = [];

        public int CallCount { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            RequestUris.Add(request.RequestUri?.ToString() ?? string.Empty);
            RequestBodies.Add(request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken));
            var response = responses[Math.Min(index, responses.Length - 1)];
            index++;
            return response;
        }
    }
}
