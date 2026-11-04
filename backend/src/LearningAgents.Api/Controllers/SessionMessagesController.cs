using System.Text;
using System.Text.Json;
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
    private static readonly JsonSerializerOptions SseJsonOptions = new(JsonSerializerDefaults.Web);

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
            if (result.StatusCode == StatusCodes.Status404NotFound && result.ErrorMessage == "Not found")
            {
                return NotFound();
            }

            var statusCode = result.StatusCode ?? StatusCodes.Status502BadGateway;
            var problem = new ProblemDetails
            {
                Status = statusCode,
                Title = "Conversation turn failed",
                Detail = result.ErrorMessage
            };

            if (result.ErrorDetails is ConversationFailureDetails failureDetails)
            {
                problem.Extensions["userMessage"] = failureDetails.UserMessage;
                problem.Extensions["appliedMemoryChanges"] = failureDetails.AppliedMemoryChanges;
            }

            return StatusCode(statusCode, problem);
        }

        return Ok(result.Data);
    }

    [HttpPost("stream")]
    public async Task Stream(
        int sessionId,
        CreateConversationMessageRequest request,
        CancellationToken cancellationToken)
    {
        Response.Headers.ContentType = "text/event-stream; charset=utf-8";
        Response.Headers.CacheControl = "no-cache";
        Response.Headers["X-Accel-Buffering"] = "no";

        await foreach (var evt in conversationService.StreamMessageAsync(sessionId, request, cancellationToken))
        {
            var payloadJson = JsonSerializer.Serialize(evt.Payload, SseJsonOptions);
            var sseFrame = $"event: {evt.Type}\ndata: {payloadJson}\n\n";
            var bytes = Encoding.UTF8.GetBytes(sseFrame);
            await Response.Body.WriteAsync(bytes, cancellationToken);
            await Response.Body.FlushAsync(cancellationToken);
        }
    }
}
