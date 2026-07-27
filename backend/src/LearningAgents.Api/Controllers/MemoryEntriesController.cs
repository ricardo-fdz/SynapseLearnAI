using LearningAgents.Application.Dtos.MemoryEntries;
using LearningAgents.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace LearningAgents.Api.Controllers;

[ApiController]
[Route("api/memory-entries")]
public sealed class MemoryEntriesController(IMemoryEntryService memoryEntryService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<MemoryEntryResponse>>> GetAll(CancellationToken cancellationToken)
    {
        var entries = await memoryEntryService.GetAllAsync(cancellationToken);
        return Ok(entries);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<MemoryEntryResponse>> GetById(int id, CancellationToken cancellationToken)
    {
        var entry = await memoryEntryService.GetByIdAsync(id, cancellationToken);
        return entry is null ? NotFound() : Ok(entry);
    }

    [HttpPost]
    public async Task<IActionResult> Create(CreateMemoryEntryRequest request, CancellationToken cancellationToken)
    {
        var result = await memoryEntryService.CreateAsync(request, cancellationToken);
        if (!result.IsSuccess)
        {
            if (result.ErrorMessage!.Contains("already has"))
            {
                return Conflict(result.ErrorMessage);
            }
            return ValidationProblem(result.ErrorMessage);
        }

        return CreatedAtAction(nameof(GetById), new { id = result.Data!.Id }, result.Data);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, UpdateMemoryEntryRequest request, CancellationToken cancellationToken)
    {
        var result = await memoryEntryService.UpdateAsync(id, request, cancellationToken);
        if (!result.IsSuccess)
        {
            if (result.ErrorMessage == "Not found")
            {
                return NotFound();
            }
            if (result.ErrorMessage!.Contains("already has"))
            {
                return Conflict(result.ErrorMessage);
            }
            return ValidationProblem(result.ErrorMessage);
        }

        return NoContent();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        var deleted = await memoryEntryService.DeleteAsync(id, cancellationToken);
        return deleted ? NoContent() : NotFound();
    }
}
