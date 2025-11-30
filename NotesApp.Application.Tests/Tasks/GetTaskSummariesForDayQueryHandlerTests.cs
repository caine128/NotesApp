using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using NotesApp.Application.Abstractions.Persistence;
using NotesApp.Application.Common.Interfaces;
using NotesApp.Application.Tasks.Queries;
using NotesApp.Application.Tests.Infrastructure;
using NotesApp.Domain.Entities;
using NotesApp.Infrastructure.Persistence.Repositories;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Application.Tests.Tasks
{
    public sealed class GetTaskSummariesForDayQueryHandlerTests
    {
        [Fact]
        public async Task Handle_returns_summaries_for_current_user_and_date_ordered_by_start_time()
        {
            // Arrange
            await using var context = SqlServerAppDbContextFactory.CreateContext();

            ITaskRepository taskRepository = new TaskRepository(context);

            var userId = Guid.NewGuid();
            var otherUserId = Guid.NewGuid();

            var currentUserMock = new Mock<ICurrentUserService>();
            currentUserMock
                .Setup(s => s.GetUserIdAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(userId);

            var logger = new LoggerFactory().CreateLogger<GetTaskSummariesForDayQueryHandler>();

            var date = new DateOnly(2025, 2, 20);

            // Tasks for current user on the target date, with different start times
            var t1 = TaskItem.Create(userId, date, "T1", null, new TimeOnly(9, 0), new TimeOnly(10, 0), null, null, DateTime.UtcNow).Value!;
            var t2 = TaskItem.Create(userId, date, "T2", null, new TimeOnly(8, 0), new TimeOnly(9, 0), null, null, DateTime.UtcNow).Value!;
            var t3 = TaskItem.Create(userId, date, "T3", null, new TimeOnly(11, 0), new TimeOnly(12, 0), null, null, DateTime.UtcNow).Value!;

            // Task for same user but different date
            var otherDate = TaskItem.Create(userId, new DateOnly(2025, 2, 21),
                "Other date", null, new TimeOnly(7, 0), new TimeOnly(8, 0), null, null, DateTime.UtcNow).Value!;

            // Task for other user on the same date
            var otherUserTask = TaskItem.Create(otherUserId, date,
                "Other user", null, new TimeOnly(6, 0), new TimeOnly(7, 0), null, null, DateTime.UtcNow).Value!;

            await context.Tasks.AddRangeAsync(t1, t2, t3, otherDate, otherUserTask);
            await context.SaveChangesAsync();

            var handler = new GetTaskSummariesForDayQueryHandler(taskRepository, currentUserMock.Object, logger);

            var query = new GetTaskSummariesForDayQuery(date);

            // Act
            var result = await handler.Handle(query, CancellationToken.None);

            // Assert
            result.IsSuccess.Should().BeTrue();
            var list = result.Value;
            list.Should().NotBeNull();
            list.Should().HaveCount(3);

            // Should be ordered by StartTime: T2 (08:00), T1 (09:00), T3 (11:00)
            list.Select(x => x.Title).Should().ContainInOrder("T2", "T1", "T3");
        }

        [Fact]
        public async Task Handle_when_no_tasks_for_day_returns_empty_list()
        {
            // Arrange
            await using var context = SqlServerAppDbContextFactory.CreateContext();

            ITaskRepository taskRepository = new TaskRepository(context);

            var userId = Guid.NewGuid();

            var currentUserMock = new Mock<ICurrentUserService>();
            currentUserMock
                .Setup(s => s.GetUserIdAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(userId);

            var logger = new LoggerFactory().CreateLogger<GetTaskSummariesForDayQueryHandler>();

            var handler = new GetTaskSummariesForDayQueryHandler(taskRepository, currentUserMock.Object, logger);

            var query = new GetTaskSummariesForDayQuery(new DateOnly(2025, 2, 20));

            // Act
            var result = await handler.Handle(query, CancellationToken.None);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.Value.Should().NotBeNull();
            result.Value.Should().BeEmpty();
        }
    }
}
