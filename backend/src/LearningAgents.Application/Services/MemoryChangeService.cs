using System.Text.Json;
using System.Text.Json.Nodes;
using LearningAgents.Application.Common;
using LearningAgents.Application.Dtos.MemoryChanges;
using LearningAgents.Application.Dtos.MemoryEntries;
using LearningAgents.Application.Dtos.MemoryPatches;
using LearningAgents.Application.Interfaces;
using LearningAgents.Domain.Entities;
using LearningAgents.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace LearningAgents.Application.Services;

internal sealed class MemoryChangeService(ILearningAgentsDbContext dbContext) : IMemoryChangeService
{
    public async Task<IReadOnlyList<MemoryChangeResponse>> GetAllAsync(int limit, CancellationToken cancellationToken)
    {
        var normalizedLimit = Math.Clamp(limit, 1, 500);
        return await dbContext.MemoryChanges
            .AsNoTracking()
            .OrderByDescending(change => change.Id)
            .Take(normalizedLimit)
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

    public async Task<ServiceResult<MemoryPatchResult>> RollbackAsync(
        int tutorId,
        int changeId,
        CancellationToken cancellationToken)
    {
        var change = await dbContext.MemoryChanges
            .Include(item => item.MemoryEntry)
            .FirstOrDefaultAsync(
                item => item.Id == changeId && item.MemoryEntry.TutorId == tutorId,
                cancellationToken);
        if (change is null)
        {
            return ServiceResult<MemoryPatchResult>.Failure("El cambio de memoria no existe para este tutor.", 404);
        }

        try
        {
            if (JsonNode.Parse(change.PreviousValueJson) is not JsonObject)
            {
                return ServiceResult<MemoryPatchResult>.Failure("El valor previo del cambio no es un objeto JSON válido.", 400);
            }
        }
        catch (JsonException exception)
        {
            return ServiceResult<MemoryPatchResult>.Failure($"JSON previo inválido: {exception.Message}", 400);
        }

        await using var transaction = await dbContext.BeginTransactionAsync(cancellationToken);
        var now = DateTime.UtcNow;
        var entry = change.MemoryEntry;
        var beforeRollbackJson = entry.ValueJson;

        entry.ValueJson = change.PreviousValueJson;
        entry.UpdatedAtUtc = now;

        var rollbackChange = new MemoryChange
        {
            MemoryEntryId = entry.Id,
            MessageId = null,
            Operation = MemoryPatchOperation.Set,
            Path = "/",
            TargetId = string.Empty,
            PreviousValueJson = beforeRollbackJson,
            NewValueJson = change.PreviousValueJson,
            Reason = $"Rollback manual al estado previo del cambio #{changeId}",
            CreatedAtUtc = now
        };

        dbContext.MemoryChanges.Add(rollbackChange);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(cancellationToken);
            return ServiceResult<MemoryPatchResult>.Failure(
                "Conflicto de concurrencia al revertir la memoria. Recarga e intenta de nuevo.",
                409);
        }

        return ServiceResult<MemoryPatchResult>.Success(
            new MemoryPatchResult(ToMemoryEntryResponse(entry), ToResponse(rollbackChange)));
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

    private static MemoryEntryResponse ToMemoryEntryResponse(MemoryEntry entry) => new(
        entry.Id,
        entry.TutorId,
        entry.Key,
        entry.ValueJson,
        entry.SchemaVersion,
        entry.CreatedAtUtc,
        entry.UpdatedAtUtc);

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
