using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using LearningAgents.Domain.LLM;

namespace LearningAgents.Infrastructure.LLM;

public abstract class OpenAICompatibleProvider(HttpClient httpClient) : ILLMProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    protected abstract string ProviderName { get; }

    protected abstract string Endpoint { get; }

    protected abstract string ApiKey { get; }

    protected virtual void AddProviderHeaders(HttpRequestMessage request)
    {
    }

    public async Task<LLMResponse> GenerateAsync(PromptRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            return LLMResponse.Failure($"{ProviderName} API key is not configured.");
        }

        if (string.IsNullOrWhiteSpace(request.Model))
        {
            return LLMResponse.Failure($"{ProviderName} model is required.");
        }

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = JsonContent.Create(BuildPayload(request), options: JsonOptions)
        };
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ApiKey);
        AddProviderHeaders(httpRequest);

        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(httpRequest, cancellationToken);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return LLMResponse.Failure($"{ProviderName} request timed out.");
        }
        catch (HttpRequestException exception)
        {
            return LLMResponse.Failure($"{ProviderName} request failed: {exception.Message}");
        }

        using var _ = response;
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var statusCode = (int)response.StatusCode;
            return LLMResponse.Failure($"{ProviderName} returned HTTP {statusCode}: {ExtractErrorMessage(responseText)}", statusCode);
        }

        try
        {
            using var document = JsonDocument.Parse(responseText);
            var functionCalls = ExtractFunctionCalls(document.RootElement);
            if (functionCalls.Count > 0)
            {
                return LLMResponse.ToolCalls(functionCalls);
            }

            var text = ExtractGeneratedText(document.RootElement);
            return string.IsNullOrWhiteSpace(text)
                ? LLMResponse.Failure($"{ProviderName} response did not contain generated text.")
                : LLMResponse.Success(text);
        }
        catch (JsonException exception)
        {
            return LLMResponse.Failure($"{ProviderName} returned malformed JSON: {exception.Message}");
        }
    }

    private static object BuildPayload(PromptRequest request)
    {
        var messages = new List<object>
        {
            new { role = "system", content = request.SystemPrompt }
        };

        messages.AddRange(ToOpenAIMessages(request.Messages));

        var payload = new Dictionary<string, object?>
        {
            ["model"] = request.Model,
            ["messages"] = messages
        };

        if (request.Tools is { Count: > 0 })
        {
            payload["tools"] = request.Tools.Select(tool => new
            {
                type = "function",
                function = new
                {
                    name = tool.Name,
                    description = tool.Description,
                    parameters = tool.Parameters
                }
            }).ToArray();
            payload["tool_choice"] = "auto";
        }

        return payload;
    }

    private static IEnumerable<object> ToOpenAIMessages(IReadOnlyList<LLMMessage> messages)
    {
        var converted = new List<object>();
        var pendingToolCallIdsByName = new Dictionary<string, Queue<string>>(StringComparer.Ordinal);
        for (var index = 0; index < messages.Count; index++)
        {
            var message = messages[index];
            if (message.FunctionCall is not null)
            {
                var toolCallId = message.FunctionCall.Id ?? $"call_{index}_{message.FunctionCall.Name}";
                if (!pendingToolCallIdsByName.TryGetValue(message.FunctionCall.Name, out var pendingIds))
                {
                    pendingIds = new Queue<string>();
                    pendingToolCallIdsByName[message.FunctionCall.Name] = pendingIds;
                }

                pendingIds.Enqueue(toolCallId);
                converted.Add(new
                {
                    role = "assistant",
                    content = (string?)null,
                    tool_calls = new[]
                    {
                        new
                        {
                            id = toolCallId,
                            type = "function",
                            function = new
                            {
                                name = message.FunctionCall.Name,
                                arguments = message.FunctionCall.Args.GetRawText()
                            }
                        }
                    }
                });
                continue;
            }

            if (message.FunctionResponse is not null)
            {
                var toolCallId = message.FunctionResponse.Id ?? ResolvePendingToolCallId(
                    pendingToolCallIdsByName,
                    message.FunctionResponse.Name);
                converted.Add(new
                {
                    role = "tool",
                    tool_call_id = toolCallId,
                    content = message.FunctionResponse.Response.GetRawText()
                });
                continue;
            }

            converted.Add(new
            {
                role = ToOpenAIRole(message.Role),
                content = message.Content ?? string.Empty
            });
        }

        return converted;
    }

    private static string ResolvePendingToolCallId(
        Dictionary<string, Queue<string>> pendingToolCallIdsByName,
        string functionName)
    {
        return pendingToolCallIdsByName.TryGetValue(functionName, out var pendingIds) && pendingIds.Count > 0
            ? pendingIds.Dequeue()
            : $"call_{functionName}";
    }

    private static string ToOpenAIRole(string role) =>
        role.Equals("assistant", StringComparison.OrdinalIgnoreCase) ? "assistant" : "user";

    private static IReadOnlyList<LLMFunctionCall> ExtractFunctionCalls(JsonElement root)
    {
        var message = GetFirstMessage(root);
        if (message.ValueKind == JsonValueKind.Undefined
            || !message.TryGetProperty("tool_calls", out var toolCalls)
            || toolCalls.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var calls = new List<LLMFunctionCall>();
        foreach (var toolCall in toolCalls.EnumerateArray())
        {
            if (!toolCall.TryGetProperty("function", out var function)
                || !function.TryGetProperty("name", out var nameElement))
            {
                continue;
            }

            var name = nameElement.GetString();
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var argumentsText = function.TryGetProperty("arguments", out var argumentsElement)
                ? argumentsElement.GetString()
                : "{}";
            var args = JsonDocument.Parse(string.IsNullOrWhiteSpace(argumentsText) ? "{}" : argumentsText).RootElement.Clone();
            var id = toolCall.TryGetProperty("id", out var idElement) ? idElement.GetString() : null;
            calls.Add(new LLMFunctionCall(name, args, id));
        }

        return calls;
    }

    private static string ExtractGeneratedText(JsonElement root)
    {
        var message = GetFirstMessage(root);
        return message.ValueKind != JsonValueKind.Undefined
            && message.TryGetProperty("content", out var contentElement)
            && contentElement.ValueKind == JsonValueKind.String
            ? contentElement.GetString() ?? string.Empty
            : string.Empty;
    }

    private static JsonElement GetFirstMessage(JsonElement root)
    {
        if (!root.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array)
        {
            return default;
        }

        foreach (var choice in choices.EnumerateArray())
        {
            if (choice.TryGetProperty("message", out var message))
            {
                return message;
            }
        }

        return default;
    }

    private static string ExtractErrorMessage(string responseText)
    {
        try
        {
            using var document = JsonDocument.Parse(responseText);
            if (document.RootElement.TryGetProperty("error", out var error)
                && error.TryGetProperty("message", out var message))
            {
                return message.GetString() ?? responseText;
            }
        }
        catch (JsonException)
        {
            return responseText;
        }

        return responseText;
    }
}
