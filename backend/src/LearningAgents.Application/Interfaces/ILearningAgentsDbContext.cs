using LearningAgents.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace LearningAgents.Application.Interfaces;

public interface ILearningAgentsDbContext
{
    DbSet<Tutor> Tutors { get; }
    DbSet<StudySession> StudySessions { get; }
    DbSet<Message> Messages { get; }
    DbSet<MemoryEntry> MemoryEntries { get; }
    DbSet<MemoryChange> MemoryChanges { get; }
    Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default);
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
