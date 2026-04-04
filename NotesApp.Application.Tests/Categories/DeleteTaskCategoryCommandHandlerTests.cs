using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using NotesApp.Application.Abstractions.Persistence;
using NotesApp.Application.Categories.Commands.DeleteTaskCategory;
using NotesApp.Application.Common;
using NotesApp.Application.Common.Interfaces;
using NotesApp.Domain.Common;
using NotesApp.Domain.Entities;
using System;

namespace NotesApp.Application.Tests.Categories
{
    /// <summary>
    /// Tests for the REST delete path (DeleteTaskCategoryCommandHandler).
    ///
    /// Key contract: the REST delete handler calls ClearCategoryFromTasksAsync server-side.
    /// This is in contrast to the sync push delete path (ProcessCategoryDeletesAsync),
    /// which does NOT clear tasks — the mobile client is responsible.
    /// </summary>
    public sealed class DeleteTaskCategoryCommandHandlerTests
    {
        private readonly Mock<ICategoryRepository> _categoryRepositoryMock = new();
        private readonly Mock<ITaskRepository> _taskRepositoryMock = new();
        private readonly Mock<IOutboxRepository> _outboxRepositoryMock = new();
        private readonly Mock<IUnitOfWork> _unitOfWorkMock = new();
        private readonly Mock<ICurrentUserService> _currentUserServiceMock = new();
        private readonly Mock<ISystemClock> _clockMock = new();
        private readonly Mock<ILogger<DeleteTaskCategoryCommandHandler>> _loggerMock = new();

        private readonly Guid _userId = Guid.NewGuid();
        private readonly DateTime _now = new(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        private DeleteTaskCategoryCommandHandler CreateHandler()
        {
            _currentUserServiceMock
                .Setup(s => s.GetUserIdAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(_userId);

            _clockMock
                .Setup(c => c.UtcNow)
                .Returns(_now);

            _unitOfWorkMock
                .Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            _taskRepositoryMock
                .Setup(r => r.ClearCategoryFromTasksAsync(
                    It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            return new DeleteTaskCategoryCommandHandler(
                _categoryRepositoryMock.Object,
                _taskRepositoryMock.Object,
                _outboxRepositoryMock.Object,
                _unitOfWorkMock.Object,
                _currentUserServiceMock.Object,
                _clockMock.Object,
                _loggerMock.Object);
        }

        [Fact]
        public async Task Handle_soft_deletes_category_clears_task_refs_and_emits_outbox()
        {
            var handler = CreateHandler();
            var categoryId = Guid.NewGuid();
            var category = TaskCategory.Create(_userId, "Work", _now).Value!;
            typeof(TaskCategory).GetProperty("Id")!.SetValue(category, categoryId);

            _categoryRepositoryMock
                .Setup(r => r.GetByIdUntrackedAsync(categoryId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(category);

            var result = await handler.Handle(
                new DeleteTaskCategoryCommand { CategoryId = categoryId }, CancellationToken.None);

            result.IsSuccess.Should().BeTrue();

            _taskRepositoryMock.Verify(
                r => r.ClearCategoryFromTasksAsync(categoryId, _userId, _now, It.IsAny<CancellationToken>()),
                Times.Once);
            _categoryRepositoryMock.Verify(r => r.Update(It.IsAny<TaskCategory>()), Times.Once);
            _outboxRepositoryMock.Verify(
                r => r.AddAsync(It.IsAny<OutboxMessage>(), It.IsAny<CancellationToken>()),
                Times.Once);
            _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task Handle_when_category_not_found_still_clears_task_refs_and_returns_ok()
        {
            // This is the safe idempotency / retry path: the category may already be deleted
            // or genuinely missing, but we still ensure task references are cleared.
            var handler = CreateHandler();
            var categoryId = Guid.NewGuid();

            _categoryRepositoryMock
                .Setup(r => r.GetByIdUntrackedAsync(categoryId, It.IsAny<CancellationToken>()))
                .ReturnsAsync((TaskCategory?)null);

            var result = await handler.Handle(
                new DeleteTaskCategoryCommand { CategoryId = categoryId }, CancellationToken.None);

            result.IsSuccess.Should().BeTrue();

            _taskRepositoryMock.Verify(
                r => r.ClearCategoryFromTasksAsync(categoryId, _userId, _now, It.IsAny<CancellationToken>()),
                Times.Once);

            // No soft-delete or outbox when category was already gone
            _categoryRepositoryMock.Verify(r => r.Update(It.IsAny<TaskCategory>()), Times.Never);
            _outboxRepositoryMock.Verify(
                r => r.AddAsync(It.IsAny<OutboxMessage>(), It.IsAny<CancellationToken>()),
                Times.Never);
            _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task Handle_when_category_belongs_to_different_user_returns_not_found_without_clearing()
        {
            var handler = CreateHandler();
            var categoryId = Guid.NewGuid();
            var foreignCategory = TaskCategory.Create(Guid.NewGuid(), "Work", _now).Value!;

            _categoryRepositoryMock
                .Setup(r => r.GetByIdUntrackedAsync(categoryId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(foreignCategory);

            var result = await handler.Handle(
                new DeleteTaskCategoryCommand { CategoryId = categoryId }, CancellationToken.None);

            result.IsFailed.Should().BeTrue();
            result.Errors.Should().Contain(e =>
                e.Metadata.ContainsKey("ErrorCode") &&
                e.Metadata["ErrorCode"].ToString() == "Categories.NotFound");

            _taskRepositoryMock.Verify(
                r => r.ClearCategoryFromTasksAsync(
                    It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
                Times.Never);
            _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        }
    }
}
