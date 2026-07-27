using LearningAgents.Application.Common;
using LearningAgents.Application.Dtos.Conversations;
using LearningAgents.Application.Dtos.Messages;
using LearningAgents.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace LearningAgents.Api.Controllers;

[ApiController]
[Route("api/sessions/{sessionId:int}/messages")]
public sealed class SessionMessagesController(
    IConversationService conversationService,
    IMessageService messageService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<MessageResponse>>> GetBySession(
        int sessionId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        var result = await messageService.GetBySessionPagedAsync(sessionId, page, pageSize, cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpPost]
    public async Task<ActionResult<ConversationMessageResponse>> Create(
        int sessionId,
        CreateConversationMessageRequest request,
        CancellationToken cancellationToken)
    {
        var result = await conversationService.SendMessageAsync(sessionId, request, cancellationToken);
        if (!result.IsSuccess)
        {
            return result.ErrorMessage == "Not found"
                ? NotFound()
                : Problem(result.ErrorMessage, statusCode: StatusCodes.Status502BadGateway);
        }

        return Ok(result.Data);
    }
}
