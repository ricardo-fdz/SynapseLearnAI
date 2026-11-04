namespace LearningAgents.Application.Interfaces;

public sealed record SynthesizeSpeechResult(bool Success, byte[] AudioBytes, string MimeType, string? ErrorMessage = null);

public interface ITtsService
{
    Task<SynthesizeSpeechResult> SynthesizeAsync(string text, string? voice, CancellationToken cancellationToken = default);
}
