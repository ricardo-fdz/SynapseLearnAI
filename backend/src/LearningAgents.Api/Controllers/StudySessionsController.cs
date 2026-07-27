using LearningAgents.Application.Dtos.StudySessions;
using LearningAgents.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace LearningAgents.Api.Controllers;

[ApiController]
[Route("api/study-sessions")]
public sealed class StudySessionsController(IStudySessionService studySessionService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<StudySessionResponse>>> GetAll(CancellationToken cancellationToken)
    {
        var sessions = await studySessionService.GetAllAsync(cancellationToken);
        return Ok(sessions);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<StudySessionResponse>> GetById(int id, CancellationToken cancellationToken)
    {
        var session = await studySessionService.GetByIdAsync(id, cancellationToken);
        return session is null ? NotFound() : Ok(session);
    }

    [HttpPost]
    public async Task<ActionResult<StudySessionResponse>> Create(CreateStudySessionRequest request, CancellationToken cancellationToken)
    {
        var result = await studySessionService.CreateAsync(request, cancellationToken);
        if (!result.IsSuccess)
        {
            return ValidationProblem(result.ErrorMessage);
        }

        return CreatedAtAction(nameof(GetById), new { id = result.Data!.Id }, result.Data);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, UpdateStudySessionRequest request, CancellationToken cancellationToken)
    {
        var result = await studySessionService.UpdateAsync(id, request, cancellationToken);
        if (!result.IsSuccess)
        {
            return result.ErrorMessage == "Not found" ? NotFound() : ValidationProblem(result.ErrorMessage);
        }

        return NoContent();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        var deleted = await studySessionService.DeleteAsync(id, cancellationToken);
        return deleted ? NoContent() : NotFound();
    }
}
