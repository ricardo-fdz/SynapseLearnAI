using LearningAgents.Application.Dtos.MemoryChanges;
using LearningAgents.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace LearningAgents.Api.Controllers;

[ApiController]
[Route("api/memory-changes")]
public sealed class MemoryChangesController(IMemoryChangeService memoryChangeService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<MemoryChangeResponse>>> GetAll(CancellationToken cancellationToken)
    {
        var changes = await memoryChangeService.GetAllAsync(cancellationToken);
        return Ok(changes);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<MemoryChangeResponse>> GetById(int id, CancellationToken cancellationToken)
    {
        var change = await memoryChangeService.GetByIdAsync(id, cancellationToken);
        return change is null ? NotFound() : Ok(change);
    }
}
