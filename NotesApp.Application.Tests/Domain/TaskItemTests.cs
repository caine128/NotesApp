using FluentAssertions;
using NotesApp.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Application.Tests.Domain
{
    public sealed class TaskItemTests
    {
        [Fact]
        public void Create_with_valid_input_returns_success_and_sets_properties()
        {
            // Arrange
            var userId = Guid.NewGuid();
            var date = new DateOnly(2024, 1, 2);
            var now = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc);

            // Act
            var result = TaskItem.Create(
                userId: userId,
                date: date,
                title: "  Title  ",
                description: "  Description  ",
                startTime: new TimeOnly(9, 0),
                endTime: new TimeOnly(10, 0),
                location: "  Office  ",
                travelTime: TimeSpan.FromMinutes(15),
                utcNow: now);

            // Assert
            result.IsSuccess.Should().BeTrue();
            var task = result.Value;

            task.UserId.Should().Be(userId);
            task.Date.Should().Be(date);
            task.Title.Should().Be("Title");
            task.Description.Should().Be("Description");
            task.Location.Should().Be("Office");
            task.StartTime.Should().Be(new TimeOnly(9, 0));
            task.EndTime.Should().Be(new TimeOnly(10, 0));
            task.TravelTime.Should().Be(TimeSpan.FromMinutes(15));
            task.IsCompleted.Should().BeFalse();
            task.Version.Should().Be(1);
            task.IsDeleted.Should().BeFalse();
        }

        [Fact]
        public void Create_with_empty_title_returns_failure()
        {
            // Arrange
            var userId = Guid.NewGuid();
            var date = new DateOnly(2024, 1, 2);
            var now = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc);

            // Act
            var result = TaskItem.Create(
                userId: userId,
                date: date,
                title: "   ",
                description: "Description",
                startTime: null,
                endTime: null,
                location: null,
                travelTime: null,
                utcNow: now);

            // Assert
            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().NotBeEmpty();
        }

        [Fact]
        public void Version_starts_at_one_for_new_task()
        {
            var now = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc);

            var result = TaskItem.Create(
                userId: Guid.NewGuid(),
                date: new DateOnly(2024, 1, 2),
                title: "Task",
                description: "Desc",
                startTime: null,
                endTime: null,
                location: null,
                travelTime: null,
                utcNow: now);

            result.IsSuccess.Should().BeTrue();
            var task = result.Value;

            task.Version.Should().Be(1);
        }

        [Fact]
        public void Version_increments_on_update_and_mark_completed_idempotent()
        {
            var now = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc);

            var result = TaskItem.Create(
                userId: Guid.NewGuid(),
                date: new DateOnly(2024, 1, 2),
                title: "Task",
                description: "Desc",
                startTime: null,
                endTime: null,
                location: null,
                travelTime: null,
                utcNow: now);

            result.IsSuccess.Should().BeTrue();
            var task = result.Value;

            var initialVersion = task.Version;
            var initialUpdatedAt = task.UpdatedAtUtc;

            // Update should bump version
            var updateResult = task.Update(
                title: "Updated",
                date: new DateOnly(2024, 1, 3),
                description: "New Desc",
                startTime: new TimeOnly(8, 0),
                endTime: new TimeOnly(9, 0),
                location: "Home",
                travelTime: TimeSpan.FromMinutes(5),
                utcNow: now.AddMinutes(1));

            updateResult.IsSuccess.Should().BeTrue();

            task.Version.Should().Be(initialVersion + 1);
            task.UpdatedAtUtc.Should().BeAfter(initialUpdatedAt);

            var afterUpdateVersion = task.Version;

            // MarkCompleted first time: bump version
            var completeResult1 = task.MarkCompleted(now.AddMinutes(2));
            completeResult1.IsSuccess.Should().BeTrue();
            var afterFirstCompleteVersion = task.Version;

            afterFirstCompleteVersion.Should().Be(afterUpdateVersion + 1);

            // MarkCompleted second time: idempotent (no further change)
            var completeResult2 = task.MarkCompleted(now.AddMinutes(3));
            completeResult2.IsSuccess.Should().BeTrue();

            task.Version.Should().Be(afterFirstCompleteVersion);
        }

        [Fact]
        public void SetReminder_increments_version_only_when_value_changes()
        {
            var now = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc);

            var result = TaskItem.Create(
                userId: Guid.NewGuid(),
                date: new DateOnly(2024, 1, 2),
                title: "Task",
                description: "Desc",
                startTime: null,
                endTime: null,
                location: null,
                travelTime: null,
                utcNow: now);

            result.IsSuccess.Should().BeTrue();
            var task = result.Value;

            var initialVersion = task.Version;

            var reminder = now.AddHours(1);

            // First set: should bump version
            var setResult1 = task.SetReminder(reminder, now.AddMinutes(1));
            setResult1.IsSuccess.Should().BeTrue();
            task.ReminderAtUtc.Should().Be(reminder);
            task.Version.Should().Be(initialVersion + 1);

            var afterFirstSetVersion = task.Version;

            // Same value: no version bump
            var setResult2 = task.SetReminder(reminder, now.AddMinutes(2));
            setResult2.IsSuccess.Should().BeTrue();
            task.Version.Should().Be(afterFirstSetVersion);

            // Changing value: bump again
            var newReminder = now.AddHours(2);
            var setResult3 = task.SetReminder(newReminder, now.AddMinutes(3));
            setResult3.IsSuccess.Should().BeTrue();
            task.ReminderAtUtc.Should().Be(newReminder);
            task.Version.Should().Be(afterFirstSetVersion + 1);
        }

        [Fact]
        public void AcknowledgeReminder_requires_reminder_and_increments_version()
        {
            var now = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc);

            var result = TaskItem.Create(
                userId: Guid.NewGuid(),
                date: new DateOnly(2024, 1, 2),
                title: "Task",
                description: "Desc",
                startTime: null,
                endTime: null,
                location: null,
                travelTime: null,
                utcNow: now);

            result.IsSuccess.Should().BeTrue();
            var task = result.Value;

            // No reminder → failure
            var failResult = task.AcknowledgeReminder(now.AddMinutes(10), now.AddMinutes(11));
            failResult.IsSuccess.Should().BeFalse();

            // Set reminder then acknowledge
            task.SetReminder(now.AddMinutes(10), now.AddMinutes(1));
            var beforeAckVersion = task.Version;

            var ackAt = now.AddMinutes(11);
            var ackResult = task.AcknowledgeReminder(ackAt, now.AddMinutes(12));
            ackResult.IsSuccess.Should().BeTrue();

            task.ReminderAcknowledgedAtUtc.Should().Be(ackAt);
            task.Version.Should().Be(beforeAckVersion + 1);

            // Second acknowledge is idempotent
            var ackResult2 = task.AcknowledgeReminder(ackAt, now.AddMinutes(13));
            ackResult2.IsSuccess.Should().BeTrue();
            task.Version.Should().Be(beforeAckVersion + 1);
        }

        [Fact]
        public void MarkReminderSent_is_idempotent_and_increments_version_once()
        {
            var now = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc);

            var result = TaskItem.Create(
                userId: Guid.NewGuid(),
                date: new DateOnly(2024, 1, 2),
                title: "Task",
                description: "Desc",
                startTime: null,
                endTime: null,
                location: null,
                travelTime: null,
                utcNow: now);

            result.IsSuccess.Should().BeTrue();
            var task = result.Value;

            task.SetReminder(now.AddMinutes(10), now.AddMinutes(1));
            var beforeSentVersion = task.Version;

            var sentResult1 = task.MarkReminderSent(now.AddMinutes(2));
            sentResult1.IsSuccess.Should().BeTrue();

            var afterSentVersion = task.Version;
            task.ReminderSentAtUtc.Should().NotBeNull();

            // Second mark is idempotent
            var sentResult2 = task.MarkReminderSent(now.AddMinutes(3));
            sentResult2.IsSuccess.Should().BeTrue();

            task.Version.Should().Be(afterSentVersion);
            afterSentVersion.Should().Be(beforeSentVersion + 1);
        }

        [Fact]
        public void SoftDelete_and_RestoreTask_are_idempotent_and_increment_version_once_each()
        {
            var now = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc);

            var result = TaskItem.Create(
                userId: Guid.NewGuid(),
                date: new DateOnly(2024, 1, 2),
                title: "Task",
                description: "Desc",
                startTime: null,
                endTime: null,
                location: null,
                travelTime: null,
                utcNow: now);

            result.IsSuccess.Should().BeTrue();
            var task = result.Value;

            var initialVersion = task.Version;

            var deleteResult1 = task.SoftDelete(now.AddMinutes(1));
            deleteResult1.IsSuccess.Should().BeTrue();
            task.IsDeleted.Should().BeTrue();
            task.Version.Should().Be(initialVersion + 1);

            // Second delete is no-op
            var deleteResult2 = task.SoftDelete(now.AddMinutes(2));
            deleteResult2.IsSuccess.Should().BeTrue();
            task.Version.Should().Be(initialVersion + 1);

            var restoreResult1 = task.RestoreTask(now.AddMinutes(3));
            restoreResult1.IsSuccess.Should().BeTrue();
            task.IsDeleted.Should().BeFalse();
            task.Version.Should().Be(initialVersion + 2);

            // Second restore is no-op
            var restoreResult2 = task.RestoreTask(now.AddMinutes(4));
            restoreResult2.IsSuccess.Should().BeTrue();
            task.Version.Should().Be(initialVersion + 2);
        }
    }
}
