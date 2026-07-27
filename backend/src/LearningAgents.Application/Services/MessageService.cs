using LearningAgents.Application.Common;
using LearningAgents.Application.Dtos.Messages;
using LearningAgents.Application.Interfaces;
using LearningAgents.Domain.Entities;
using LearningAgents.Domain.Enums;
using LearningAgents.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LearningAgents.Application.Services;

internal sealed class MessageService(LearningAgentsDbContext dbContext) : IMessageService
{
    public async Task<IReadOnlyList<MessageResponse>> GetAllAsync(CancellationToken cancellationToken)
    {
        return await dbContext.Messages
            .AsNoTracking()
            .OrderBy(message => message.Id)
            .Select(message => ToResponse(message))
            .ToListAsync(cancellationToken);
    }

    public async Task<MessageResponse?> GetByIdAsync(int id, CancellationToken cancellationToken)
    {
        var message = await dbContext.Messages
            .AsNoTracking()
            .FirstOrDefaultAsync(message => message.Id == id, cancellationToken);

        return message is null ? null : ToResponse(message);
    }

    public async Task<PagedResult<MessageResponse>?> GetBySessionPagedAsync(
        int sessionId,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        if (!await dbContext.StudySessions.AsNoTracking().AnyAsync(session => session.Id == sessionId, cancellationToken))
        {
            return null;
        }

        var normalizedPage = Math.Max(1, page);
        var normalizedPageSize = Math.Clamp(pageSize, 1, 100);
        var query = dbContext.Messages
            .AsNoTracking()
            .Where(message => message.SessionId == sessionId);

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(message => message.CreatedAtUtc)
            .ThenByDescending(message => message.Id)
            .Skip((normalizedPage - 1) * normalizedPageSize)
            .Take(normalizedPageSize)
            .Select(message => ToResponse(message))
            .ToListAsync(cancellationToken);

        return new PagedResult<MessageResponse>(normalizedPage, normalizedPageSize, totalCount, items);
    }

    public async Task<ServiceResult<MessageResponse>> CreateAsync(CreateMessageRequest request, CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<MessageRole>(request.Role, ignoreCase: true, out var role))
        {
            return ServiceResult<MessageResponse>.Failure("Role must be one of: user, assistant, system, tool.");
        }

        if (!await dbContext.StudySessions.AnyAsync(session => session.Id == request.SessionId, cancellationToken))
        {
            return ServiceResult<MessageResponse>.Failure($"StudySession {request.SessionId} does not exist.");
        }

        var message = new Message
        {
            SessionId = request.SessionId,
            Role = role,
            Content = request.Content,
            CreatedAtUtc = DateTime.UtcNow
        };

        dbContext.Messages.Add(message);
        await dbContext.SaveChangesAsync(cancellationToken);

        return ServiceResult<MessageResponse>.Success(ToResponse(message));
    }

    private static MessageResponse ToResponse(Message message) => new(
        message.Id,
        message.SessionId,
        message.Role.ToString().ToLowerInvariant(),
        message.Content,
        message.CreatedAtUtc);
}
