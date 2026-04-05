using FluentAssertions;
using NotesApp.Domain.Entities;
using System;

namespace NotesApp.Application.Tests.Domain
{
    public sealed class SubtaskTests
    {
        private readonly DateTime _now = new(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        private readonly Guid _userId = Guid.NewGuid();
        private readonly Guid _taskId = Guid.NewGuid();

        // ── Create ────────────────────────────────────────────────────────────

        [Fact]
        public void Create_with_valid_input_returns_success_and_sets_properties()
        {
            var result = Subtask.Create(_userId, _taskId, "Buy groceries", "a0", _now);

            result.IsSuccess.Should().BeTrue();
            var subtask = result.Value!;
            subtask.UserId.Should().Be(_userId);
            subtask.TaskId.Should().Be(_taskId);
            subtask.Text.Should().Be("Buy groceries");
            subtask.Position.Should().Be("a0");
            subtask.IsCompleted.Should().BeFalse();
            subtask.Version.Should().Be(1);
            subtask.IsDeleted.Should().BeFalse();
            subtask.CreatedAtUtc.Should().Be(_now);
        }

        [Fact]
        public void Create_trims_whitespace_from_text()
        {
            var result = Subtask.Create(_userId, _taskId, "  Buy groceries  ", "a0", _now);

            result.IsSuccess.Should().BeTrue();
            result.Value!.Text.Should().Be("Buy groceries");
        }

        [Fact]
        public void Create_with_whitespace_only_text_returns_failure()
        {
            var result = Subtask.Create(_userId, _taskId, "   ", "a0", _now);

            result.IsFailure.Should().BeTrue();
            result.Errors.Should().Contain(e => e.Code == "Subtask.Text.Empty");
        }

        [Fact]
        public void Create_with_null_text_returns_failure()
        {
            var result = Subtask.Create(_userId, _taskId, null, "a0", _now);

            result.IsFailure.Should().BeTrue();
            result.Errors.Should().Contain(e => e.Code == "Subtask.Text.Empty");
        }

        [Fact]
        public void Create_with_empty_position_returns_failure()
        {
            var result = Subtask.Create(_userId, _taskId, "Buy groceries", "", _now);

            result.IsFailure.Should().BeTrue();
            result.Errors.Should().Contain(e => e.Code == "Subtask.Position.Empty");
        }

        [Fact]
        public void Create_with_null_position_returns_failure()
        {
            var result = Subtask.Create(_userId, _taskId, "Buy groceries", null, _now);

            result.IsFailure.Should().BeTrue();
            result.Errors.Should().Contain(e => e.Code == "Subtask.Position.Empty");
        }

        [Fact]
        public void Create_with_empty_userId_returns_failure()
        {
            var result = Subtask.Create(Guid.Empty, _taskId, "Buy groceries", "a0", _now);

            result.IsFailure.Should().BeTrue();
            result.Errors.Should().Contain(e => e.Code == "Subtask.UserId.Empty");
        }

        [Fact]
        public void Create_with_empty_taskId_returns_failure()
        {
            var result = Subtask.Create(_userId, Guid.Empty, "Buy groceries", "a0", _now);

            result.IsFailure.Should().BeTrue();
            result.Errors.Should().Contain(e => e.Code == "Subtask.TaskId.Empty");
        }

        [Fact]
        public void Create_with_multiple_invalid_fields_returns_all_errors()
        {
            var result = Subtask.Create(Guid.Empty, Guid.Empty, null, null, _now);

            result.IsFailure.Should().BeTrue();
            result.Errors.Should().HaveCount(4);
        }

        // ── UpdateText ────────────────────────────────────────────────────────

        [Fact]
        public void UpdateText_changes_text_and_increments_version()
        {
            var subtask = Subtask.Create(_userId, _taskId, "Old text", "a0", _now).Value!;

            var result = subtask.UpdateText("New text", _now.AddMinutes(1));

            result.IsSuccess.Should().BeTrue();
            subtask.Text.Should().Be("New text");
            subtask.Version.Should().Be(2);
        }

        [Fact]
        public void UpdateText_trims_whitespace()
        {
            var subtask = Subtask.Create(_userId, _taskId, "Old text", "a0", _now).Value!;

            var result = subtask.UpdateText("  Trimmed  ", _now.AddMinutes(1));

            result.IsSuccess.Should().BeTrue();
            subtask.Text.Should().Be("Trimmed");
        }

        [Fact]
        public void UpdateText_with_empty_text_returns_failure_and_does_not_mutate()
        {
            var subtask = Subtask.Create(_userId, _taskId, "Original", "a0", _now).Value!;

            var result = subtask.UpdateText("   ", _now.AddMinutes(1));

            result.IsFailure.Should().BeTrue();
            result.Errors.Should().Contain(e => e.Code == "Subtask.Text.Empty");
            subtask.Text.Should().Be("Original");
            subtask.Version.Should().Be(1);
        }

        [Fact]
        public void UpdateText_on_deleted_subtask_returns_failure()
        {
            var subtask = Subtask.Create(_userId, _taskId, "Buy groceries", "a0", _now).Value!;
            subtask.SoftDelete(_now);

            var result = subtask.UpdateText("New text", _now.AddMinutes(1));

            result.IsFailure.Should().BeTrue();
            result.Errors.Should().Contain(e => e.Code == "Subtask.Deleted");
        }

        // ── SetCompleted ──────────────────────────────────────────────────────

        [Fact]
        public void SetCompleted_to_true_marks_completed_and_increments_version()
        {
            var subtask = Subtask.Create(_userId, _taskId, "Buy groceries", "a0", _now).Value!;

            var result = subtask.SetCompleted(true, _now.AddMinutes(1));

            result.IsSuccess.Should().BeTrue();
            subtask.IsCompleted.Should().BeTrue();
            subtask.Version.Should().Be(2);
        }

        [Fact]
        public void SetCompleted_is_idempotent_when_value_unchanged()
        {
            var subtask = Subtask.Create(_userId, _taskId, "Buy groceries", "a0", _now).Value!;
            subtask.SetCompleted(true, _now.AddMinutes(1));

            // Set to same value again
            var result = subtask.SetCompleted(true, _now.AddMinutes(2));

            result.IsSuccess.Should().BeTrue();
            subtask.Version.Should().Be(2); // not incremented a second time
        }

        [Fact]
        public void SetCompleted_to_false_marks_incomplete_and_increments_version()
        {
            var subtask = Subtask.Create(_userId, _taskId, "Buy groceries", "a0", _now).Value!;
            subtask.SetCompleted(true, _now.AddMinutes(1));

            var result = subtask.SetCompleted(false, _now.AddMinutes(2));

            result.IsSuccess.Should().BeTrue();
            subtask.IsCompleted.Should().BeFalse();
            subtask.Version.Should().Be(3);
        }

        [Fact]
        public void SetCompleted_on_deleted_subtask_returns_failure()
        {
            var subtask = Subtask.Create(_userId, _taskId, "Buy groceries", "a0", _now).Value!;
            subtask.SoftDelete(_now);

            var result = subtask.SetCompleted(true, _now.AddMinutes(1));

            result.IsFailure.Should().BeTrue();
            result.Errors.Should().Contain(e => e.Code == "Subtask.Deleted");
        }

        // ── UpdatePosition ────────────────────────────────────────────────────

        [Fact]
        public void UpdatePosition_changes_position_and_increments_version()
        {
            var subtask = Subtask.Create(_userId, _taskId, "Buy groceries", "a0", _now).Value!;

            var result = subtask.UpdatePosition("a1", _now.AddMinutes(1));

            result.IsSuccess.Should().BeTrue();
            subtask.Position.Should().Be("a1");
            subtask.Version.Should().Be(2);
        }

        [Fact]
        public void UpdatePosition_with_empty_position_returns_failure_and_does_not_mutate()
        {
            var subtask = Subtask.Create(_userId, _taskId, "Buy groceries", "a0", _now).Value!;

            var result = subtask.UpdatePosition("", _now.AddMinutes(1));

            result.IsFailure.Should().BeTrue();
            result.Errors.Should().Contain(e => e.Code == "Subtask.Position.Empty");
            subtask.Position.Should().Be("a0");
            subtask.Version.Should().Be(1);
        }

        [Fact]
        public void UpdatePosition_on_deleted_subtask_returns_failure()
        {
            var subtask = Subtask.Create(_userId, _taskId, "Buy groceries", "a0", _now).Value!;
            subtask.SoftDelete(_now);

            var result = subtask.UpdatePosition("a1", _now.AddMinutes(1));

            result.IsFailure.Should().BeTrue();
            result.Errors.Should().Contain(e => e.Code == "Subtask.Deleted");
        }

        // ── SoftDelete ────────────────────────────────────────────────────────

        [Fact]
        public void SoftDelete_marks_deleted_and_increments_version()
        {
            var subtask = Subtask.Create(_userId, _taskId, "Buy groceries", "a0", _now).Value!;

            var result = subtask.SoftDelete(_now);

            result.IsSuccess.Should().BeTrue();
            subtask.IsDeleted.Should().BeTrue();
            subtask.Version.Should().Be(2);
        }

        [Fact]
        public void SoftDelete_is_idempotent_and_does_not_double_increment_version()
        {
            var subtask = Subtask.Create(_userId, _taskId, "Buy groceries", "a0", _now).Value!;
            subtask.SoftDelete(_now);
            var versionAfterFirstDelete = subtask.Version;

            var secondResult = subtask.SoftDelete(_now.AddMinutes(1));

            secondResult.IsSuccess.Should().BeTrue();
            subtask.Version.Should().Be(versionAfterFirstDelete);
        }
    }
}
