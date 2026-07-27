using LearningAgents.Application.Dtos.MemoryChanges;
using LearningAgents.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace LearningAgents.Api.Controllers;

[ApiController]
[Route("api/memory-entries/{memoryEntryId:int}/memory-changes")]
public sealed class MemoryEntryMemoryChangesController(IMemoryChangeService memoryChangeService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedMemoryChangeHistoryResponse>> GetByMemoryEntryId(
        int memoryEntryId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await memoryChangeService.GetByMemoryEntryIdAsync(memoryEntryId, page, pageSize, cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }
}
