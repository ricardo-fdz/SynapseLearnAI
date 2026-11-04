using System.Net.Http.Json;
using System.Text.Json;
using LearningAgents.Application.Interfaces;
using LearningAgents.Infrastructure.LLM;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LearningAgents.Infrastructure.Services;

public sealed class GeminiTtsService(
    HttpClient httpClient,
    IOptions<GeminiOptions> options,
    IMemoryCache cache,
    ILogger<GeminiTtsService>? logger = null) : ITtsService
{
    private static readonly string[] PreferredModels = ["gemini-3.8-flash-lite-tts", "gemini-3.8-flash-tts"];

    public async Task<SynthesizeSpeechResult> SynthesizeAsync(string text, string? voice, CancellationToken cancellationToken = default)
    {
        var cleaned = text.Trim();
        if (string.IsNullOrWhiteSpace(cleaned))
        {
            return new SynthesizeSpeechResult(false, Array.Empty<byte>(), string.Empty, "El texto a sintetizar no puede estar vacío.");
        }

        var apiKey = options.Value.ApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return new SynthesizeSpeechResult(false, Array.Empty<byte>(), string.Empty, "Gemini API key is not configured.");
        }

        var cacheKey = $"tts:{voice ?? "default"}:{cleaned}";
        if (cache.TryGetValue(cacheKey, out (byte[] bytes, string mime) cached))
        {
            return new SynthesizeSpeechResult(true, cached.bytes, cached.mime);
        }

        var voiceName = string.IsNullOrWhiteSpace(voice) ? "Puck" : voice;
        var payload = new
        {
            contents = new[]
            {
                new
                {
                    parts = new object[]
                    {
                        new { text = cleaned }
                    }
                }
            },
            generationConfig = new
            {
                responseModalities = new[] { "AUDIO" },
                speechConfig = new
                {
                    voiceConfig = new
                    {
                        prebuiltVoiceConfig = new
                        {
                            voiceName
                        }
                    }
                }
            }
        };

        foreach (var model in PreferredModels)
        {
            try
            {
                var url = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={apiKey}";
                using var response = await httpClient.PostAsJsonAsync(url, payload, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    var err = await response.Content.ReadAsStringAsync(cancellationToken);
                    logger?.LogWarning("Gemini TTS request for {Model} returned {Status}: {Error}", model, response.StatusCode, err);
                    continue;
                }

                using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
                var root = doc.RootElement;
                if (!root.TryGetProperty("candidates", out var candidates) || candidates.GetArrayLength() == 0)
                {
                    continue;
                }

                var firstCandidate = candidates[0];
                if (!firstCandidate.TryGetProperty("content", out var content) ||
                    !content.TryGetProperty("parts", out var parts) ||
                    parts.GetArrayLength() == 0)
                {
                    continue;
                }

                foreach (var part in parts.EnumerateArray())
                {
                    if (part.TryGetProperty("inlineData", out var inlineData))
                    {
                        var mime = inlineData.GetProperty("mimeType").GetString() ?? "audio/wav";
                        var b64 = inlineData.GetProperty("data").GetString();
                        if (!string.IsNullOrEmpty(b64))
                        {
                            var bytes = Convert.FromBase64String(b64);
                            cache.Set(cacheKey, (bytes, mime), TimeSpan.FromHours(24));
                            return new SynthesizeSpeechResult(true, bytes, mime);
                        }
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger?.LogWarning(ex, "Gemini TTS error with model {Model}", model);
            }
        }

        return new SynthesizeSpeechResult(false, Array.Empty<byte>(), string.Empty, "No fue posible generar el audio con los modelos Gemini TTS disponibles.");
    }
}
