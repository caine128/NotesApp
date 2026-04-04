using FluentAssertions;
using NotesApp.Domain.Entities;
using System;

namespace NotesApp.Application.Tests.Domain
{
    public sealed class TaskCategoryTests
    {
        private readonly DateTime _now = new(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        // ── Create ────────────────────────────────────────────────────────────

        [Fact]
        public void Create_with_valid_input_returns_success_and_sets_properties()
        {
            var userId = Guid.NewGuid();

            var result = TaskCategory.Create(userId, "Work", _now);

            result.IsSuccess.Should().BeTrue();
            var category = result.Value!;
            category.UserId.Should().Be(userId);
            category.Name.Should().Be("Work");
            category.Version.Should().Be(1);
            category.IsDeleted.Should().BeFalse();
            category.CreatedAtUtc.Should().Be(_now);
        }

        [Fact]
        public void Create_trims_whitespace_from_name()
        {
            var result = TaskCategory.Create(Guid.NewGuid(), "  Lifestyle  ", _now);

            result.IsSuccess.Should().BeTrue();
            result.Value!.Name.Should().Be("Lifestyle");
        }

        [Fact]
        public void Create_with_whitespace_only_name_returns_failure()
        {
            var result = TaskCategory.Create(Guid.NewGuid(), "   ", _now);

            result.IsFailure.Should().BeTrue();
            result.Errors.Should().Contain(e => e.Code == "TaskCategory.Name.Empty");
        }

        [Fact]
        public void Create_with_null_name_returns_failure()
        {
            var result = TaskCategory.Create(Guid.NewGuid(), null, _now);

            result.IsFailure.Should().BeTrue();
            result.Errors.Should().Contain(e => e.Code == "TaskCategory.Name.Empty");
        }

        [Fact]
        public void Create_with_empty_userId_returns_failure()
        {
            var result = TaskCategory.Create(Guid.Empty, "Work", _now);

            result.IsFailure.Should().BeTrue();
            result.Errors.Should().Contain(e => e.Code == "TaskCategory.UserId.Empty");
        }

        // ── Update ────────────────────────────────────────────────────────────

        [Fact]
        public void Update_renames_category_and_increments_version()
        {
            var category = TaskCategory.Create(Guid.NewGuid(), "Work", _now).Value!;

            var result = category.Update("Lifestyle", _now.AddMinutes(1));

            result.IsSuccess.Should().BeTrue();
            category.Name.Should().Be("Lifestyle");
            category.Version.Should().Be(2);
        }

        [Fact]
        public void Update_trims_whitespace_from_name()
        {
            var category = TaskCategory.Create(Guid.NewGuid(), "Work", _now).Value!;

            var result = category.Update("  Personal  ", _now.AddMinutes(1));

            result.IsSuccess.Should().BeTrue();
            category.Name.Should().Be("Personal");
        }

        [Fact]
        public void Update_with_empty_name_returns_failure_and_does_not_mutate()
        {
            var category = TaskCategory.Create(Guid.NewGuid(), "Work", _now).Value!;

            var result = category.Update("   ", _now.AddMinutes(1));

            result.IsFailure.Should().BeTrue();
            result.Errors.Should().Contain(e => e.Code == "TaskCategory.Name.Empty");
            category.Name.Should().Be("Work");   // unchanged
            category.Version.Should().Be(1);      // not incremented
        }

        [Fact]
        public void Update_on_deleted_category_returns_failure()
        {
            var category = TaskCategory.Create(Guid.NewGuid(), "Work", _now).Value!;
            category.SoftDelete(_now);

            var result = category.Update("Lifestyle", _now.AddMinutes(1));

            result.IsFailure.Should().BeTrue();
            result.Errors.Should().Contain(e => e.Code == "TaskCategory.Deleted");
        }

        // ── SoftDelete ────────────────────────────────────────────────────────

        [Fact]
        public void SoftDelete_marks_deleted_and_increments_version()
        {
            var category = TaskCategory.Create(Guid.NewGuid(), "Work", _now).Value!;

            var result = category.SoftDelete(_now);

            result.IsSuccess.Should().BeTrue();
            category.IsDeleted.Should().BeTrue();
            category.Version.Should().Be(2);
        }

        [Fact]
        public void SoftDelete_is_idempotent_and_does_not_double_increment_version()
        {
            var category = TaskCategory.Create(Guid.NewGuid(), "Work", _now).Value!;
            category.SoftDelete(_now);
            var versionAfterFirstDelete = category.Version;

            var secondResult = category.SoftDelete(_now.AddMinutes(1));

            secondResult.IsSuccess.Should().BeTrue();
            category.Version.Should().Be(versionAfterFirstDelete);
        }
    }
}
