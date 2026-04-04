using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using NotesApp.Application.Abstractions.Persistence;
using NotesApp.Application.Categories.Commands.CreateTaskCategory;
using NotesApp.Application.Common;
using NotesApp.Application.Common.Interfaces;
using NotesApp.Domain.Common;
using NotesApp.Domain.Entities;
using System;

namespace NotesApp.Application.Tests.Categories
{
    public sealed class CreateTaskCategoryCommandHandlerTests
    {
        private readonly Mock<ICategoryRepository> _categoryRepositoryMock = new();
        private readonly Mock<IOutboxRepository> _outboxRepositoryMock = new();
        private readonly Mock<IUnitOfWork> _unitOfWorkMock = new();
        private readonly Mock<ICurrentUserService> _currentUserServiceMock = new();
        private readonly Mock<ISystemClock> _clockMock = new();
        private readonly Mock<ILogger<CreateTaskCategoryCommandHandler>> _loggerMock = new();

        private readonly Guid _userId = Guid.NewGuid();
        private readonly DateTime _now = new(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        private CreateTaskCategoryCommandHandler CreateHandler()
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

            return new CreateTaskCategoryCommandHandler(
                _categoryRepositoryMock.Object,
                _outboxRepositoryMock.Object,
                _unitOfWorkMock.Object,
                _currentUserServiceMock.Object,
                _clockMock.Object,
                _loggerMock.Object);
        }

        [Fact]
        public async Task Handle_creates_category_persists_and_returns_dto()
        {
            var handler = CreateHandler();
            var command = new CreateTaskCategoryCommand { Name = "Work" };

            var result = await handler.Handle(command, CancellationToken.None);

            result.IsSuccess.Should().BeTrue();
            result.Value!.Name.Should().Be("Work");
            result.Value.Version.Should().Be(1);
            result.Value.CategoryId.Should().NotBeEmpty();

            _categoryRepositoryMock.Verify(
                r => r.AddAsync(It.IsAny<TaskCategory>(), It.IsAny<CancellationToken>()),
                Times.Once);
            _outboxRepositoryMock.Verify(
                r => r.AddAsync(It.IsAny<OutboxMessage>(), It.IsAny<CancellationToken>()),
                Times.Once);
            _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task Handle_trims_name_whitespace()
        {
            var handler = CreateHandler();
            var command = new CreateTaskCategoryCommand { Name = "  Lifestyle  " };

            var result = await handler.Handle(command, CancellationToken.None);

            result.IsSuccess.Should().BeTrue();
            result.Value!.Name.Should().Be("Lifestyle");
        }

        [Fact]
        public async Task Handle_with_empty_name_returns_failure_and_does_not_persist()
        {
            var handler = CreateHandler();
            var command = new CreateTaskCategoryCommand { Name = "   " };

            var result = await handler.Handle(command, CancellationToken.None);

            result.IsFailed.Should().BeTrue();

            _categoryRepositoryMock.Verify(
                r => r.AddAsync(It.IsAny<TaskCategory>(), It.IsAny<CancellationToken>()),
                Times.Never);
            _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        }
    }
}
