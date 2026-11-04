using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
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
            Content = JsonContent.Create(BuildPayload(request, stream: false), options: JsonOptions)
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
            var usage = ExtractUsage(document.RootElement);
            var functionCalls = ExtractFunctionCalls(document.RootElement);
            if (functionCalls.Count > 0)
            {
                return LLMResponse.ToolCalls(functionCalls, usage);
            }

            var text = ExtractGeneratedText(document.RootElement);
            return string.IsNullOrWhiteSpace(text)
                ? LLMResponse.Failure($"{ProviderName} response did not contain generated text.")
                : LLMResponse.Success(text, usage);
        }
        catch (JsonException exception)
        {
            return LLMResponse.Failure($"{ProviderName} returned malformed JSON: {exception.Message}");
        }
    }

    public async IAsyncEnumerable<LLMStreamChunk> StreamGenerateAsync(
        PromptRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            yield return LLMStreamChunk.Failure($"{ProviderName} API key is not configured.");
            yield break;
        }

        if (string.IsNullOrWhiteSpace(request.Model))
        {
            yield return LLMStreamChunk.Failure($"{ProviderName} model is required.");
            yield break;
        }

        var (response, sendError) = await SendStreamRequestAsync(request, cancellationToken);
        if (response is null)
        {
            yield return LLMStreamChunk.Failure(sendError ?? $"{ProviderName} stream request failed.");
            yield break;
        }

        using var ownedResponse = response;
        if (!ownedResponse.IsSuccessStatusCode)
        {
            var statusCode = (int)ownedResponse.StatusCode;
            var errorBody = await ownedResponse.Content.ReadAsStringAsync(cancellationToken);
            yield return LLMStreamChunk.Failure(
                $"{ProviderName} returned HTTP {statusCode}: {ExtractErrorMessage(errorBody)}",
                statusCode);
            yield break;
        }

        await using var stream = await ownedResponse.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);

        var toolCallBuilders = new SortedDictionary<int, ToolCallFragment>();
        LLMTokenUsage? latestUsage = null;
        var emittedAnyText = false;

        while (true)
        {
            var (line, readError) = await ReadLineSafeAsync(reader, cancellationToken);
            if (readError is not null)
            {
                yield return LLMStreamChunk.Failure(readError);
                yield break;
            }

            if (line is null)
            {
                break;
            }

            var trimmed = line.Trim();
            if (!trimmed.StartsWith("data:", StringComparison.Ordinal))
            {
                continue;
            }

            var dataJson = trimmed["data:".Length..].Trim();
            if (string.IsNullOrEmpty(dataJson))
            {
                continue;
            }

            if (dataJson == "[DONE]")
            {
                break;
            }

            var (deltaText, usage, parseError) = ApplyOpenAiSseChunk(dataJson, toolCallBuilders);
            if (parseError is not null)
            {
                yield return LLMStreamChunk.Failure(parseError);
                yield break;
            }

            if (usage is not null)
            {
                latestUsage = usage;
            }

            if (!string.IsNullOrEmpty(deltaText))
            {
                emittedAnyText = true;
                yield return LLMStreamChunk.Delta(deltaText);
            }
        }

        if (toolCallBuilders.Count > 0)
        {
            var calls = BuildAccumulatedToolCalls(toolCallBuilders.Values, out var argsError);
            if (argsError is not null)
            {
                yield return LLMStreamChunk.Failure(argsError);
                yield break;
            }

            if (calls.Count > 0)
            {
                yield return LLMStreamChunk.ToolCalls(calls, latestUsage);
                yield break;
            }
        }

        if (!emittedAnyText)
        {
            yield return LLMStreamChunk.Failure($"{ProviderName} response did not contain generated text.");
            yield break;
        }

        if (latestUsage is not null)
        {
            yield return LLMStreamChunk.Completed(latestUsage);
        }
    }

    private async Task<(HttpResponseMessage? Response, string? Error)> SendStreamRequestAsync(
        PromptRequest request,
        CancellationToken cancellationToken)
    {
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = JsonContent.Create(BuildPayload(request, stream: true), options: JsonOptions)
        };
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ApiKey);
        AddProviderHeaders(httpRequest);

        try
        {
            var response = await httpClient.SendAsync(
                httpRequest,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            return (response, null);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return (null, $"{ProviderName} request timed out.");
        }
        catch (HttpRequestException exception)
        {
            return (null, $"{ProviderName} request failed: {exception.Message}");
        }
    }

    private async Task<(string? Line, string? Error)> ReadLineSafeAsync(
        StreamReader reader,
        CancellationToken cancellationToken)
    {
        try
        {
            return (await reader.ReadLineAsync(cancellationToken), null);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return (null, $"{ProviderName} stream timed out.");
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException && !cancellationToken.IsCancellationRequested)
        {
            return (null, $"{ProviderName} stream interrupted: {exception.Message}");
        }
    }

    private (string? TextDelta, LLMTokenUsage? Usage, string? Error) ApplyOpenAiSseChunk(
        string dataJson,
        SortedDictionary<int, ToolCallFragment> toolCallBuilders)
    {
        try
        {
            using var document = JsonDocument.Parse(dataJson);
            var root = document.RootElement;
            var usage = ExtractUsage(root);
            string? textDelta = null;

            if (root.TryGetProperty("choices", out var choices) && choices.ValueKind == JsonValueKind.Array)
            {
                foreach (var choice in choices.EnumerateArray())
                {
                    if (!choice.TryGetProperty("delta", out var delta) || delta.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    if (delta.TryGetProperty("content", out var contentEl) && contentEl.ValueKind == JsonValueKind.String)
                    {
                        textDelta = (textDelta ?? string.Empty) + contentEl.GetString();
                    }

                    if (delta.TryGetProperty("tool_calls", out var toolCallsEl) && toolCallsEl.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var tc in toolCallsEl.EnumerateArray())
                        {
                            var idx = tc.TryGetProperty("index", out var idxEl) && idxEl.TryGetInt32(out var iv) ? iv : 0;
                            if (!toolCallBuilders.TryGetValue(idx, out var fragment))
                            {
                                fragment = new ToolCallFragment();
                                toolCallBuilders[idx] = fragment;
                            }

                            if (tc.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String)
                            {
                                fragment.Id = idEl.GetString();
                            }

                            if (tc.TryGetProperty("function", out var fnEl) && fnEl.ValueKind == JsonValueKind.Object)
                            {
                                if (fnEl.TryGetProperty("name", out var nameEl) && nameEl.ValueKind == JsonValueKind.String)
                                {
                                    fragment.Name = nameEl.GetString();
                                }

                                if (fnEl.TryGetProperty("arguments", out var argsEl) && argsEl.ValueKind == JsonValueKind.String)
                                {
                                    fragment.Arguments.Append(argsEl.GetString());
                                }
                            }
                        }
                    }
                }
            }

            return (textDelta, usage, null);
        }
        catch (JsonException exception)
        {
            return (null, null, $"{ProviderName} returned malformed SSE JSON: {exception.Message}");
        }
    }

    private IReadOnlyList<LLMFunctionCall> BuildAccumulatedToolCalls(
        IEnumerable<ToolCallFragment> fragments,
        out string? error)
    {
        error = null;
        var calls = new List<LLMFunctionCall>();
        foreach (var fragment in fragments)
        {
            if (string.IsNullOrWhiteSpace(fragment.Name))
            {
                continue;
            }

            var rawArgs = fragment.Arguments.ToString();
            try
            {
                var args = JsonDocument.Parse(string.IsNullOrWhiteSpace(rawArgs) ? "{}" : rawArgs).RootElement.Clone();
                calls.Add(new LLMFunctionCall(fragment.Name, args, fragment.Id));
            }
            catch (JsonException exception)
            {
                error = $"{ProviderName} returned malformed tool arguments JSON: {exception.Message}";
                return [];
            }
        }

        return calls;
    }

    private sealed class ToolCallFragment
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public StringBuilder Arguments { get; } = new();
    }

    private static object BuildPayload(PromptRequest request, bool stream)
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

        if (stream)
        {
            payload["stream"] = true;
        }

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
            var calls = message.FunctionCalls is { Count: > 0 }
                ? message.FunctionCalls
                : message.FunctionCall is not null
                    ? [message.FunctionCall]
                    : null;

            if (calls is not null)
            {
                var openAiToolCalls = new List<object>();
                for (var cIdx = 0; cIdx < calls.Count; cIdx++)
                {
                    var call = calls[cIdx];
                    var toolCallId = call.Id ?? $"call_{index}_{cIdx}_{call.Name}";
                    if (!pendingToolCallIdsByName.TryGetValue(call.Name, out var pendingIds))
                    {
                        pendingIds = new Queue<string>();
                        pendingToolCallIdsByName[call.Name] = pendingIds;
                    }

                    pendingIds.Enqueue(toolCallId);
                    openAiToolCalls.Add(new
                    {
                        id = toolCallId,
                        type = "function",
                        function = new
                        {
                            name = call.Name,
                            arguments = call.Args.GetRawText()
                        }
                    });
                }

                converted.Add(new
                {
                    role = "assistant",
                    content = (string?)null,
                    tool_calls = openAiToolCalls
                });
                continue;
            }

            var responses = message.FunctionResponses is { Count: > 0 }
                ? message.FunctionResponses
                : message.FunctionResponse is not null
                    ? [message.FunctionResponse]
                    : null;

            if (responses is not null)
            {
                foreach (var fnResponse in responses)
                {
                    var toolCallId = fnResponse.Id ?? ResolvePendingToolCallId(
                        pendingToolCallIdsByName,
                        fnResponse.Name);
                    converted.Add(new
                    {
                        role = "tool",
                        tool_call_id = toolCallId,
                        content = fnResponse.Response.GetRawText()
                    });
                }
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

    private static LLMTokenUsage? ExtractUsage(JsonElement root)
    {
        if (!root.TryGetProperty("usage", out var usage) || usage.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var prompt = usage.TryGetProperty("prompt_tokens", out var p) && p.TryGetInt32(out var pv) ? pv : 0;
        var completion = usage.TryGetProperty("completion_tokens", out var c) && c.TryGetInt32(out var cv) ? cv : 0;
        var total = usage.TryGetProperty("total_tokens", out var t) && t.TryGetInt32(out var tv) ? tv : prompt + completion;
        return new LLMTokenUsage(prompt, completion, total);
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
