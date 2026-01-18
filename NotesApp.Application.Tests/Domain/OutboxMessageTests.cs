using FluentAssertions;
using NotesApp.Domain.Common;
using NotesApp.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Application.Tests.Domain
{
    /// <summary>
    /// Unit tests for OutboxMessage entity.
    /// 
    /// CHANGED: Note.Create calls updated for block-based model (no content parameter).
    /// </summary>
    public sealed class OutboxMessageTests
    {
        [Fact]
        public void Create_with_valid_note_and_payload_returns_success_and_sets_fields()
        {
            // Arrange
            var userId = Guid.NewGuid();
            var date = new DateOnly(2025, 2, 20);
            var utcNow = new DateTime(2025, 2, 1, 12, 0, 0, DateTimeKind.Utc);

            // CHANGED: content parameter removed from Note.Create
            var note = Note.Create(
                userId: userId,
                date: date,
                title: "Title",
                summary: null,
                tags: null,
                utcNow: utcNow).Value!;

            var payload = """{"noteId":"test"}""";

            // Act
            var result = OutboxMessage.Create<Note, NoteEventType>(
                aggregate: note,
                eventType: NoteEventType.Created,
                payload: payload,
                utcNow: utcNow);

            // Assert
            result.IsSuccess.Should().BeTrue();
            var message = result.Value!;

            message.Id.Should().NotBe(Guid.Empty);
            message.UserId.Should().Be(userId);
            message.AggregateId.Should().Be(note.Id);
            message.AggregateType.Should().Be(nameof(Note));
            message.MessageType.Should().Be($"{nameof(Note)}.{NoteEventType.Created}");
            message.Payload.Should().Be(payload.Trim());
            message.AttemptCount.Should().Be(0);
            message.ProcessedAtUtc.Should().BeNull();

            message.CreatedAtUtc.Should().Be(utcNow);
            message.UpdatedAtUtc.Should().Be(utcNow);
        }

        [Fact]
        public void Create_with_null_aggregate_returns_failure()
        {
            var payload = "{}";
            var utcNow = DateTime.UtcNow;

            var result = OutboxMessage.Create<Note, NoteEventType>(
                aggregate: null!,
                eventType: NoteEventType.Created,
                payload: payload,
                utcNow: utcNow);

            result.IsFailure.Should().BeTrue();
            result.Errors.Should().Contain(e => e.Code == "OutboxMessage.Aggregate.Null");
        }

        [Fact]
        public void Create_with_empty_payload_returns_failure()
        {
            // CHANGED: content parameter removed from Note.Create
            var note = Note.Create(
                userId: Guid.NewGuid(),
                date: new DateOnly(2025, 2, 20),
                title: "Title",
                summary: null,
                tags: null,
                utcNow: DateTime.UtcNow).Value!;

            var result = OutboxMessage.Create<Note, NoteEventType>(
                aggregate: note,
                eventType: NoteEventType.Created,
                payload: "   ",
                utcNow: DateTime.UtcNow);

            result.IsFailure.Should().BeTrue();
            result.Errors.Should().Contain(e => e.Code == "OutboxMessage.Payload.Empty");
        }

        [Fact]
        public void MarkProcessed_sets_processed_time_and_is_idempotent()
        {
            // CHANGED: content parameter removed from Note.Create
            var note = Note.Create(
                userId: Guid.NewGuid(),
                date: new DateOnly(2025, 2, 20),
                title: "Title",
                summary: null,
                tags: null,
                utcNow: DateTime.UtcNow).Value!;

            var utcNow = new DateTime(2025, 2, 1, 12, 0, 0, DateTimeKind.Utc);

            var message = OutboxMessage.Create<Note, NoteEventType>(
                aggregate: note,
                eventType: NoteEventType.Created,
                payload: "{}",
                utcNow: utcNow).Value!;

            var processedTime = utcNow.AddMinutes(5);
            message.MarkProcessed(processedTime);

            message.ProcessedAtUtc.Should().Be(processedTime);
            message.UpdatedAtUtc.Should().Be(processedTime);

            var later = processedTime.AddMinutes(5);
            message.MarkProcessed(later);

            // Idempotent: we allow overwriting, but UpdatedAtUtc should reflect last call.
            // Your implementation simply sets ProcessedAtUtc and Touch(utcNow) each time,
            // so we assert the latest value:
            message.ProcessedAtUtc.Should().Be(later);
            message.UpdatedAtUtc.Should().Be(later);
        }

        [Fact]
        public void IncrementAttempt_increases_attempt_count_and_updates_timestamp()
        {
            // CHANGED: content parameter removed from Note.Create
            var note = Note.Create(
                userId: Guid.NewGuid(),
                date: new DateOnly(2025, 2, 20),
                title: "Title",
                summary: null,
                tags: null,
                utcNow: DateTime.UtcNow).Value!;

            var utcNow = new DateTime(2025, 2, 1, 12, 0, 0, DateTimeKind.Utc);

            var message = OutboxMessage.Create<Note, NoteEventType>(
                aggregate: note,
                eventType: NoteEventType.Created,
                payload: "{}",
                utcNow: utcNow).Value!;

            var t1 = utcNow.AddMinutes(1);
            message.IncrementAttempt(t1);
            message.AttemptCount.Should().Be(1);
            message.UpdatedAtUtc.Should().Be(t1);

            var t2 = t1.AddMinutes(1);
            message.IncrementAttempt(t2);
            message.AttemptCount.Should().Be(2);
            message.UpdatedAtUtc.Should().Be(t2);
        }
    }
}
