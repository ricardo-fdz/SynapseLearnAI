using LearningAgents.Application.Dtos.MemoryChanges;
using LearningAgents.Application.Interfaces;
using LearningAgents.Domain.Entities;
using LearningAgents.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LearningAgents.Application.Services;

internal sealed class MemoryChangeService(LearningAgentsDbContext dbContext) : IMemoryChangeService
{
    public async Task<IReadOnlyList<MemoryChangeResponse>> GetAllAsync(CancellationToken cancellationToken)
    {
        return await dbContext.MemoryChanges
            .AsNoTracking()
            .OrderBy(change => change.Id)
            .Select(change => ToResponse(change))
            .ToListAsync(cancellationToken);
    }

    public async Task<MemoryChangeResponse?> GetByIdAsync(int id, CancellationToken cancellationToken)
    {
        var change = await dbContext.MemoryChanges
            .AsNoTracking()
            .FirstOrDefaultAsync(change => change.Id == id, cancellationToken);

        return change is null ? null : ToResponse(change);
    }

    public async Task<PagedMemoryChangeHistoryResponse?> GetByTutorIdAsync(
        int tutorId,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        if (!await dbContext.Tutors.AsNoTracking().AnyAsync(tutor => tutor.Id == tutorId, cancellationToken))
        {
            return null;
        }

        var (normalizedPage, normalizedPageSize) = NormalizePagination(page, pageSize);
        var query = dbContext.MemoryChanges
            .AsNoTracking()
            .Where(change => change.MemoryEntry.TutorId == tutorId);

        return await ToPagedResponseAsync(query, normalizedPage, normalizedPageSize, cancellationToken);
    }

    public async Task<PagedMemoryChangeHistoryResponse?> GetByMemoryEntryIdAsync(
        int memoryEntryId,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        if (!await dbContext.MemoryEntries.AsNoTracking().AnyAsync(entry => entry.Id == memoryEntryId, cancellationToken))
        {
            return null;
        }

        var (normalizedPage, normalizedPageSize) = NormalizePagination(page, pageSize);
        var query = dbContext.MemoryChanges
            .AsNoTracking()
            .Where(change => change.MemoryEntryId == memoryEntryId);

        return await ToPagedResponseAsync(query, normalizedPage, normalizedPageSize, cancellationToken);
    }

    private static (int Page, int PageSize) NormalizePagination(int page, int pageSize) =>
        (Math.Max(1, page), Math.Clamp(pageSize, 1, 100));

    private static async Task<PagedMemoryChangeHistoryResponse> ToPagedResponseAsync(
        IQueryable<MemoryChange> query,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .Include(change => change.MemoryEntry)
            .OrderByDescending(change => change.CreatedAtUtc)
            .ThenByDescending(change => change.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(change => ToHistoryResponse(change))
            .ToListAsync(cancellationToken);

        return new PagedMemoryChangeHistoryResponse(page, pageSize, totalCount, items);
    }

    private static MemoryChangeResponse ToResponse(MemoryChange change) => new(
        change.Id,
        change.MemoryEntryId,
        change.MessageId,
        change.Operation.ToString(),
        change.Path,
        change.TargetId,
        change.PreviousValueJson,
        change.NewValueJson,
        change.Reason,
        change.CreatedAtUtc);

    private static MemoryChangeHistoryResponse ToHistoryResponse(MemoryChange change) => new(
        change.Id,
        change.MemoryEntryId,
        change.MemoryEntry.Key,
        change.MessageId,
        change.Operation.ToString(),
        change.Path,
        change.TargetId,
        change.PreviousValueJson,
        change.NewValueJson,
        change.Reason,
        change.CreatedAtUtc);
}
