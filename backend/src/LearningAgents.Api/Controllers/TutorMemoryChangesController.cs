using LearningAgents.Application.Dtos.MemoryChanges;
using LearningAgents.Application.Dtos.MemoryPatches;
using LearningAgents.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace LearningAgents.Api.Controllers;

[ApiController]
[Route("api/tutors/{tutorId:int}/memory-changes")]
public sealed class TutorMemoryChangesController(IMemoryChangeService memoryChangeService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedMemoryChangeHistoryResponse>> GetByTutorId(
        int tutorId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await memoryChangeService.GetByTutorIdAsync(tutorId, page, pageSize, cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpPost("{changeId:int}/rollback")]
    public async Task<ActionResult<MemoryPatchResult>> Rollback(
        int tutorId,
        int changeId,
        CancellationToken cancellationToken = default)
    {
        var result = await memoryChangeService.RollbackAsync(tutorId, changeId, cancellationToken);
        if (!result.IsSuccess)
        {
            if (result.StatusCode == 404)
            {
                return NotFound();
            }

            return Problem(
                detail: result.ErrorMessage,
                statusCode: result.StatusCode ?? 400);
        }

        return Ok(result.Data);
    }
}
