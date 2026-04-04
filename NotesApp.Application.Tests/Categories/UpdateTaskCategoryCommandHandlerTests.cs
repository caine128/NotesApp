using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using NotesApp.Application.Abstractions.Persistence;
using NotesApp.Application.Categories.Commands.UpdateTaskCategory;
using NotesApp.Application.Common;
using NotesApp.Application.Common.Interfaces;
using NotesApp.Domain.Common;
using NotesApp.Domain.Entities;
using System;

namespace NotesApp.Application.Tests.Categories
{
    public sealed class UpdateTaskCategoryCommandHandlerTests
    {
        private readonly Mock<ICategoryRepository> _categoryRepositoryMock = new();
        private readonly Mock<IOutboxRepository> _outboxRepositoryMock = new();
        private readonly Mock<IUnitOfWork> _unitOfWorkMock = new();
        private readonly Mock<ICurrentUserService> _currentUserServiceMock = new();
        private readonly Mock<ISystemClock> _clockMock = new();
        private readonly Mock<ILogger<UpdateTaskCategoryCommandHandler>> _loggerMock = new();

        private readonly Guid _userId = Guid.NewGuid();
        private readonly DateTime _now = new(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        private UpdateTaskCategoryCommandHandler CreateHandler()
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

            return new UpdateTaskCategoryCommandHandler(
                _categoryRepositoryMock.Object,
                _outboxRepositoryMock.Object,
                _unitOfWorkMock.Object,
                _currentUserServiceMock.Object,
                _clockMock.Object,
                _loggerMock.Object);
        }

        [Fact]
        public async Task Handle_renames_category_increments_version_and_emits_outbox()
        {
            var handler = CreateHandler();
            var categoryId = Guid.NewGuid();
            var category = TaskCategory.Create(_userId, "Work", _now).Value!;
            typeof(TaskCategory).GetProperty("Id")!.SetValue(category, categoryId);

            _categoryRepositoryMock
                .Setup(r => r.GetByIdUntrackedAsync(categoryId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(category);

            var command = new UpdateTaskCategoryCommand { CategoryId = categoryId, Name = "Lifestyle" };

            var result = await handler.Handle(command, CancellationToken.None);

            result.IsSuccess.Should().BeTrue();
            result.Value!.Name.Should().Be("Lifestyle");
            result.Value.Version.Should().Be(2);

            _categoryRepositoryMock.Verify(r => r.Update(It.IsAny<TaskCategory>()), Times.Once);
            _outboxRepositoryMock.Verify(
                r => r.AddAsync(It.IsAny<OutboxMessage>(), It.IsAny<CancellationToken>()),
                Times.Once);
            _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task Handle_returns_not_found_when_category_does_not_exist()
        {
            var handler = CreateHandler();
            var categoryId = Guid.NewGuid();

            _categoryRepositoryMock
                .Setup(r => r.GetByIdUntrackedAsync(categoryId, It.IsAny<CancellationToken>()))
                .ReturnsAsync((TaskCategory?)null);

            var command = new UpdateTaskCategoryCommand { CategoryId = categoryId, Name = "Lifestyle" };

            var result = await handler.Handle(command, CancellationToken.None);

            result.IsFailed.Should().BeTrue();
            result.Errors.Should().Contain(e =>
                e.Metadata.ContainsKey("ErrorCode") &&
                e.Metadata["ErrorCode"].ToString() == "Categories.NotFound");

            _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task Handle_returns_not_found_when_category_belongs_to_different_user()
        {
            var handler = CreateHandler();
            var categoryId = Guid.NewGuid();
            var foreignCategory = TaskCategory.Create(Guid.NewGuid(), "Work", _now).Value!;

            _categoryRepositoryMock
                .Setup(r => r.GetByIdUntrackedAsync(categoryId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(foreignCategory);

            var command = new UpdateTaskCategoryCommand { CategoryId = categoryId, Name = "Lifestyle" };

            var result = await handler.Handle(command, CancellationToken.None);

            result.IsFailed.Should().BeTrue();
            result.Errors.Should().Contain(e =>
                e.Metadata.ContainsKey("ErrorCode") &&
                e.Metadata["ErrorCode"].ToString() == "Categories.NotFound");
        }
    }
}
