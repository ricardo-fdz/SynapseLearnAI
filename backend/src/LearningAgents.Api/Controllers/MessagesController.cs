using LearningAgents.Application.Dtos.Messages;
using LearningAgents.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace LearningAgents.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class MessagesController(IMessageService messageService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<MessageResponse>>> GetAll(CancellationToken cancellationToken)
    {
        var messages = await messageService.GetAllAsync(cancellationToken);
        return Ok(messages);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<MessageResponse>> GetById(int id, CancellationToken cancellationToken)
    {
        var message = await messageService.GetByIdAsync(id, cancellationToken);
        return message is null ? NotFound() : Ok(message);
    }

    [HttpPost]
    public async Task<ActionResult<MessageResponse>> Create(CreateMessageRequest request, CancellationToken cancellationToken)
    {
        var result = await messageService.CreateAsync(request, cancellationToken);
        if (!result.IsSuccess)
        {
            return ValidationProblem(result.ErrorMessage);
        }

        return CreatedAtAction(nameof(GetById), new { id = result.Data!.Id }, result.Data);
    }
}
