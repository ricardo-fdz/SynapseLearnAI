using LearningAgents.Application.Dtos.MemoryChanges;
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
}
