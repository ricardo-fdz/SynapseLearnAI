using LearningAgents.Api.Controllers;
using LearningAgents.Application;
using LearningAgents.Application.Dtos.MemoryChanges;
using LearningAgents.Application.Dtos.MemoryPatches;
using LearningAgents.Application.Interfaces;
using LearningAgents.Domain.Entities;
using LearningAgents.Domain.Enums;
using LearningAgents.Domain.Memory;
using LearningAgents.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LearningAgents.Tests;

public sealed class MemoryChangeHistoryEndpointTests
{
    [Fact]
    public async Task TutorMemoryChangesEndpoint_FiltersByTutorAndOrdersDescending()
    {
        await using var fixture = await HistoryFixture.CreateAsync();
        var controller = new TutorMemoryChangesController(fixture.MemoryChangeService);

        var actionResult = await controller.GetByTutorId(1, page: 1, pageSize: 20);
        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var response = Assert.IsType<PagedMemoryChangeHistoryResponse>(okResult.Value);

        Assert.Equal(3, response.TotalCount);
        Assert.Equal([3, 2, 1], response.Items.Select(item => item.Id).ToArray());
        Assert.All(response.Items, item => Assert.True(
            new[] { MemoryKeys.SessionMemory, MemoryKeys.DomainMap }.Contains(item.MemoryEntryKey),
            $"Unexpected memory key: {item.MemoryEntryKey}"));
        Assert.DoesNotContain(response.Items, item => item.Id == 4);
    }

    [Fact]
    public async Task MemoryEntryMemoryChangesEndpoint_FiltersByMemoryEntryAndOrdersDescending()
    {
        await using var fixture = await HistoryFixture.CreateAsync();
        var controller = new MemoryEntryMemoryChangesController(fixture.MemoryChangeService);

        var actionResult = await controller.GetByMemoryEntryId(3, page: 1, pageSize: 20);
        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var response = Assert.IsType<PagedMemoryChangeHistoryResponse>(okResult.Value);

        Assert.Equal(2, response.TotalCount);
        Assert.Equal([3, 2], response.Items.Select(item => item.Id).ToArray());
        Assert.All(response.Items, item => Assert.Equal(3, item.MemoryEntryId));
        Assert.All(response.Items, item => Assert.Equal(MemoryKeys.DomainMap, item.MemoryEntryKey));
        Assert.DoesNotContain(response.Items, item => item.Id is 1 or 4);
    }

    [Fact]
    public async Task RollbackEndpoint_RestoresPreviousValue_CreatesAuditRecord_AndRejectsForeignTutor()
    {
        await using var fixture = await HistoryFixture.CreateAsync();
        var entry = await fixture.Db.MemoryEntries.SingleAsync(e => e.Id == 1);
        entry.ValueJson = """{"proximo_paso":"Estado modificado erroneo"}""";
        var change = await fixture.Db.MemoryChanges.SingleAsync(c => c.Id == 1);
        change.PreviousValueJson = """{"proximo_paso":"Estado previo sano"}""";
        change.NewValueJson = """{"proximo_paso":"Estado modificado erroneo"}""";
        await fixture.Db.SaveChangesAsync();

        var controller = new TutorMemoryChangesController(fixture.MemoryChangeService);

        // Wrong tutor must get 404
        var foreignAttempt = await controller.Rollback(tutorId: 2, changeId: 1);
        Assert.IsType<NotFoundResult>(foreignAttempt.Result);

        // Owner tutor succeeds and restores previous JSON + appends audit change
        var rollbackAction = await controller.Rollback(tutorId: 1, changeId: 1);
        var okResult = Assert.IsType<OkObjectResult>(rollbackAction.Result);
        var result = Assert.IsType<MemoryPatchResult>(okResult.Value);

        Assert.Equal("""{"proximo_paso":"Estado previo sano"}""", result.MemoryEntry.ValueJson);
        Assert.Equal("Set", result.MemoryChange.Operation);
        Assert.Equal("/", result.MemoryChange.Path);
        Assert.Contains("Rollback manual al estado previo del cambio #1", result.MemoryChange.Reason);
    }

    private sealed class HistoryFixture : IAsyncDisposable
    {
        private readonly SqliteConnection connection;
        private readonly ServiceProvider serviceProvider;

        private HistoryFixture(SqliteConnection connection, ServiceProvider serviceProvider)
        {
            this.connection = connection;
            this.serviceProvider = serviceProvider;
            Db = serviceProvider.GetRequiredService<LearningAgentsDbContext>();
            MemoryChangeService = serviceProvider.GetRequiredService<IMemoryChangeService>();
        }

        public LearningAgentsDbContext Db { get; }
        public IMemoryChangeService MemoryChangeService { get; }

        public static async Task<HistoryFixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();

            var services = new ServiceCollection();
            services.AddDbContext<LearningAgentsDbContext>(options => options.UseSqlite(connection));
            services.AddApplicationServices();
            services.AddTestInfrastructureBindings();
            var provider = services.BuildServiceProvider();
            var db = provider.GetRequiredService<LearningAgentsDbContext>();
            await db.Database.EnsureCreatedAsync();

            db.Tutors.Add(new Tutor
            {
                Id = 2,
                Name = "Other Tutor",
                Description = "Other tutor",
                SystemPromptContent = "Other prompt",
                GeminiModel = "gemini-2.5-flash",
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            });
            db.MemoryEntries.Add(new MemoryEntry
            {
                Id = 6,
                TutorId = 2,
                Key = MemoryKeys.SessionMemory,
                ValueJson = "{}",
                SchemaVersion = 1,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            });

            var baseTime = new DateTime(2026, 6, 25, 12, 0, 0, DateTimeKind.Utc);
            db.MemoryChanges.AddRange(
                Change(1, memoryEntryId: 1, MemoryPatchOperation.Set, baseTime.AddMinutes(1), "/proximo_paso"),
                Change(2, memoryEntryId: 3, MemoryPatchOperation.Add, baseTime.AddMinutes(2), "/temas"),
                Change(3, memoryEntryId: 3, MemoryPatchOperation.Update, baseTime.AddMinutes(3), "/temas/nivel"),
                Change(4, memoryEntryId: 6, MemoryPatchOperation.Set, baseTime.AddMinutes(4), "/proximo_paso"));
            await db.SaveChangesAsync();

            return new HistoryFixture(connection, provider);
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await serviceProvider.DisposeAsync();
            await connection.DisposeAsync();
        }

        private static MemoryChange Change(
            int id,
            int memoryEntryId,
            MemoryPatchOperation operation,
            DateTime createdAtUtc,
            string path) => new()
            {
                Id = id,
                MemoryEntryId = memoryEntryId,
                Operation = operation,
                Path = path,
                TargetId = string.Empty,
                PreviousValueJson = "{}",
                NewValueJson = "{}",
                Reason = $"reason-{id}",
                CreatedAtUtc = createdAtUtc
            };
    }
}
