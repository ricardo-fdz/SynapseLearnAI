using System.Net;
using LearningAgents.Domain.LLM;
using LearningAgents.Infrastructure.LLM;
using Microsoft.Extensions.Options;
using Xunit;

namespace LearningAgents.Tests;

public sealed class OpenAICompatibleProviderTests
{
    [Fact]
    public async Task GroqProvider_GenerateAsync_ReturnsGeneratedText()
    {
        var handler = new CaptureMessageHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""
            {
              "choices": [
                {
                  "message": {
                    "role": "assistant",
                    "content": "hello from groq"
                  }
                }
              ]
            }
            """)
        });
        var provider = new GroqProvider(new HttpClient(handler), Options.Create(new GroqOptions { ApiKey = "test-key" }));

        var response = await provider.GenerateAsync(new PromptRequest(
            "openai/gpt-oss-20b",
            "system",
            [new LLMMessage("user", "hello")],
            Tools: null), CancellationToken.None);

        Assert.True(response.IsSuccess);
        Assert.Equal("hello from groq", response.Content);
        Assert.Equal("Bearer", handler.LastRequest?.Headers.Authorization?.Scheme);
        Assert.Equal("test-key", handler.LastRequest?.Headers.Authorization?.Parameter);
    }

    [Fact]
    public async Task GroqProvider_GenerateAsync_ReturnsToolCalls()
    {
        var handler = new CaptureMessageHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""
            {
              "choices": [
                {
                  "message": {
                    "role": "assistant",
                    "tool_calls": [
                      {
                        "id": "call_123",
                        "type": "function",
                        "function": {
                          "name": "leer_memoria",
                          "arguments": "{\"key\":\"perfil_estudiante\"}"
                        }
                      }
                    ]
                  }
                }
              ]
            }
            """)
        });
        var provider = new GroqProvider(new HttpClient(handler), Options.Create(new GroqOptions { ApiKey = "test-key" }));

        var response = await provider.GenerateAsync(new PromptRequest(
            "openai/gpt-oss-20b",
            "system",
            [new LLMMessage("user", "hello")],
            Tools: [new LLMToolDeclaration("leer_memoria", "Lee memoria", System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>("{\"type\":\"object\"}"))]), CancellationToken.None);

        Assert.True(response.IsSuccess);
        var call = Assert.Single(response.FunctionCalls!);
        Assert.Equal("call_123", call.Id);
        Assert.Equal("leer_memoria", call.Name);
        Assert.Equal("perfil_estudiante", call.Args.GetProperty("key").GetString());
    }

    [Fact]
    public async Task OpenRouterProvider_GenerateAsync_SendsProviderHeaders()
    {
        var handler = new CaptureMessageHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""
            {
              "choices": [
                {
                  "message": {
                    "role": "assistant",
                    "content": "hello from openrouter"
                  }
                }
              ]
            }
            """)
        });
        var provider = new OpenRouterProvider(new HttpClient(handler), Options.Create(new OpenRouterOptions
        {
            ApiKey = "router-key",
            HttpReferer = "https://example.test",
            Title = "LearningAgents Tests"
        }));

        var response = await provider.GenerateAsync(new PromptRequest(
            "openai/gpt-oss-20b",
            "system",
            [new LLMMessage("user", "hello")],
            Tools: null), CancellationToken.None);

        Assert.True(response.IsSuccess);
        Assert.NotNull(handler.LastRequest);
        Assert.True(handler.LastRequest.Headers.TryGetValues("HTTP-Referer", out var refererValues));
        Assert.Contains("https://example.test", refererValues!);
        Assert.True(handler.LastRequest.Headers.TryGetValues("X-Title", out var titleValues));
        Assert.Contains("LearningAgents Tests", titleValues!);
    }

    private sealed class CaptureMessageHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(response);
        }
    }
}
