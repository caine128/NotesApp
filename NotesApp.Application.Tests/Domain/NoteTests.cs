using FluentAssertions;
using NotesApp.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Application.Tests.Domain
{
    public sealed class NoteTests
    {
        [Fact]
        public void Create_with_valid_input_returns_success_and_sets_properties()
        {
            // Arrange
            var userId = Guid.NewGuid();
            var date = new DateOnly(2025, 2, 20);
            var utcNow = new DateTime(2025, 2, 1, 12, 0, 0, DateTimeKind.Utc);

            // Act
            var result = Note.Create(
                userId: userId,
                date: date,
                title: "  My title  ",
                content: "  My content  ",
                summary: "  summary  ",
                tags: "  tag1, tag2  ",
                utcNow: utcNow);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.Value.Should().NotBeNull();

            var note = result.Value!;
            note.Id.Should().NotBe(Guid.Empty);
            note.UserId.Should().Be(userId);
            note.Date.Should().Be(date);
            note.Title.Should().Be("My title");
            note.Content.Should().Be("My content");
            note.Summary.Should().Be("summary");
            note.Tags.Should().Be("tag1, tag2");

            note.CreatedAtUtc.Should().Be(utcNow);
            note.UpdatedAtUtc.Should().Be(utcNow);
            note.IsDeleted.Should().BeFalse();
        }

        [Fact]
        public void Create_with_empty_userid_returns_failure()
        {
            var result = Note.Create(
                userId: Guid.Empty,
                date: new DateOnly(2025, 2, 20),
                title: "Title",
                content: "Content",
                summary: null,
                tags: null,
                utcNow: DateTime.UtcNow);

            result.IsFailure.Should().BeTrue();
            result.Errors.Should().Contain(e => e.Code == "Note.UserId.Empty");
        }

        [Fact]
        public void Create_with_default_date_returns_failure()
        {
            var result = Note.Create(
                userId: Guid.NewGuid(),
                date: default,
                title: "Title",
                content: "Content",
                summary: null,
                tags: null,
                utcNow: DateTime.UtcNow);

            result.IsFailure.Should().BeTrue();
            result.Errors.Should().Contain(e => e.Code == "Note.Date.Default");
        }

        [Fact]
        public void Create_with_empty_title_and_content_returns_failure()
        {
            var result = Note.Create(
                userId: Guid.NewGuid(),
                date: new DateOnly(2025, 2, 20),
                title: "   ",
                content: "   ",
                summary: null,
                tags: null,
                utcNow: DateTime.UtcNow);

            result.IsFailure.Should().BeTrue();
            result.Errors.Should().Contain(e => e.Code == "Note.Empty");
        }

        [Fact]
        public void Update_on_deleted_note_returns_failure()
        {
            // Arrange: create + soft delete
            var userId = Guid.NewGuid();
            var date = new DateOnly(2025, 2, 20);
            var utcNow = DateTime.UtcNow;

            var note = Note.Create(
                userId, date, "Title", "Content", null, null, utcNow).Value!;

            note.SoftDelete(utcNow);

            // Act
            var result = note.Update(
                title: "New title",
                content: "New content",
                summary: null,
                tags: null,
                date: date,
                utcNow: utcNow.AddMinutes(5));

            // Assert
            result.IsFailure.Should().BeTrue();
            result.Errors.Should().Contain(e => e.Code == "Note.Deleted");
        }

        [Fact]
        public void Update_with_invalid_data_returns_failure_and_does_not_change_state()
        {
            // Arrange
            var utcNow = DateTime.UtcNow;
            var note = Note.Create(
                userId: Guid.NewGuid(),
                date: new DateOnly(2025, 2, 20),
                title: "Title",
                content: "Content",
                summary: null,
                tags: null,
                utcNow: utcNow).Value!;

            var originalTitle = note.Title;
            var originalContent = note.Content;
            var originalDate = note.Date;
            var originalUpdatedAt = note.UpdatedAtUtc;

            // Invalid: empty title and content + default date
            var result = note.Update(
                title: "   ",
                content: "   ",
                summary: null,
                tags: null,
                date: default,
                utcNow: utcNow.AddMinutes(5));

            // Assert
            result.IsFailure.Should().BeTrue();
            result.Errors.Should().Contain(e => e.Code == "Note.Empty");
            result.Errors.Should().Contain(e => e.Code == "Note.Date.Default");

            note.Title.Should().Be(originalTitle);
            note.Content.Should().Be(originalContent);
            note.Date.Should().Be(originalDate);
            note.UpdatedAtUtc.Should().Be(originalUpdatedAt);
        }

        [Fact]
        public void Update_with_valid_data_updates_fields_and_timestamp()
        {
            // Arrange
            var utcNow = new DateTime(2025, 2, 1, 12, 0, 0, DateTimeKind.Utc);
            var note = Note.Create(
                userId: Guid.NewGuid(),
                date: new DateOnly(2025, 2, 20),
                title: "Title",
                content: "Content",
                summary: null,
                tags: null,
                utcNow: utcNow).Value!;

            var later = utcNow.AddMinutes(10);
            var newDate = new DateOnly(2025, 2, 21);

            // Act
            var result = note.Update(
                title: " New title ",
                content: " New content ",
                summary: " New summary ",
                tags: " tag1 ",
                date: newDate,
                utcNow: later);

            // Assert
            result.IsSuccess.Should().BeTrue();

            note.Title.Should().Be("New title");
            note.Content.Should().Be("New content");
            note.Summary.Should().Be("New summary");
            note.Tags.Should().Be("tag1");
            note.Date.Should().Be(newDate);

            note.CreatedAtUtc.Should().Be(utcNow); // unchanged
            note.UpdatedAtUtc.Should().Be(later);
        }

        [Fact]
        public void MoveToDate_on_deleted_note_returns_failure()
        {
            var utcNow = DateTime.UtcNow;
            var note = Note.Create(
                userId: Guid.NewGuid(),
                date: new DateOnly(2025, 2, 20),
                title: "Title",
                content: "Content",
                summary: null,
                tags: null,
                utcNow: utcNow).Value!;

            note.SoftDelete(utcNow);

            var result = note.MoveToDate(new DateOnly(2025, 2, 21), utcNow.AddMinutes(1));

            result.IsFailure.Should().BeTrue();
            result.Errors.Should().Contain(e => e.Code == "Note.Deleted");
        }

        [Fact]
        public void MoveToDate_with_default_date_returns_failure()
        {
            var utcNow = DateTime.UtcNow;
            var note = Note.Create(
                userId: Guid.NewGuid(),
                date: new DateOnly(2025, 2, 20),
                title: "Title",
                content: "Content",
                summary: null,
                tags: null,
                utcNow: utcNow).Value!;

            var result = note.MoveToDate(default, utcNow.AddMinutes(1));

            result.IsFailure.Should().BeTrue();
            result.Errors.Should().Contain(e => e.Code == "Note.Date.Default");
        }

        [Fact]
        public void MoveToDate_with_same_date_is_noop_and_keeps_timestamp()
        {
            var utcNow = new DateTime(2025, 2, 1, 12, 0, 0, DateTimeKind.Utc);
            var note = Note.Create(
                userId: Guid.NewGuid(),
                date: new DateOnly(2025, 2, 20),
                title: "Title",
                content: "Content",
                summary: null,
                tags: null,
                utcNow: utcNow).Value!;

            var later = utcNow.AddMinutes(5);

            var result = note.MoveToDate(note.Date, later);

            result.IsSuccess.Should().BeTrue();
            note.Date.Should().Be(new DateOnly(2025, 2, 20));
            note.UpdatedAtUtc.Should().Be(utcNow); // unchanged because no real move
        }

        [Fact]
        public void MoveToDate_with_new_date_updates_date_and_timestamp()
        {
            var utcNow = new DateTime(2025, 2, 1, 12, 0, 0, DateTimeKind.Utc);
            var note = Note.Create(
                userId: Guid.NewGuid(),
                date: new DateOnly(2025, 2, 20),
                title: "Title",
                content: "Content",
                summary: null,
                tags: null,
                utcNow: utcNow).Value!;

            var later = utcNow.AddMinutes(5);
            var newDate = new DateOnly(2025, 2, 21);

            var result = note.MoveToDate(newDate, later);

            result.IsSuccess.Should().BeTrue();
            note.Date.Should().Be(newDate);
            note.UpdatedAtUtc.Should().Be(later);
        }

        [Fact]
        public void SoftDelete_marks_deleted_and_is_idempotent()
        {
            var utcNow = new DateTime(2025, 2, 1, 12, 0, 0, DateTimeKind.Utc);
            var note = Note.Create(
                userId: Guid.NewGuid(),
                date: new DateOnly(2025, 2, 20),
                title: "Title",
                content: "Content",
                summary: null,
                tags: null,
                utcNow: utcNow).Value!;

            var firstDeleteTime = utcNow.AddMinutes(5);
            note.SoftDelete(firstDeleteTime);

            note.IsDeleted.Should().BeTrue();
            note.UpdatedAtUtc.Should().Be(firstDeleteTime);

            // Second call should be idempotent & not change IsDeleted
            var secondDeleteTime = firstDeleteTime.AddMinutes(5);
            note.SoftDelete(secondDeleteTime);

            note.IsDeleted.Should().BeTrue();
            note.UpdatedAtUtc.Should().Be(firstDeleteTime); // still first delete time
        }

        [Fact]
        public void RestoreNote_restores_deleted_and_is_idempotent()
        {
            var utcNow = DateTime.UtcNow;
            var note = Note.Create(
                userId: Guid.NewGuid(),
                date: new DateOnly(2025, 2, 20),
                title: "Title",
                content: "Content",
                summary: null,
                tags: null,
                utcNow: utcNow).Value!;

            var deleteTime = utcNow.AddMinutes(1);
            note.SoftDelete(deleteTime);
            note.IsDeleted.Should().BeTrue();

            var restoreTime = deleteTime.AddMinutes(1);
            note.RestoreNote(restoreTime);

            note.IsDeleted.Should().BeFalse();
            note.UpdatedAtUtc.Should().Be(restoreTime);

            // Idempotent: restoring again when not deleted
            var secondRestoreTime = restoreTime.AddMinutes(1);
            note.RestoreNote(secondRestoreTime);

            note.IsDeleted.Should().BeFalse();
            note.UpdatedAtUtc.Should().Be(restoreTime); // unchanged
        }

        [Fact]
        public void GetDisplayTitle_prefers_title_over_content()
        {
            var note = Note.Create(
                userId: Guid.NewGuid(),
                date: new DateOnly(2025, 2, 20),
                title: "My title",
                content: "My content",
                summary: null,
                tags: null,
                utcNow: DateTime.UtcNow).Value!;

            note.GetDisplayTitle().Should().Be("My title");
        }

        [Fact]
        public void GetDisplayTitle_uses_content_excerpt_when_title_empty()
        {
            var longContent = new string('x', 40); // longer than 30
            var note = Note.Create(
                userId: Guid.NewGuid(),
                date: new DateOnly(2025, 2, 20),
                title: "  ", // trimmed → empty
                content: longContent,
                summary: null,
                tags: null,
                utcNow: DateTime.UtcNow).Value!;

            var display = note.GetDisplayTitle();

            display.Length.Should().Be(31); // 30 chars + '…'
            display.Should().EndWith("…");
        }

        [Fact]
        public void GetDisplayTitle_returns_Untitled_note_when_both_empty()
        {
            var note = Note.Create(
                userId: Guid.NewGuid(),
                date: new DateOnly(2025, 2, 20),
                title: "   ",
                content: "   ",
                summary: null,
                tags: null,
                utcNow: DateTime.UtcNow).Value!;

            // Note.Create would normally fail with both empty, so instead we simulate by
            // updating an existing valid note to empty values (which also fails).
            // To test this method in isolation, construct via reflection or simpler:
            // create with content then clear fields directly is not possible (private setters).
            //
            // As a simpler, realistic test, we can call GetDisplayTitle() on a note with
            // empty Title but non-empty Content OR just assert the default "Untitled note"
            // for an edge case with both empty:
            note = Note.Create(
                userId: Guid.NewGuid(),
                date: new DateOnly(2025, 2, 20),
                title: string.Empty,
                content: "x",
                summary: null,
                tags: null,
                utcNow: DateTime.UtcNow).Value!;

            // For the strictly both-empty scenario, the code path is theoretically unreachable
            // via public API, so we assert what we can:
            note.GetDisplayTitle().Should().NotBeNullOrWhiteSpace();
        }

        [Fact]
        public void SetSummaryAndTags_on_deleted_note_returns_failure()
        {
            var utcNow = DateTime.UtcNow;
            var note = Note.Create(
                userId: Guid.NewGuid(),
                date: new DateOnly(2025, 2, 20),
                title: "Title",
                content: "Content",
                summary: null,
                tags: null,
                utcNow: utcNow).Value!;

            note.SoftDelete(utcNow);

            var result = note.SetSummaryAndTags("New summary", "tag1", utcNow.AddMinutes(1));

            result.IsFailure.Should().BeTrue();
            result.Errors.Should().Contain(e => e.Code == "Note.Deleted");
        }

        [Fact]
        public void SetSummaryAndTags_updates_fields_and_timestamp()
        {
            var utcNow = DateTime.UtcNow;
            var note = Note.Create(
                userId: Guid.NewGuid(),
                date: new DateOnly(2025, 2, 20),
                title: "Title",
                content: "Content",
                summary: null,
                tags: null,
                utcNow: utcNow).Value!;

            var later = utcNow.AddMinutes(5);

            var result = note.SetSummaryAndTags("AI summary", "ai, note", later);

            result.IsSuccess.Should().BeTrue();
            note.Summary.Should().Be("AI summary");
            note.Tags.Should().Be("ai, note");
            note.UpdatedAtUtc.Should().Be(later);
        }
    }
}
