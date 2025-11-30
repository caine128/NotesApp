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
    public sealed class GetTaskDetailQueryHandlerTests
    {
        [Fact]
        public async Task Handle_returns_task_detail_for_current_user()
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

            // Seed one task for current user
            var createResult = TaskItem.Create(
                userId: userId,
                date: new DateOnly(2025, 2, 20),
                title: "My task",
                description: "My desc",
                startTime: new TimeOnly(9, 0),
                endTime: new TimeOnly(10, 0),
                location: "Office",
                travelTime: TimeSpan.FromMinutes(15),
                utcNow: DateTime.UtcNow);

            createResult.IsSuccess.Should().BeTrue();
            var myTask = createResult.Value!;

            // And another task for a different user
            var otherResult = TaskItem.Create(
                userId: otherUserId,
                date: new DateOnly(2025, 2, 20),
                title: "Other task",
                description: null,
                startTime: null,
                endTime: null,
                location: null,
                travelTime: null,
                utcNow: DateTime.UtcNow);

            otherResult.IsSuccess.Should().BeTrue();
            var otherTask = otherResult.Value!;

            await context.Tasks.AddRangeAsync(myTask, otherTask);
            await context.SaveChangesAsync();

            var handler = new GetTaskDetailQueryHandler(taskRepository, currentUserMock.Object);

            var query = new GetTaskDetailQuery(myTask.Id);

            // Act
            var result = await handler.Handle(query, CancellationToken.None);

            // Assert
            result.IsSuccess.Should().BeTrue();
            var dto = result.Value;
            dto.Should().NotBeNull();
            dto.TaskId.Should().Be(myTask.Id);
            dto.Title.Should().Be("My task");
            dto.Description.Should().Be("My desc");
            dto.Date.Should().Be(new DateOnly(2025, 2, 20));
        }

        [Fact]
        public async Task Handle_when_task_does_not_exist_returns_not_found()
        {
            await using var context = SqlServerAppDbContextFactory.CreateContext();

            ITaskRepository taskRepository = new TaskRepository(context);

            var userId = Guid.NewGuid();

            var currentUserMock = new Mock<ICurrentUserService>();
            currentUserMock
                .Setup(s => s.GetUserIdAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(userId);

            var handler = new GetTaskDetailQueryHandler(taskRepository, currentUserMock.Object);

            var query = new GetTaskDetailQuery(Guid.NewGuid());

            var result = await handler.Handle(query, CancellationToken.None);

            result.IsFailed.Should().BeTrue();
            result.Errors.Should().NotBeEmpty();
            result.Errors[0].Metadata["ErrorCode"].Should().Be("Tasks.NotFound");
        }

        [Fact]
        public async Task Handle_when_task_belongs_to_another_user_returns_not_found()
        {
            await using var context = SqlServerAppDbContextFactory.CreateContext();

            ITaskRepository taskRepository = new TaskRepository(context);

            var currentUserId = Guid.NewGuid();
            var otherUserId = Guid.NewGuid();

            var currentUserMock = new Mock<ICurrentUserService>();
            currentUserMock
                .Setup(s => s.GetUserIdAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(currentUserId);

            var otherTaskResult = TaskItem.Create(
                userId: otherUserId,
                date: new DateOnly(2025, 2, 20),
                title: "Other users task",
                description: null,
                startTime: null,
                endTime: null,
                location: null,
                travelTime: null,
                utcNow: DateTime.UtcNow);

            otherTaskResult.IsSuccess.Should().BeTrue();
            var otherTask = otherTaskResult.Value!;

            await context.Tasks.AddAsync(otherTask);
            await context.SaveChangesAsync();

            var handler = new GetTaskDetailQueryHandler(taskRepository, currentUserMock.Object);

            var query = new GetTaskDetailQuery(otherTask.Id);

            var result = await handler.Handle(query, CancellationToken.None);

            result.IsFailed.Should().BeTrue();
            result.Errors.Should().NotBeEmpty();
            result.Errors[0].Metadata["ErrorCode"].Should().Be("Tasks.NotFound");
        }
    }
}
