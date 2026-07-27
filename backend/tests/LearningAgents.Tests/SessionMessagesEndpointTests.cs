using LearningAgents.Api.Controllers;
using LearningAgents.Application;
using LearningAgents.Application.Common;
using LearningAgents.Application.Dtos.Messages;
using LearningAgents.Application.Interfaces;
using LearningAgents.Domain.Entities;
using LearningAgents.Domain.Enums;
using LearningAgents.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LearningAgents.Tests;

public sealed class SessionMessagesEndpointTests
{
    [Fact]
    public async Task GetBySession_ReturnsPagedMessagesNewestFirst()
    {
        await using var fixture = await MessagesFixture.CreateAsync();
        var controller = new SessionMessagesController(null!, fixture.MessageService);

        var actionResult = await controller.GetBySession(10, page: 1, pageSize: 2);
        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var response = Assert.IsType<PagedResult<MessageResponse>>(okResult.Value);

        Assert.Equal(1, response.Page);
        Assert.Equal(2, response.PageSize);
        Assert.Equal(3, response.TotalCount);
        Assert.Equal([3, 2], response.Items.Select(item => item.Id).ToArray());
        Assert.All(response.Items, item => Assert.Equal(10, item.SessionId));
        Assert.True(response.Items[0].CreatedAtUtc >= response.Items[1].CreatedAtUtc);
    }

    [Fact]
    public async Task GetBySession_ReturnsNotFoundWhenSessionDoesNotExist()
    {
        await using var fixture = await MessagesFixture.CreateAsync();
        var controller = new SessionMessagesController(null!, fixture.MessageService);

        var actionResult = await controller.GetBySession(999, page: 1, pageSize: 10);

        Assert.IsType<NotFoundResult>(actionResult.Result);
    }

    private sealed class MessagesFixture : IAsyncDisposable
    {
        private readonly SqliteConnection connection;
        private readonly ServiceProvider serviceProvider;

        private MessagesFixture(SqliteConnection connection, ServiceProvider serviceProvider)
        {
            this.connection = connection;
            this.serviceProvider = serviceProvider;
            MessageService = serviceProvider.GetRequiredService<IMessageService>();
        }

        public IMessageService MessageService { get; }

        public static async Task<MessagesFixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();

            var services = new ServiceCollection();
            services.AddDbContext<LearningAgentsDbContext>(options => options.UseSqlite(connection));
            services.AddApplicationServices();
            var provider = services.BuildServiceProvider();
            var db = provider.GetRequiredService<LearningAgentsDbContext>();
            await db.Database.EnsureCreatedAsync();

            var baseTime = new DateTime(2026, 7, 3, 12, 0, 0, DateTimeKind.Utc);
            db.StudySessions.AddRange(
                Session(10, tutorId: 1, "Target session", baseTime),
                Session(11, tutorId: 1, "Other session", baseTime));
            db.Messages.AddRange(
                Message(1, sessionId: 10, MessageRole.User, "oldest", baseTime.AddMinutes(1)),
                Message(2, sessionId: 10, MessageRole.Assistant, "middle", baseTime.AddMinutes(2)),
                Message(3, sessionId: 10, MessageRole.User, "newest", baseTime.AddMinutes(3)),
                Message(4, sessionId: 11, MessageRole.User, "other", baseTime.AddMinutes(4)));
            await db.SaveChangesAsync();

            return new MessagesFixture(connection, provider);
        }

        public async ValueTask DisposeAsync()
        {
            await serviceProvider.DisposeAsync();
            await connection.DisposeAsync();
        }

        private static StudySession Session(int id, int tutorId, string name, DateTime createdAtUtc) => new()
        {
            Id = id,
            TutorId = tutorId,
            Name = name,
            Goal = name,
            CreatedAtUtc = createdAtUtc,
            UpdatedAtUtc = createdAtUtc
        };

        private static Message Message(
            int id,
            int sessionId,
            MessageRole role,
            string content,
            DateTime createdAtUtc) => new()
            {
                Id = id,
                SessionId = sessionId,
                Role = role,
                Content = content,
                CreatedAtUtc = createdAtUtc
            };
    }
}
