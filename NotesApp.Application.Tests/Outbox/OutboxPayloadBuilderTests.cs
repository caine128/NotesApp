using FluentAssertions;
using NotesApp.Application.Common;
using NotesApp.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;

namespace NotesApp.Application.Tests.Outbox
{
    public sealed class OutboxPayloadBuilderTests
    {
        [Fact]
        public void BuildTaskPayload_includes_expected_fields_and_origin_device_id()
        {
            // Arrange
            var userId = Guid.NewGuid();
            var originDeviceId = Guid.NewGuid();
            var now = new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc);

            var createResult = TaskItem.Create(
                userId,
                new DateOnly(2025, 1, 2),
                "Task title",
                "Task description",
                startTime: null,
                endTime: null,
                location: "Office",
                travelTime: null,
                utcNow: now);

            createResult.IsSuccess.Should().BeTrue();
            var task = createResult.Value;

            task.SetReminder(now.AddHours(1), now);

            // Act
            var json = OutboxPayloadBuilder.BuildTaskPayload(task, originDeviceId);

            // Assert
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            root.GetProperty("TaskId").GetGuid().Should().Be(task.Id);
            root.GetProperty("UserId").GetGuid().Should().Be(task.UserId);
            root.GetProperty("Date").GetDateTime().Date.Should().Be(task.Date.ToDateTime(TimeOnly.MinValue).Date);
            root.GetProperty("Title").GetString().Should().Be(task.Title);
            root.GetProperty("IsCompleted").GetBoolean().Should().Be(task.IsCompleted);
            root.GetProperty("Version").GetInt64().Should().Be(task.Version);
            root.GetProperty("OriginDeviceId").GetGuid().Should().Be(originDeviceId);

            if (task.ReminderAtUtc is not null)
            {
                root.GetProperty("ReminderAtUtc").GetDateTime().Should().Be(task.ReminderAtUtc.Value);
            }
            else
            {
                root.TryGetProperty("ReminderAtUtc", out _).Should().BeFalse();
            }

            // And we deliberately do NOT expose internal / heavy fields
            root.TryGetProperty("Description", out _).Should().BeFalse();
            root.TryGetProperty("StartTime", out _).Should().BeFalse();
            root.TryGetProperty("EndTime", out _).Should().BeFalse();
            root.TryGetProperty("Location", out _).Should().BeFalse();
            root.TryGetProperty("TravelTime", out _).Should().BeFalse();
            root.TryGetProperty("CreatedAtUtc", out _).Should().BeFalse();
            root.TryGetProperty("UpdatedAtUtc", out _).Should().BeFalse();
            root.TryGetProperty("IsDeleted", out _).Should().BeFalse();
            root.TryGetProperty("RowVersion", out _).Should().BeFalse();
            root.TryGetProperty("ReminderAcknowledgedAtUtc", out _).Should().BeFalse();
            root.TryGetProperty("ReminderSentAtUtc", out _).Should().BeFalse();
        }

        [Fact]
        public void BuildNotePayload_includes_expected_fields_and_origin_device_id()
        {
            // Arrange
            var userId = Guid.NewGuid();
            var originDeviceId = Guid.NewGuid();
            var now = new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc);

            var createResult = Note.Create(
                userId,
                new DateOnly(2025, 1, 3),
                title: "Note title",
                content: "Note content",
                summary: "Summary",
                tags: "tag1,tag2",
                utcNow: now);

            createResult.IsSuccess.Should().BeTrue();
            var note = createResult.Value;

            // Act
            var json = OutboxPayloadBuilder.BuildNotePayload(note, originDeviceId);

            // Assert
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            root.GetProperty("NoteId").GetGuid().Should().Be(note.Id);
            root.GetProperty("UserId").GetGuid().Should().Be(note.UserId);
            root.GetProperty("Date").GetDateTime().Date.Should().Be(note.Date.ToDateTime(TimeOnly.MinValue).Date);
            root.GetProperty("Title").GetString().Should().Be(note.Title);
            root.GetProperty("Version").GetInt64().Should().Be(note.Version);
            root.GetProperty("OriginDeviceId").GetGuid().Should().Be(originDeviceId);

            // And again we do NOT expose full content or internal/audit fields
            root.TryGetProperty("Content", out _).Should().BeFalse();
            root.TryGetProperty("Summary", out _).Should().BeFalse();
            root.TryGetProperty("Tags", out _).Should().BeFalse();
            root.TryGetProperty("CreatedAtUtc", out _).Should().BeFalse();
            root.TryGetProperty("UpdatedAtUtc", out _).Should().BeFalse();
            root.TryGetProperty("IsDeleted", out _).Should().BeFalse();
            root.TryGetProperty("RowVersion", out _).Should().BeFalse();
        }
    }
}
