using LearningAgents.Application.Dtos.Tutors;
using LearningAgents.Application.Enums;
using LearningAgents.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace LearningAgents.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class TutorsController(
    ITutorService tutorService,
    IPromptBuilder promptBuilder) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<TutorResponse>>> GetAll(CancellationToken cancellationToken)
    {
        var tutors = await tutorService.GetAllAsync(cancellationToken);
        return Ok(tutors);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<TutorResponse>> GetById(int id, CancellationToken cancellationToken)
    {
        var tutor = await tutorService.GetByIdAsync(id, cancellationToken);
        return tutor is null ? NotFound() : Ok(tutor);
    }

    // Diagnostic endpoint for Sprint 3 Prompt Builder visual verification only.
    [HttpGet("{id:int}/prompt-preview")]
    public async Task<IActionResult> GetPromptPreview(
        int id,
        [FromQuery] ContextLoadProfile profile = ContextLoadProfile.Standard,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var prompt = await promptBuilder.BuildSystemPromptAsync(id, profile, cancellationToken);
            return Content(prompt, "text/plain");
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    [HttpPost]
    public async Task<ActionResult<TutorResponse>> Create(CreateTutorRequest request, CancellationToken cancellationToken)
    {
        var tutor = await tutorService.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = tutor.Id }, tutor);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, UpdateTutorRequest request, CancellationToken cancellationToken)
    {
        var updated = await tutorService.UpdateAsync(id, request, cancellationToken);
        return updated ? NoContent() : NotFound();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        var deleted = await tutorService.DeleteAsync(id, cancellationToken);
        return deleted ? NoContent() : NotFound();
    }
}
