using LearningAgents.Application.Common;
using LearningAgents.Application.Dtos.StudySessions;
using LearningAgents.Application.Interfaces;
using LearningAgents.Domain.Entities;
using LearningAgents.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LearningAgents.Application.Services;

internal sealed class StudySessionService(LearningAgentsDbContext dbContext) : IStudySessionService
{
    public async Task<IReadOnlyList<StudySessionResponse>> GetAllAsync(CancellationToken cancellationToken)
    {
        return await dbContext.StudySessions
            .AsNoTracking()
            .OrderBy(session => session.Id)
            .Select(session => ToResponse(session))
            .ToListAsync(cancellationToken);
    }

    public async Task<StudySessionResponse?> GetByIdAsync(int id, CancellationToken cancellationToken)
    {
        var session = await dbContext.StudySessions
            .AsNoTracking()
            .FirstOrDefaultAsync(session => session.Id == id, cancellationToken);

        return session is null ? null : ToResponse(session);
    }

    public async Task<ServiceResult<StudySessionResponse>> CreateAsync(CreateStudySessionRequest request, CancellationToken cancellationToken)
    {
        if (!await dbContext.Tutors.AnyAsync(tutor => tutor.Id == request.TutorId, cancellationToken))
        {
            return ServiceResult<StudySessionResponse>.Failure($"Tutor {request.TutorId} does not exist.");
        }

        var now = DateTime.UtcNow;
        var session = new StudySession
        {
            TutorId = request.TutorId,
            Name = request.Name,
            Goal = request.Goal,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        dbContext.StudySessions.Add(session);
        await dbContext.SaveChangesAsync(cancellationToken);

        return ServiceResult<StudySessionResponse>.Success(ToResponse(session));
    }

    public async Task<ServiceResult<bool>> UpdateAsync(int id, UpdateStudySessionRequest request, CancellationToken cancellationToken)
    {
        var session = await dbContext.StudySessions.FirstOrDefaultAsync(session => session.Id == id, cancellationToken);
        if (session is null)
        {
            return ServiceResult<bool>.Failure("Not found");
        }

        if (!await dbContext.Tutors.AnyAsync(tutor => tutor.Id == request.TutorId, cancellationToken))
        {
            return ServiceResult<bool>.Failure($"Tutor {request.TutorId} does not exist.");
        }

        session.TutorId = request.TutorId;
        session.Name = request.Name;
        session.Goal = request.Goal;
        session.UpdatedAtUtc = DateTime.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);

        return ServiceResult<bool>.Success(true);
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken)
    {
        var session = await dbContext.StudySessions.FirstOrDefaultAsync(session => session.Id == id, cancellationToken);
        if (session is null)
        {
            return false;
        }

        dbContext.StudySessions.Remove(session);
        await dbContext.SaveChangesAsync(cancellationToken);

        return true;
    }

    private static StudySessionResponse ToResponse(StudySession session) => new(
        session.Id,
        session.TutorId,
        session.Name,
        session.Goal,
        session.CreatedAtUtc,
        session.UpdatedAtUtc);
}
