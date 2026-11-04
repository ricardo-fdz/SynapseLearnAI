using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using LearningAgents.Domain.LLM;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LearningAgents.Infrastructure.LLM;

public sealed class GeminiProvider(
    HttpClient httpClient,
    IOptions<GeminiOptions> options,
    ILogger<GeminiProvider>? logger = null) : ILLMProvider
{
    private const int MaxTransientRetries = 3;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly GeminiOptions options = options.Value;

    public async Task<LLMResponse> GenerateAsync(PromptRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            return LLMResponse.Failure("Gemini API key is not configured.");
        }

        if (string.IsNullOrWhiteSpace(request.Model))
        {
            return LLMResponse.Failure("Gemini model is required.");
        }

        var payload = BuildPayload(request);

        var (response, responseText) = await SendWithRetriesAsync(BuildEndpoint(request.Model), payload, cancellationToken);
        if (response is null)
        {
            return LLMResponse.Failure(responseText);
        }

        if (!response.IsSuccessStatusCode
            && IsTransientStatus(response.StatusCode)
            && HasFallbackModel(request.Model))
        {
            logger?.LogWarning(
                "Gemini model {Model} exhausted transient retries. Trying fallback model {FallbackModel}.",
                request.Model,
                options.FallbackModel);
            (response, responseText) = await SendWithRetriesAsync(BuildEndpoint(options.FallbackModel), payload, cancellationToken);
            if (response is null)
            {
                return LLMResponse.Failure(responseText);
            }
        }

        if (!response.IsSuccessStatusCode)
        {
            var statusCode = (int)response.StatusCode;
            if (IsTransientStatus(response.StatusCode))
            {
                return LLMResponse.Failure($"Gemini está temporalmente sobrecargado (HTTP {statusCode}) después de {MaxTransientRetries} reintentos. Intenta de nuevo en un momento.", statusCode);
            }

            return LLMResponse.Failure($"Gemini returned HTTP {statusCode}: {ExtractErrorMessage(responseText)}", statusCode);
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
                ? LLMResponse.Failure("Gemini response did not contain generated text.")
                : LLMResponse.Success(text, usage);
        }
        catch (JsonException exception)
        {
            return LLMResponse.Failure($"Gemini returned malformed JSON: {exception.Message}");
        }
    }

    public async IAsyncEnumerable<LLMStreamChunk> StreamGenerateAsync(
        PromptRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            yield return LLMStreamChunk.Failure("Gemini API key is not configured.");
            yield break;
        }

        if (string.IsNullOrWhiteSpace(request.Model))
        {
            yield return LLMStreamChunk.Failure("Gemini model is required.");
            yield break;
        }

        var payload = BuildPayload(request);
        var (response, errorText) = await SendStreamHeadersWithRetriesAsync(
            BuildStreamEndpoint(request.Model),
            payload,
            cancellationToken);

        if (response is null)
        {
            yield return LLMStreamChunk.Failure(errorText ?? "Gemini stream failed.");
            yield break;
        }

        if (!response.IsSuccessStatusCode
            && IsTransientStatus(response.StatusCode)
            && HasFallbackModel(request.Model))
        {
            response.Dispose();
            logger?.LogWarning(
                "Gemini stream model {Model} exhausted transient retries. Trying fallback model {FallbackModel}.",
                request.Model,
                options.FallbackModel);
            (response, errorText) = await SendStreamHeadersWithRetriesAsync(
                BuildStreamEndpoint(options.FallbackModel),
                payload,
                cancellationToken);
            if (response is null)
            {
                yield return LLMStreamChunk.Failure(errorText ?? "Gemini stream failed.");
                yield break;
            }
        }

        using var ownedResponse = response;
        if (!ownedResponse.IsSuccessStatusCode)
        {
            var statusCode = (int)ownedResponse.StatusCode;
            var body = await ownedResponse.Content.ReadAsStringAsync(cancellationToken);
            if (IsTransientStatus(ownedResponse.StatusCode))
            {
                yield return LLMStreamChunk.Failure(
                    $"Gemini está temporalmente sobrecargado (HTTP {statusCode}) después de {MaxTransientRetries} reintentos. Intenta de nuevo en un momento.",
                    statusCode);
            }
            else
            {
                yield return LLMStreamChunk.Failure(
                    $"Gemini returned HTTP {statusCode}: {ExtractErrorMessage(body)}",
                    statusCode);
            }

            yield break;
        }

        await using var stream = await ownedResponse.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);

        var functionCalls = new List<LLMFunctionCall>();
        LLMTokenUsage? latestUsage = null;
        var emittedAnyText = false;

        while (true)
        {
            var (line, readError) = await ReadNextLineSafeAsync(reader, cancellationToken);
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
            if (string.IsNullOrEmpty(dataJson) || dataJson == "[DONE]")
            {
                continue;
            }

            var parsed = ParseGeminiSseData(dataJson);
            if (parsed.Error is not null)
            {
                yield return LLMStreamChunk.Failure(parsed.Error);
                yield break;
            }

            if (parsed.Usage is not null)
            {
                latestUsage = parsed.Usage;
            }

            if (parsed.FunctionCalls.Count > 0)
            {
                functionCalls.AddRange(parsed.FunctionCalls);
            }

            if (!string.IsNullOrEmpty(parsed.TextDelta))
            {
                emittedAnyText = true;
                yield return LLMStreamChunk.Delta(parsed.TextDelta);
            }
        }

        if (functionCalls.Count > 0)
        {
            yield return LLMStreamChunk.ToolCalls(functionCalls, latestUsage);
            yield break;
        }

        if (!emittedAnyText)
        {
            yield return LLMStreamChunk.Failure("Gemini response did not contain generated text.");
            yield break;
        }

        if (latestUsage is not null)
        {
            yield return LLMStreamChunk.Completed(latestUsage);
        }
    }

    private static string BuildEndpoint(string model) =>
        $"https://generativelanguage.googleapis.com/v1beta/models/{Uri.EscapeDataString(model)}:generateContent";

    private static string BuildStreamEndpoint(string model) =>
        $"https://generativelanguage.googleapis.com/v1beta/models/{Uri.EscapeDataString(model)}:streamGenerateContent?alt=sse";

    private bool HasFallbackModel(string primaryModel) =>
        !string.IsNullOrWhiteSpace(options.FallbackModel)
        && !options.FallbackModel.Equals(primaryModel, StringComparison.OrdinalIgnoreCase);

    private static string ToGeminiRole(string role) =>
        role.Equals("assistant", StringComparison.OrdinalIgnoreCase) ? "model" : "user";

    private async Task<(HttpResponseMessage? Response, string? ErrorText)> SendStreamHeadersWithRetriesAsync(
        string endpoint,
        object payload,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt <= MaxTransientRetries; attempt++)
        {
            HttpResponseMessage response;
            try
            {
                using var httpRequest = new HttpRequestMessage(HttpMethod.Post, endpoint)
                {
                    Content = JsonContent.Create(payload, options: JsonOptions)
                };
                httpRequest.Headers.Add("x-goog-api-key", options.ApiKey);
                response = await httpClient.SendAsync(
                    httpRequest,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return (null, "Gemini request timed out.");
            }
            catch (HttpRequestException exception) when (attempt < MaxTransientRetries)
            {
                var rn = attempt + 1;
                var d = TimeSpan.FromSeconds(Math.Pow(2, rn - 1));
                logger?.LogWarning(
                    "Gemini stream network error: {ErrorMessage}. Retry {RetryNumber} of {MaxRetries} after {Delay}.",
                    exception.Message,
                    rn,
                    MaxTransientRetries,
                    d);
                await Task.Delay(d, cancellationToken);
                continue;
            }
            catch (HttpRequestException exception)
            {
                return (null, $"Gemini request failed after {MaxTransientRetries} retries: {exception.Message}");
            }

            if (!IsTransientStatus(response.StatusCode) || attempt == MaxTransientRetries)
            {
                return (response, null);
            }

            var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
            var retryNumber = attempt + 1;
            var delay = GetRetryDelay(response, responseText, retryNumber);
            logger?.LogWarning(
                "Gemini stream transient error HTTP {StatusCode}. Retry {RetryNumber} of {MaxRetries} after {Delay}.",
                (int)response.StatusCode,
                retryNumber,
                MaxTransientRetries,
                delay);
            response.Dispose();
            await Task.Delay(delay, cancellationToken);
        }

        return (null, "Gemini retry loop ended unexpectedly.");
    }

    private static async Task<(string? Line, string? Error)> ReadNextLineSafeAsync(
        StreamReader reader,
        CancellationToken cancellationToken)
    {
        try
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            return (line, null);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return (null, "Gemini stream timed out.");
        }
        catch (HttpRequestException exception)
        {
            return (null, $"Gemini stream interrupted: {exception.Message}");
        }
        catch (IOException exception) when (!cancellationToken.IsCancellationRequested)
        {
            return (null, $"Gemini stream I/O error: {exception.Message}");
        }
    }

    private static (string? TextDelta, IReadOnlyList<LLMFunctionCall> FunctionCalls, LLMTokenUsage? Usage, string? Error) ParseGeminiSseData(string dataJson)
    {
        try
        {
            using var document = JsonDocument.Parse(dataJson);
            var root = document.RootElement;
            var usage = ExtractUsage(root);
            var calls = ExtractFunctionCalls(root);
            var delta = ExtractStreamTextDelta(root);
            return (delta, calls, usage, null);
        }
        catch (JsonException exception)
        {
            return (null, [], null, $"Gemini returned malformed SSE JSON: {exception.Message}");
        }
    }

    private async Task<(HttpResponseMessage? Response, string ResponseText)> SendWithRetriesAsync(
        string endpoint,
        object payload,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt <= MaxTransientRetries; attempt++)
        {
            HttpResponseMessage response;
            try
            {
                using var httpRequest = new HttpRequestMessage(HttpMethod.Post, endpoint)
                {
                    Content = JsonContent.Create(payload, options: JsonOptions)
                };
                httpRequest.Headers.Add("x-goog-api-key", options.ApiKey);
                response = await httpClient.SendAsync(httpRequest, cancellationToken);
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return (null, "Gemini request timed out.");
            }
            catch (HttpRequestException exception) when (attempt < MaxTransientRetries)
            {
                var rn = attempt + 1;
                var d = TimeSpan.FromSeconds(Math.Pow(2, rn - 1));
                logger?.LogWarning(
                    "Gemini network error: {ErrorMessage}. Retry {RetryNumber} of {MaxRetries} after {Delay}.",
                    exception.Message,
                    rn,
                    MaxTransientRetries,
                    d);
                await Task.Delay(d, cancellationToken);
                continue;
            }
            catch (HttpRequestException exception)
            {
                return (null, $"Gemini request failed after {MaxTransientRetries} retries: {exception.Message}");
            }

            var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!IsTransientStatus(response.StatusCode) || attempt == MaxTransientRetries)
            {
                return (response, responseText);
            }

            var retryNumber = attempt + 1;
            var delay = GetRetryDelay(response, responseText, retryNumber);
            logger?.LogWarning(
                "Gemini transient error HTTP {StatusCode}. Retry {RetryNumber} of {MaxRetries} after {Delay}.",
                (int)response.StatusCode,
                retryNumber,
                MaxTransientRetries,
                delay);
            response.Dispose();
            await Task.Delay(delay, cancellationToken);
        }

        return (null, "Gemini retry loop ended unexpectedly.");
    }

    private static bool IsTransientStatus(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable;

    private static TimeSpan GetRetryDelay(HttpResponseMessage response, string responseText, int retryNumber)
    {
        if (response.Headers.RetryAfter?.Delta is { } delta)
        {
            return delta;
        }

        if (response.Headers.RetryAfter?.Date is { } date)
        {
            var delay = date - DateTimeOffset.UtcNow;
            if (delay > TimeSpan.Zero)
            {
                return delay;
            }
        }

        if (TryExtractRetryDelay(responseText, out var retryDelay))
        {
            return retryDelay;
        }

        return TimeSpan.FromSeconds(Math.Pow(2, retryNumber - 1));
    }

    private static bool TryExtractRetryDelay(string responseText, out TimeSpan retryDelay)
    {
        retryDelay = default;
        try
        {
            using var document = JsonDocument.Parse(responseText);
            return TryFindRetryDelay(document.RootElement, out retryDelay);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryFindRetryDelay(JsonElement element, out TimeSpan retryDelay)
    {
        retryDelay = default;
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty("retryDelay", out var retryDelayElement)
                && retryDelayElement.ValueKind == JsonValueKind.String
                && TryParseGoogleDuration(retryDelayElement.GetString(), out retryDelay))
            {
                return true;
            }

            foreach (var property in element.EnumerateObject())
            {
                if (TryFindRetryDelay(property.Value, out retryDelay))
                {
                    return true;
                }
            }
        }

        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                if (TryFindRetryDelay(item, out retryDelay))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool TryParseGoogleDuration(string? value, out TimeSpan duration)
    {
        duration = default;
        if (string.IsNullOrWhiteSpace(value) || !value.EndsWith('s'))
        {
            return false;
        }

        return double.TryParse(value[..^1], System.Globalization.CultureInfo.InvariantCulture, out var seconds)
            && seconds >= 0
            && (duration = TimeSpan.FromSeconds(seconds)) >= TimeSpan.Zero;
    }

    private static object BuildPayload(PromptRequest request)
    {
        var payload = new Dictionary<string, object?>
        {
            ["systemInstruction"] = new
            {
                parts = new[] { new { text = request.SystemPrompt } }
            },
            ["contents"] = request.Messages.Select(ToGeminiContent).ToArray()
        };

        if (request.Tools is { Count: > 0 })
        {
            payload["tools"] = new[]
            {
                new
                {
                    functionDeclarations = request.Tools.Select(tool => new
                    {
                        name = tool.Name,
                        description = tool.Description,
                        parameters = tool.Parameters
                    }).ToArray()
                }
            };
        }

        return payload;
    }

    private static object ToGeminiContent(LLMMessage message)
    {
        if (message.FunctionCalls is { Count: > 0 })
        {
            return new
            {
                role = "model",
                parts = message.FunctionCalls.Select(ToGeminiFunctionCallPart).ToArray()
            };
        }

        if (message.FunctionCall is not null)
        {
            return new
            {
                role = "model",
                parts = new[] { ToGeminiFunctionCallPart(message.FunctionCall) }
            };
        }

        if (message.FunctionResponses is { Count: > 0 })
        {
            return new
            {
                role = "user",
                parts = message.FunctionResponses.Select(ToGeminiFunctionResponsePart).ToArray()
            };
        }

        if (message.FunctionResponse is not null)
        {
            return new
            {
                role = "user",
                parts = new[] { ToGeminiFunctionResponsePart(message.FunctionResponse) }
            };
        }

        return new
        {
            role = ToGeminiRole(message.Role),
            parts = new object[] { new { text = message.Content ?? string.Empty } }
        };
    }

    private static object ToGeminiFunctionCallPart(LLMFunctionCall functionCall)
    {
        if (!string.IsNullOrWhiteSpace(functionCall.RawPartJson))
        {
            return JsonDocument.Parse(functionCall.RawPartJson).RootElement.Clone();
        }

        var part = new Dictionary<string, object?>
        {
            ["functionCall"] = new
            {
                name = functionCall.Name,
                args = functionCall.Args
            }
        };

        if (!string.IsNullOrWhiteSpace(functionCall.ThoughtSignature))
        {
            part["thoughtSignature"] = functionCall.ThoughtSignature;
        }

        return part;
    }

    private static object ToGeminiFunctionResponsePart(LLMFunctionResponse functionResponse) => new
    {
        functionResponse = new
        {
            name = functionResponse.Name,
            response = functionResponse.Response
        }
    };

    private static string ExtractGeneratedText(JsonElement root)
    {
        if (!root.TryGetProperty("candidates", out var candidates) || candidates.ValueKind != JsonValueKind.Array)
        {
            return string.Empty;
        }

        var parts = new List<string>();
        foreach (var candidate in candidates.EnumerateArray())
        {
            if (!candidate.TryGetProperty("content", out var content)
                || !content.TryGetProperty("parts", out var contentParts)
                || contentParts.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            parts.AddRange(contentParts.EnumerateArray()
                .Where(part => part.TryGetProperty("text", out _))
                .Select(part => part.GetProperty("text").GetString())
                .Where(text => !string.IsNullOrWhiteSpace(text))!);
        }

        return string.Join(Environment.NewLine, parts);
    }

    private static string ExtractStreamTextDelta(JsonElement root)
    {
        if (!root.TryGetProperty("candidates", out var candidates) || candidates.ValueKind != JsonValueKind.Array)
        {
            return string.Empty;
        }

        var builder = new System.Text.StringBuilder();
        foreach (var candidate in candidates.EnumerateArray())
        {
            if (!candidate.TryGetProperty("content", out var content)
                || !content.TryGetProperty("parts", out var contentParts)
                || contentParts.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var part in contentParts.EnumerateArray())
            {
                if (part.TryGetProperty("text", out var textEl) && textEl.ValueKind == JsonValueKind.String)
                {
                    builder.Append(textEl.GetString());
                }
            }
        }

        return builder.ToString();
    }

    private static IReadOnlyList<LLMFunctionCall> ExtractFunctionCalls(JsonElement root)
    {
        if (!root.TryGetProperty("candidates", out var candidates) || candidates.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var calls = new List<LLMFunctionCall>();
        foreach (var candidate in candidates.EnumerateArray())
        {
            if (!candidate.TryGetProperty("content", out var content)
                || !content.TryGetProperty("parts", out var parts)
                || parts.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var part in parts.EnumerateArray())
            {
                if (!part.TryGetProperty("functionCall", out var functionCall)
                    || !functionCall.TryGetProperty("name", out var nameElement))
                {
                    continue;
                }

                var name = nameElement.GetString();
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                var args = functionCall.TryGetProperty("args", out var argsElement)
                    ? JsonDocument.Parse(argsElement.GetRawText()).RootElement.Clone()
                    : JsonDocument.Parse("{}").RootElement.Clone();

                var thoughtSignature = TryGetThoughtSignature(part, functionCall);
                calls.Add(new LLMFunctionCall(
                    name,
                    args,
                    ThoughtSignature: thoughtSignature,
                    RawPartJson: part.GetRawText()));
            }
        }

        return calls;
    }

    private static LLMTokenUsage? ExtractUsage(JsonElement root)
    {
        if (!root.TryGetProperty("usageMetadata", out var usage) || usage.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var prompt = usage.TryGetProperty("promptTokenCount", out var p) && p.TryGetInt32(out var pv) ? pv : 0;
        var candidates = usage.TryGetProperty("candidatesTokenCount", out var c) && c.TryGetInt32(out var cv) ? cv : 0;
        var total = usage.TryGetProperty("totalTokenCount", out var t) && t.TryGetInt32(out var tv) ? tv : prompt + candidates;
        return new LLMTokenUsage(prompt, candidates, total);
    }

    private static string? TryGetThoughtSignature(JsonElement part, JsonElement functionCall)
    {
        if (part.TryGetProperty("thoughtSignature", out var camelCase)
            && camelCase.ValueKind == JsonValueKind.String)
        {
            return camelCase.GetString();
        }

        if (part.TryGetProperty("thought_signature", out var snakeCase)
            && snakeCase.ValueKind == JsonValueKind.String)
        {
            return snakeCase.GetString();
        }

        if (functionCall.TryGetProperty("thoughtSignature", out var functionCamelCase)
            && functionCamelCase.ValueKind == JsonValueKind.String)
        {
            return functionCamelCase.GetString();
        }

        if (functionCall.TryGetProperty("thought_signature", out var functionSnakeCase)
            && functionSnakeCase.ValueKind == JsonValueKind.String)
        {
            return functionSnakeCase.GetString();
        }

        return null;
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
