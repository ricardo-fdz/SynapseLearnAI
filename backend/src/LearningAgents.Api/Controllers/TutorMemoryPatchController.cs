using LearningAgents.Application.Dtos.MemoryPatches;
using LearningAgents.Application.Interfaces;
using LearningAgents.Domain.Memory;
using Microsoft.AspNetCore.Mvc;

namespace LearningAgents.Api.Controllers;

[ApiController]
[Route("api/tutors/{id:int}/memory-patch")]
public sealed class TutorMemoryPatchController(IMemoryPatchEngine memoryPatchEngine) : ControllerBase
{
    // Diagnostic endpoint for Sprint 5 manual verification only.
    [HttpPost]
    public async Task<ActionResult<MemoryPatchResult>> Apply(
        int id,
        MemoryPatch patch,
        [FromQuery] int? messageId,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await memoryPatchEngine.ApplyPatchAsync(id, patch, messageId, cancellationToken);
            return Ok(result);
        }
        catch (InvalidMemoryPatchException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
    }
}
