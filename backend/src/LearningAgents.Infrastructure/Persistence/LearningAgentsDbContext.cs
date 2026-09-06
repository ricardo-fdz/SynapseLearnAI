using LearningAgents.Domain.Entities;
using LearningAgents.Domain.Enums;
using LearningAgents.Domain.Memory;
using Microsoft.EntityFrameworkCore;

namespace LearningAgents.Infrastructure.Persistence;

public sealed class LearningAgentsDbContext(DbContextOptions<LearningAgentsDbContext> options) : DbContext(options)
{
    public DbSet<Tutor> Tutors => Set<Tutor>();
    public DbSet<StudySession> StudySessions => Set<StudySession>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<MemoryEntry> MemoryEntries => Set<MemoryEntry>();
    public DbSet<MemoryChange> MemoryChanges => Set<MemoryChange>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var seedCreatedAtUtc = new DateTime(2026, 6, 24, 0, 0, 0, DateTimeKind.Utc);

        modelBuilder.Entity<Tutor>(entity =>
        {
            entity.HasKey(tutor => tutor.Id);
            entity.Property(tutor => tutor.Name).HasMaxLength(200).IsRequired();
            entity.Property(tutor => tutor.Description).HasMaxLength(1000).IsRequired();
            entity.Property(tutor => tutor.SystemPromptContent).IsRequired();
            entity.Property(tutor => tutor.GeminiModel).HasMaxLength(100).IsRequired();
            entity.Property(tutor => tutor.LlmProfile).HasMaxLength(100).IsRequired().HasDefaultValue("gemini-default");
            entity.Property(tutor => tutor.CreatedAtUtc).IsRequired();
            entity.Property(tutor => tutor.UpdatedAtUtc).IsRequired();

            entity.HasData(new Tutor
            {
                Id = 1,
                Name = "Programming Tutor",
                Description = "Tutor de ejemplo para aprendizaje guiado de programacion.",
                SystemPromptContent = "Actua como un tutor de programacion socratico y enfocado en aprendizaje real.",
                GeminiModel = "gemini-2.5-flash",
                LlmProfile = "gemini-default",
                CreatedAtUtc = seedCreatedAtUtc,
                UpdatedAtUtc = seedCreatedAtUtc
            });
        });

