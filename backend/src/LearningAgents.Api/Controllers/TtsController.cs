using LearningAgents.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace LearningAgents.Api.Controllers;

public sealed record SynthesizeSpeechRequest(string Text, string? Voice = null);

[ApiController]
[Route("api/tts")]
public sealed class TtsController(ITtsService ttsService) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Synthesize([FromBody] SynthesizeSpeechRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request?.Text))
        {
            return BadRequest(new { error = "El texto no puede estar vacío." });
        }

        if (request.Text.Length > 2000)
        {
            return BadRequest(new { error = "El texto excede el límite máximo de 2000 caracteres." });
        }

        var result = await ttsService.SynthesizeAsync(request.Text, request.Voice, cancellationToken);
        if (!result.Success)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = result.ErrorMessage });
        }

        return File(result.AudioBytes, result.MimeType);
    }
}
