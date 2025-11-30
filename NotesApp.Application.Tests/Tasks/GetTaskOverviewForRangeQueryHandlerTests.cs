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
    public sealed class GetTaskOverviewForRangeQueryHandlerTests
    {
        [Fact]
        public async Task Handle_returns_overview_only_for_current_user_and_in_range_ordered_by_date()
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

            // In-range tasks for current user: 20,21,22
            var t1 = TaskItem.Create(userId, new DateOnly(2025, 2, 20),
                "T1", null, null, null, null, null, DateTime.UtcNow).Value!;
            var t2 = TaskItem.Create(userId, new DateOnly(2025, 2, 21),
                "T2", null, null, null, null, null, DateTime.UtcNow).Value!;
            var t3 = TaskItem.Create(userId, new DateOnly(2025, 2, 22),
                "T3", null, null, null, null, null, DateTime.UtcNow).Value!;

            // Out-of-range for current user
            var beforeRange = TaskItem.Create(userId, new DateOnly(2025, 2, 19),
                "Before", null, null, null, null, null, DateTime.UtcNow).Value!;
            var afterRange = TaskItem.Create(userId, new DateOnly(2025, 2, 23),
                "After", null, null, null, null, null, DateTime.UtcNow).Value!;

            // In-range but for another user
            var otherInRange = TaskItem.Create(otherUserId, new DateOnly(2025, 2, 21),
                "Other", null, null, null, null, null, DateTime.UtcNow).Value!;

            await context.Tasks.AddRangeAsync(t1, t2, t3, beforeRange, afterRange, otherInRange);
            await context.SaveChangesAsync();

            var handler = new GetTaskOverviewForRangeQueryHandler(taskRepository, currentUserMock.Object);

            var query = new GetTaskOverviewForRangeQuery(start, endExclusive);

            // Act
            var result = await handler.Handle(query, CancellationToken.None);

            // Assert
            result.IsSuccess.Should().BeTrue();
            var list = result.Value;
            list.Should().NotBeNull();
            list.Should().HaveCount(3);

            var dates = list.Select(o => o.Date).ToList();
            dates.Should().ContainInOrder(
                new DateOnly(2025, 2, 20),
                new DateOnly(2025, 2, 21),
                new DateOnly(2025, 2, 22));
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

            var handler = new GetTaskOverviewForRangeQueryHandler(taskRepository, currentUserMock.Object);

            var query = new GetTaskOverviewForRangeQuery(
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
