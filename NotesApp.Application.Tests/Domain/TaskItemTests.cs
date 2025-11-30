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
            var userId = Guid.NewGuid();
            var date = new DateOnly(2025, 2, 20);
            var utcNow = new DateTime(2025, 2, 1, 12, 0, 0, DateTimeKind.Utc);

            var result = TaskItem.Create(
                userId: userId,
                date: date,
                title: "  My task  ",
                description: "  desc  ",
                startTime: new TimeOnly(9, 0),
                endTime: new TimeOnly(10, 0),
                location: "  Office  ",
                travelTime: TimeSpan.FromMinutes(15),
                utcNow: utcNow);

            result.IsSuccess.Should().BeTrue();
            var task = result.Value!;

            task.Id.Should().NotBe(Guid.Empty);
            task.UserId.Should().Be(userId);
            task.Date.Should().Be(date);
            task.Title.Should().Be("My task");
            task.Description.Should().Be("desc");
            task.StartTime.Should().Be(new TimeOnly(9, 0));
            task.EndTime.Should().Be(new TimeOnly(10, 0));
            task.Location.Should().Be("Office");
            task.TravelTime.Should().Be(TimeSpan.FromMinutes(15));
            task.IsCompleted.Should().BeFalse();
            task.IsDeleted.Should().BeFalse();

            task.CreatedAtUtc.Should().Be(utcNow);
            task.UpdatedAtUtc.Should().Be(utcNow);
        }

        [Fact]
        public void Create_with_empty_userid_returns_failure()
        {
            var result = TaskItem.Create(
                userId: Guid.Empty,
                date: new DateOnly(2025, 2, 20),
                title: "Title",
                description: null,
                startTime: null,
                endTime: null,
                location: null,
                travelTime: null,
                utcNow: DateTime.UtcNow);

            result.IsFailure.Should().BeTrue();
            result.Errors.Should().Contain(e => e.Code == "Task.UserId.Empty");
        }

        [Fact]
        public void Create_with_empty_title_returns_failure()
        {
            var result = TaskItem.Create(
                userId: Guid.NewGuid(),
                date: new DateOnly(2025, 2, 20),
                title: "   ",
                description: null,
                startTime: null,
                endTime: null,
                location: null,
                travelTime: null,
                utcNow: DateTime.UtcNow);

            result.IsFailure.Should().BeTrue();
            result.Errors.Should().Contain(e => e.Code == "Task.Title.Empty");
        }

        [Fact]
        public void Create_with_default_date_returns_failure()
        {
            var result = TaskItem.Create(
                userId: Guid.NewGuid(),
                date: default,
                title: "Title",
                description: null,
                startTime: null,
                endTime: null,
                location: null,
                travelTime: null,
                utcNow: DateTime.UtcNow);

            result.IsFailure.Should().BeTrue();
            result.Errors.Should().Contain(e => e.Code == "Task.Date.Default");
        }

        [Fact]
        public void Create_with_endtime_before_starttime_returns_failure()
        {
            var result = TaskItem.Create(
                userId: Guid.NewGuid(),
                date: new DateOnly(2025, 2, 20),
                title: "Title",
                description: null,
                startTime: new TimeOnly(10, 0),
                endTime: new TimeOnly(9, 0),
                location: null,
                travelTime: null,
                utcNow: DateTime.UtcNow);

            result.IsFailure.Should().BeTrue();
            result.Errors.Should().Contain(e => e.Code == "Task.Time.Invalid");
        }

        [Fact]
        public void Update_with_invalid_data_returns_failure_and_does_not_change_state()
        {
            var utcNow = DateTime.UtcNow;
            var task = TaskItem.Create(
                userId: Guid.NewGuid(),
                date: new DateOnly(2025, 2, 20),
                title: "Title",
                description: "Desc",
                startTime: new TimeOnly(9, 0),
                endTime: new TimeOnly(10, 0),
                location: "Office",
                travelTime: TimeSpan.FromMinutes(15),
                utcNow: utcNow).Value!;

            var originalTitle = task.Title;
            var originalDate = task.Date;
            var originalUpdatedAt = task.UpdatedAtUtc;

            var result = task.Update(
                title: "   ",
                date: default,
                description: "  ",
                startTime: new TimeOnly(10, 0),
                endTime: new TimeOnly(9, 0),
                location: null,
                travelTime: null,
                utcNow: utcNow.AddMinutes(5));

            result.IsFailure.Should().BeTrue();
            result.Errors.Should().Contain(e => e.Code == "Task.Title.Empty");
            result.Errors.Should().Contain(e => e.Code == "Task.Date.Default");
            result.Errors.Should().Contain(e => e.Code == "Task.Time.Invalid");

            task.Title.Should().Be(originalTitle);
            task.Date.Should().Be(originalDate);
            task.UpdatedAtUtc.Should().Be(originalUpdatedAt);
        }

        [Fact]
        public void Update_with_valid_data_updates_fields_and_timestamp()
        {
            var utcNow = new DateTime(2025, 2, 1, 12, 0, 0, DateTimeKind.Utc);
            var task = TaskItem.Create(
                userId: Guid.NewGuid(),
                date: new DateOnly(2025, 2, 20),
                title: "Title",
                description: null,
                startTime: new TimeOnly(9, 0),
                endTime: new TimeOnly(10, 0),
                location: null,
                travelTime: null,
                utcNow: utcNow).Value!;

            var later = utcNow.AddMinutes(5);
            var newDate = new DateOnly(2025, 2, 21);

            var result = task.Update(
                title: "  New title  ",
                date: newDate,
                description: "  New desc  ",
                startTime: new TimeOnly(10, 0),
                endTime: new TimeOnly(11, 0),
                location: "  Site  ",
                travelTime: TimeSpan.FromMinutes(30),
                utcNow: later);

            result.IsSuccess.Should().BeTrue();

            task.Title.Should().Be("New title");
            task.Description.Should().Be("New desc");
            task.Date.Should().Be(newDate);
            task.StartTime.Should().Be(new TimeOnly(10, 0));
            task.EndTime.Should().Be(new TimeOnly(11, 0));
            task.Location.Should().Be("Site");
            task.TravelTime.Should().Be(TimeSpan.FromMinutes(30));
            task.UpdatedAtUtc.Should().Be(later);
        }

        [Fact]
        public void Update_on_deleted_task_returns_failure()
        {
            var utcNow = DateTime.UtcNow;
            var task = TaskItem.Create(
                userId: Guid.NewGuid(),
                date: new DateOnly(2025, 2, 20),
                title: "Title",
                description: null,
                startTime: null,
                endTime: null,
                location: null,
                travelTime: null,
                utcNow: utcNow).Value!;

            task.SoftDelete(utcNow);

            var result = task.Update(
                title: "New title",
                date: new DateOnly(2025, 2, 21),
                description: null,
                startTime: null,
                endTime: null,
                location: null,
                travelTime: null,
                utcNow: utcNow.AddMinutes(5));

            result.IsFailure.Should().BeTrue();
            result.Errors.Should().Contain(e => e.Code == "Task.Deleted");
        }

        [Fact]
        public void MarkCompleted_sets_completed_and_is_idempotent()
        {
            var utcNow = new DateTime(2025, 2, 1, 12, 0, 0, DateTimeKind.Utc);
            var task = TaskItem.Create(
                userId: Guid.NewGuid(),
                date: new DateOnly(2025, 2, 20),
                title: "Title",
                description: null,
                startTime: null,
                endTime: null,
                location: null,
                travelTime: null,
                utcNow: utcNow).Value!;

            var completeTime = utcNow.AddMinutes(5);
            var result1 = task.MarkCompleted(completeTime);

            result1.IsSuccess.Should().BeTrue();
            task.IsCompleted.Should().BeTrue();
            task.UpdatedAtUtc.Should().Be(completeTime);

            var later = completeTime.AddMinutes(5);
            var result2 = task.MarkCompleted(later);

            result2.IsSuccess.Should().BeTrue();
            task.IsCompleted.Should().BeTrue();
            task.UpdatedAtUtc.Should().Be(completeTime); // unchanged (idempotent)
        }

        [Fact]
        public void MarkCompleted_on_deleted_task_returns_failure()
        {
            var utcNow = DateTime.UtcNow;
            var task = TaskItem.Create(
                userId: Guid.NewGuid(),
                date: new DateOnly(2025, 2, 20),
                title: "Title",
                description: null,
                startTime: null,
                endTime: null,
                location: null,
                travelTime: null,
                utcNow: utcNow).Value!;

            task.SoftDelete(utcNow);

            var result = task.MarkCompleted(utcNow.AddMinutes(1));

            result.IsFailure.Should().BeTrue();
            result.Errors.Should().Contain(e => e.Code == "Task.Deleted");
        }

        [Fact]
        public void MarkPending_clears_completed_and_is_idempotent()
        {
            var utcNow = DateTime.UtcNow;
            var task = TaskItem.Create(
                userId: Guid.NewGuid(),
                date: new DateOnly(2025, 2, 20),
                title: "Title",
                description: null,
                startTime: null,
                endTime: null,
                location: null,
                travelTime: null,
                utcNow: utcNow).Value!;

            task.MarkCompleted(utcNow.AddMinutes(1));

            var pendingTime = utcNow.AddMinutes(2);
            var result1 = task.MarkPending(pendingTime);

            result1.IsSuccess.Should().BeTrue();
            task.IsCompleted.Should().BeFalse();
            task.UpdatedAtUtc.Should().Be(pendingTime);

            var later = pendingTime.AddMinutes(1);
            var result2 = task.MarkPending(later);

            result2.IsSuccess.Should().BeTrue();
            task.IsCompleted.Should().BeFalse();
            task.UpdatedAtUtc.Should().Be(pendingTime); // unchanged
        }

        [Fact]
        public void MarkPending_on_deleted_task_returns_failure()
        {
            var utcNow = DateTime.UtcNow;
            var task = TaskItem.Create(
                userId: Guid.NewGuid(),
                date: new DateOnly(2025, 2, 20),
                title: "Title",
                description: null,
                startTime: null,
                endTime: null,
                location: null,
                travelTime: null,
                utcNow: utcNow).Value!;

            task.SoftDelete(utcNow);

            var result = task.MarkPending(utcNow.AddMinutes(1));

            result.IsFailure.Should().BeTrue();
            result.Errors.Should().Contain(e => e.Code == "Task.Deleted");
        }

        [Fact]
        public void SetReminder_updates_reminder_and_timestamp()
        {
            var utcNow = DateTime.UtcNow;
            var task = TaskItem.Create(
                userId: Guid.NewGuid(),
                date: new DateOnly(2025, 2, 20),
                title: "Title",
                description: null,
                startTime: null,
                endTime: null,
                location: null,
                travelTime: null,
                utcNow: utcNow).Value!;

            var reminder = utcNow.AddHours(1);
            var result = task.SetReminder(reminder, utcNow.AddMinutes(5));

            result.IsSuccess.Should().BeTrue();
            task.ReminderAtUtc.Should().Be(reminder);
        }

        [Fact]
        public void SetReminder_on_deleted_task_returns_failure()
        {
            var utcNow = DateTime.UtcNow;
            var task = TaskItem.Create(
                userId: Guid.NewGuid(),
                date: new DateOnly(2025, 2, 20),
                title: "Title",
                description: null,
                startTime: null,
                endTime: null,
                location: null,
                travelTime: null,
                utcNow: utcNow).Value!;

            task.SoftDelete(utcNow);

            var result = task.SetReminder(DateTime.UtcNow.AddHours(1), utcNow.AddMinutes(5));

            result.IsFailure.Should().BeTrue();
            result.Errors.Should().Contain(e => e.Code == "Task.Deleted");
        }

        [Fact]
        public void SoftDelete_and_RestoreTask_behave_idempotently()
        {
            var utcNow = DateTime.UtcNow;
            var task = TaskItem.Create(
                userId: Guid.NewGuid(),
                date: new DateOnly(2025, 2, 20),
                title: "Title",
                description: null,
                startTime: null,
                endTime: null,
                location: null,
                travelTime: null,
                utcNow: utcNow).Value!;

            var deleteTime = utcNow.AddMinutes(1);
            task.SoftDelete(deleteTime);
            task.IsDeleted.Should().BeTrue();

            // Second delete: idempotent
            var secondDeleteTime = deleteTime.AddMinutes(1);
            task.SoftDelete(secondDeleteTime);
            task.IsDeleted.Should().BeTrue();
            task.UpdatedAtUtc.Should().Be(deleteTime);

            var restoreTime = secondDeleteTime.AddMinutes(1);
            task.RestoreTask(restoreTime);
            task.IsDeleted.Should().BeFalse();

            // Second restore: idempotent
            var secondRestoreTime = restoreTime.AddMinutes(1);
            task.RestoreTask(secondRestoreTime);
            task.IsDeleted.Should().BeFalse();
            task.UpdatedAtUtc.Should().Be(restoreTime);
        }

        [Fact]
        public void GetDisplayTitle_returns_title_when_present()
        {
            var task = TaskItem.Create(
                userId: Guid.NewGuid(),
                date: new DateOnly(2025, 2, 20),
                title: "My task",
                description: null,
                startTime: null,
                endTime: null,
                location: null,
                travelTime: null,
                utcNow: DateTime.UtcNow).Value!;

            task.GetDisplayTitle().Should().Be("My task");
        }
    }
}
