using FluentAssertions;
using Moq;
using NotesApp.Application.Abstractions.Persistence;
using NotesApp.Application.Categories.Queries.GetTaskCategory;
using NotesApp.Application.Common.Interfaces;
using NotesApp.Domain.Entities;
using System;

namespace NotesApp.Application.Tests.Categories
{
    public sealed class GetTaskCategoryQueryHandlerTests
    {
        private readonly Mock<ICategoryRepository> _categoryRepositoryMock = new();
        private readonly Mock<ICurrentUserService> _currentUserServiceMock = new();

        private readonly Guid _userId = Guid.NewGuid();
        private readonly DateTime _now = new(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        private GetTaskCategoryQueryHandler CreateHandler()
        {
            _currentUserServiceMock
                .Setup(s => s.GetUserIdAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(_userId);

            return new GetTaskCategoryQueryHandler(
                _categoryRepositoryMock.Object,
                _currentUserServiceMock.Object);
        }

        [Fact]
        public async Task Handle_returns_dto_when_category_found_and_owned_by_user()
        {
            var handler = CreateHandler();
            var categoryId = Guid.NewGuid();
            var category = TaskCategory.Create(_userId, "Work", _now).Value!;
            typeof(TaskCategory).GetProperty("Id")!.SetValue(category, categoryId);

            _categoryRepositoryMock
                .Setup(r => r.GetByIdUntrackedAsync(categoryId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(category);

            var result = await handler.Handle(new GetTaskCategoryQuery(categoryId), CancellationToken.None);

            result.IsSuccess.Should().BeTrue();
            result.Value!.CategoryId.Should().Be(categoryId);
            result.Value.Name.Should().Be("Work");
            result.Value.Version.Should().Be(1);
        }

        [Fact]
        public async Task Handle_returns_not_found_when_category_does_not_exist()
        {
            var handler = CreateHandler();
            var categoryId = Guid.NewGuid();

            _categoryRepositoryMock
                .Setup(r => r.GetByIdUntrackedAsync(categoryId, It.IsAny<CancellationToken>()))
                .ReturnsAsync((TaskCategory?)null);

            var result = await handler.Handle(new GetTaskCategoryQuery(categoryId), CancellationToken.None);

            result.IsFailed.Should().BeTrue();
            result.Errors.Should().Contain(e =>
                e.Metadata.ContainsKey("ErrorCode") &&
                e.Metadata["ErrorCode"].ToString() == "Categories.NotFound");
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

            var result = await handler.Handle(new GetTaskCategoryQuery(categoryId), CancellationToken.None);

            result.IsFailed.Should().BeTrue();
            result.Errors.Should().Contain(e =>
                e.Metadata.ContainsKey("ErrorCode") &&
                e.Metadata["ErrorCode"].ToString() == "Categories.NotFound");
        }
    }
}