        modelBuilder.Entity<StudySession>(entity =>
        {
            entity.HasKey(session => session.Id);
            entity.Property(session => session.Name).HasMaxLength(200).IsRequired();
            entity.Property(session => session.Goal).HasMaxLength(1000).IsRequired();
            entity.Property(session => session.CreatedAtUtc).IsRequired();
            entity.Property(session => session.UpdatedAtUtc).IsRequired();

            entity.HasOne(session => session.Tutor)
                .WithMany(tutor => tutor.StudySessions)
                .HasForeignKey(session => session.TutorId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(session => session.TutorId);
        });

        modelBuilder.Entity<Message>(entity =>
        {
            entity.HasKey(message => message.Id);
            entity.Property(message => message.Role)
                .HasConversion(
                    role => role.ToString().ToLowerInvariant(),
                    value => Enum.Parse<MessageRole>(value, ignoreCase: true))
                .HasMaxLength(20)
                .IsRequired();
            entity.Property(message => message.Content).IsRequired();
            entity.Property(message => message.CreatedAtUtc).IsRequired();

            entity.HasOne(message => message.Session)
                .WithMany(session => session.Messages)
                .HasForeignKey(message => message.SessionId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(message => message.SessionId);
        });

        modelBuilder.Entity<MemoryEntry>(entity =>
        {
            entity.HasKey(memoryEntry => memoryEntry.Id);
            entity.Property(memoryEntry => memoryEntry.Key).HasMaxLength(100).IsRequired();
            entity.Property(memoryEntry => memoryEntry.ValueJson).IsRequired();
            entity.Property(memoryEntry => memoryEntry.SchemaVersion).IsRequired();
            entity.Property(memoryEntry => memoryEntry.CreatedAtUtc).IsRequired();
            entity.Property(memoryEntry => memoryEntry.UpdatedAtUtc).IsRequired();
            entity.Property(memoryEntry => memoryEntry.RowVersion).IsConcurrencyToken().ValueGeneratedOnAddOrUpdate();

            entity.HasOne(memoryEntry => memoryEntry.Tutor)
                .WithMany(tutor => tutor.MemoryEntries)
                .HasForeignKey(memoryEntry => memoryEntry.TutorId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(memoryEntry => new { memoryEntry.TutorId, memoryEntry.Key }).IsUnique();

            entity.HasData(
                new MemoryEntry
                {
                    Id = 1,
                    TutorId = 1,
                    Key = MemoryKeys.SessionMemory,
                    ValueJson = MemoryEntryDefaults.SessionMemoryJson,
                    SchemaVersion = 1,
                    CreatedAtUtc = seedCreatedAtUtc,
                    UpdatedAtUtc = seedCreatedAtUtc
                },
                new MemoryEntry
                {
                    Id = 2,
                    TutorId = 1,
                    Key = MemoryKeys.StudentProfile,
                    ValueJson = MemoryEntryDefaults.StudentProfileJson,
                    SchemaVersion = 1,
                    CreatedAtUtc = seedCreatedAtUtc,
                    UpdatedAtUtc = seedCreatedAtUtc
                },
                new MemoryEntry
                {
                    Id = 3,
                    TutorId = 1,
                    Key = MemoryKeys.DomainMap,
                    ValueJson = MemoryEntryDefaults.DomainMapJsonTemas,
                    SchemaVersion = 1,
                    CreatedAtUtc = seedCreatedAtUtc,
                    UpdatedAtUtc = seedCreatedAtUtc
                },
                new MemoryEntry
                {
                    Id = 4,
                    TutorId = 1,
                    Key = MemoryKeys.GapsOrErrors,
                    ValueJson = MemoryEntryDefaults.GapsOrErrorsJson,
                    SchemaVersion = 1,
                    CreatedAtUtc = seedCreatedAtUtc,
                    UpdatedAtUtc = seedCreatedAtUtc
                },
                new MemoryEntry
                {
                    Id = 5,
                    TutorId = 1,
                    Key = MemoryKeys.ActivityHistory,
                    ValueJson = MemoryEntryDefaults.ActivityHistoryJson,
                    SchemaVersion = 1,
                    CreatedAtUtc = seedCreatedAtUtc,
                    UpdatedAtUtc = seedCreatedAtUtc
                },
                new MemoryEntry
                {
                    Id = 100,
                    TutorId = 1,
                    Key = MemoryKeys.Roadmap,
                    ValueJson = MemoryEntryDefaults.RoadmapJson,
                    SchemaVersion = 1,
                    CreatedAtUtc = seedCreatedAtUtc,
                    UpdatedAtUtc = seedCreatedAtUtc
                });
        });

        modelBuilder.Entity<MemoryChange>(entity =>
        {
            entity.HasKey(memoryChange => memoryChange.Id);
            entity.Property(memoryChange => memoryChange.Operation)
                .HasConversion(
                    operation => operation.ToString(),
                    value => Enum.Parse<MemoryPatchOperation>(value, ignoreCase: true))
                .HasMaxLength(20)
                .IsRequired();
            entity.Property(memoryChange => memoryChange.Path).HasMaxLength(500).IsRequired();
            entity.Property(memoryChange => memoryChange.TargetId).HasMaxLength(200).IsRequired();
            entity.Property(memoryChange => memoryChange.PreviousValueJson).IsRequired();
            entity.Property(memoryChange => memoryChange.NewValueJson).IsRequired();
            entity.Property(memoryChange => memoryChange.Reason).HasMaxLength(1000).IsRequired();
            entity.Property(memoryChange => memoryChange.CreatedAtUtc).IsRequired();

            entity.HasOne(memoryChange => memoryChange.MemoryEntry)
                .WithMany(memoryEntry => memoryEntry.MemoryChanges)
                .HasForeignKey(memoryChange => memoryChange.MemoryEntryId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(memoryChange => memoryChange.Message)
                .WithMany(message => message.MemoryChanges)
                .HasForeignKey(memoryChange => memoryChange.MessageId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasIndex(memoryChange => memoryChange.MemoryEntryId);
            entity.HasIndex(memoryChange => memoryChange.MessageId);
        });
    }
}
