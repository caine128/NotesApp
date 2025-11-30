using FluentAssertions;
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
    public sealed class GetTaskSummariesForRangeQueryHandlerTests
    {
        [Fact]
        public async Task Handle_returns_summaries_for_user_in_range_ordered_by_date_then_start_time()
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

            var start = new DateOnly(2025, 2, 20);
            var endExclusive = new DateOnly(2025, 2, 23);

            // Tasks for current user in range at different dates and times
            var t1 = TaskItem.Create(userId, new DateOnly(2025, 2, 20),
                "D20-09", null, new TimeOnly(9, 0), new TimeOnly(10, 0), null, null, DateTime.UtcNow).Value!;
            var t2 = TaskItem.Create(userId, new DateOnly(2025, 2, 21),
                "D21-08", null, new TimeOnly(8, 0), new TimeOnly(9, 0), null, null, DateTime.UtcNow).Value!;
            var t3 = TaskItem.Create(userId, new DateOnly(2025, 2, 21),
                "D21-10", null, new TimeOnly(10, 0), new TimeOnly(11, 0), null, null, DateTime.UtcNow).Value!;
            var t4 = TaskItem.Create(userId, new DateOnly(2025, 2, 22),
                "D22-07", null, new TimeOnly(7, 0), new TimeOnly(8, 0), null, null, DateTime.UtcNow).Value!;

            // Out-of-range tasks for current user
            var beforeRange = TaskItem.Create(userId, new DateOnly(2025, 2, 19),
                "Before", null, null, null, null, null, DateTime.UtcNow).Value!;
            var afterRange = TaskItem.Create(userId, new DateOnly(2025, 2, 23),
                "After", null, null, null, null, null, DateTime.UtcNow).Value!;

            // In-range, other user
            var otherUserTask = TaskItem.Create(otherUserId, new DateOnly(2025, 2, 21),
                "OtherUser", null, new TimeOnly(6, 0), new TimeOnly(7, 0), null, null, DateTime.UtcNow).Value!;

            await context.Tasks.AddRangeAsync(
                t1, t2, t3, t4,
                beforeRange, afterRange,
                otherUserTask);

            await context.SaveChangesAsync();

            var handler = new GetTaskSummariesForRangeQueryHandler(taskRepository, currentUserMock.Object);

            var query = new GetTaskSummariesForRangeQuery(start, endExclusive);

            // Act
            var result = await handler.Handle(query, CancellationToken.None);

            // Assert
            result.IsSuccess.Should().BeTrue();
            var list = result.Value;
            list.Should().NotBeNull();
            list.Should().HaveCount(4);

            // Should be ordered by Date, then StartTime:
            // 20: D20-09
            // 21: D21-08, D21-10
            // 22: D22-07
            list.Select(x => x.Title).Should()
                .ContainInOrder("D20-09", "D21-08", "D21-10", "D22-07");
        }

        [Fact]
        public async Task Handle_when_no_tasks_in_range_returns_empty_list()
        {
            // Arrange
            await using var context = SqlServerAppDbContextFactory.CreateContext();

            ITaskRepository taskRepository = new TaskRepository(context);

            var userId = Guid.NewGuid();

            var currentUserMock = new Mock<ICurrentUserService>();
            currentUserMock
                .Setup(s => s.GetUserIdAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(userId);

            var handler = new GetTaskSummariesForRangeQueryHandler(taskRepository, currentUserMock.Object);

            var query = new GetTaskSummariesForRangeQuery(
                Start: new DateOnly(2025, 2, 20),
                EndExclusive: new DateOnly(2025, 2, 21));

            // Act
            var result = await handler.Handle(query, CancellationToken.None);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.Value.Should().NotBeNull();
            result.Value.Should().BeEmpty();
        }
    }
}
