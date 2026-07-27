using LearningAgents.Application.Common;
using LearningAgents.Application.Dtos.MemoryEntries;
using LearningAgents.Application.Interfaces;
using LearningAgents.Domain.Entities;
using LearningAgents.Domain.Memory;
using LearningAgents.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LearningAgents.Application.Services;

internal sealed class MemoryEntryService(LearningAgentsDbContext dbContext) : IMemoryEntryService
{
    public async Task<IReadOnlyList<MemoryEntryResponse>> GetAllAsync(CancellationToken cancellationToken)
    {
        return await dbContext.MemoryEntries
            .AsNoTracking()
            .OrderBy(entry => entry.Id)
            .Select(entry => ToResponse(entry))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<MemoryEntryResponse>> GetByTutorIdAndKeysAsync(
        int tutorId,
        IReadOnlyCollection<string> keys,
        CancellationToken cancellationToken)
    {
        return await dbContext.MemoryEntries
            .AsNoTracking()
            .Where(entry => entry.TutorId == tutorId && keys.Contains(entry.Key))
            .OrderBy(entry => entry.Id)
            .Select(entry => ToResponse(entry))
            .ToListAsync(cancellationToken);
    }

    public async Task<MemoryEntryResponse?> GetByIdAsync(int id, CancellationToken cancellationToken)
    {
        var entry = await dbContext.MemoryEntries
            .AsNoTracking()
            .FirstOrDefaultAsync(entry => entry.Id == id, cancellationToken);

        return entry is null ? null : ToResponse(entry);
    }

    public async Task<ServiceResult<MemoryEntryResponse>> CreateAsync(CreateMemoryEntryRequest request, CancellationToken cancellationToken)
    {
        var validationError = await ValidateRequest(request.TutorId, request.Key, id: null, cancellationToken);
        if (validationError is not null)
        {
            return ServiceResult<MemoryEntryResponse>.Failure(validationError);
        }

        var now = DateTime.UtcNow;
        var entry = new MemoryEntry
        {
            TutorId = request.TutorId,
            Key = request.Key,
            ValueJson = request.ValueJson,
            SchemaVersion = request.SchemaVersion,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        dbContext.MemoryEntries.Add(entry);
        await dbContext.SaveChangesAsync(cancellationToken);

        return ServiceResult<MemoryEntryResponse>.Success(ToResponse(entry));
    }

    public async Task<ServiceResult<bool>> UpdateAsync(int id, UpdateMemoryEntryRequest request, CancellationToken cancellationToken)
    {
        var entry = await dbContext.MemoryEntries.FirstOrDefaultAsync(entry => entry.Id == id, cancellationToken);
        if (entry is null)
        {
            return ServiceResult<bool>.Failure("Not found");
        }

        var validationError = await ValidateRequest(request.TutorId, request.Key, id, cancellationToken);
        if (validationError is not null)
        {
            return ServiceResult<bool>.Failure(validationError);
        }

        entry.TutorId = request.TutorId;
        entry.Key = request.Key;
        entry.ValueJson = request.ValueJson;
        entry.SchemaVersion = request.SchemaVersion;
        entry.UpdatedAtUtc = DateTime.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);

        return ServiceResult<bool>.Success(true);
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken)
    {
        var entry = await dbContext.MemoryEntries.FirstOrDefaultAsync(entry => entry.Id == id, cancellationToken);
        if (entry is null)
        {
            return false;
        }

        dbContext.MemoryEntries.Remove(entry);
        await dbContext.SaveChangesAsync(cancellationToken);

        return true;
    }

    private async Task<string?> ValidateRequest(int tutorId, string key, int? id, CancellationToken cancellationToken)
    {
        if (!MemoryKeys.IsStandard(key))
        {
            return $"Key must be one of: {string.Join(", ", MemoryKeys.All)}.";
        }

        if (!await dbContext.Tutors.AnyAsync(tutor => tutor.Id == tutorId, cancellationToken))
        {
            return $"Tutor {tutorId} does not exist.";
        }

        var duplicateExists = await dbContext.MemoryEntries.AnyAsync(
            entry => entry.TutorId == tutorId && entry.Key == key && (!id.HasValue || entry.Id != id.Value),
            cancellationToken);
        if (duplicateExists)
        {
            return $"Tutor {tutorId} already has a MemoryEntry with key '{key}'.";
        }

        return null;
    }

    private static MemoryEntryResponse ToResponse(MemoryEntry entry) => new(
        entry.Id,
        entry.TutorId,
        entry.Key,
        entry.ValueJson,
        entry.SchemaVersion,
        entry.CreatedAtUtc,
        entry.UpdatedAtUtc);
}
