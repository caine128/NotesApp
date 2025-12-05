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
            var date = new DateOnly(2024, 1, 2);
            var now = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc);

            // Act
            var result = Note.Create(
                userId: userId,
                date: date,
                title: "  Title  ",
                content: "  Content  ",
                summary: "  Summary  ",
                tags: "  tag1, tag2  ",
                utcNow: now);

            // Assert
            result.IsSuccess.Should().BeTrue();
            var note = result.Value;

            note.UserId.Should().Be(userId);
            note.Date.Should().Be(date);
            note.Title.Should().Be("Title");
            note.Content.Should().Be("Content");
            note.Summary.Should().Be("Summary");
            note.Tags.Should().Be("tag1, tag2");
            note.Version.Should().Be(1);
            note.IsDeleted.Should().BeFalse();
        }

        [Fact]
        public void Create_with_empty_title_and_content_returns_failure()
        {
            var userId = Guid.NewGuid();
            var date = new DateOnly(2024, 1, 2);
            var now = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc);

            var result = Note.Create(
                userId: userId,
                date: date,
                title: "   ",
                content: "   ",
                summary: null,
                tags: null,
                utcNow: now);

            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().NotBeEmpty();
        }

        [Fact]
        public void Version_starts_at_one_for_new_note()
        {
            var now = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc);

            var result = Note.Create(
                userId: Guid.NewGuid(),
                date: new DateOnly(2024, 1, 2),
                title: "Title",
                content: "Content",
                summary: null,
                tags: null,
                utcNow: now);

            result.IsSuccess.Should().BeTrue();
            var note = result.Value;

            note.Version.Should().Be(1);
        }

        [Fact]
        public void Version_increments_on_update()
        {
            var now = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc);

            var result = Note.Create(
                userId: Guid.NewGuid(),
                date: new DateOnly(2024, 1, 2),
                title: "Title",
                content: "Content",
                summary: null,
                tags: null,
                utcNow: now);

            result.IsSuccess.Should().BeTrue();
            var note = result.Value;

            var initialVersion = note.Version;
            var initialUpdatedAt = note.UpdatedAtUtc;

            var updateResult = note.Update(
                title: "New Title",
                content: "New Content",
                summary: "New Summary",
                tags: "tag1,tag2",
                date: new DateOnly(2024, 1, 3),
                utcNow: now.AddMinutes(1));

            updateResult.IsSuccess.Should().BeTrue();

            note.Version.Should().Be(initialVersion + 1);
            note.UpdatedAtUtc.Should().BeAfter(initialUpdatedAt);
            note.Title.Should().Be("New Title");
            note.Content.Should().Be("New Content");
            note.Summary.Should().Be("New Summary");
            note.Tags.Should().Be("tag1,tag2");
        }

        [Fact]
        public void MoveToDate_changes_date_and_increments_version_only_when_date_differs()
        {
            var now = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc);
            var initialDate = new DateOnly(2024, 1, 2);
            var newDate = new DateOnly(2024, 1, 3);

            var result = Note.Create(
                userId: Guid.NewGuid(),
                date: initialDate,
                title: "Title",
                content: "Content",
                summary: null,
                tags: null,
                utcNow: now);

            result.IsSuccess.Should().BeTrue();
            var note = result.Value;

            var initialVersion = note.Version;
            var initialUpdatedAt = note.UpdatedAtUtc;

            // Moving to a different date
            var moveResult1 = note.MoveToDate(newDate, now.AddMinutes(1));
            moveResult1.IsSuccess.Should().BeTrue();

            note.Date.Should().Be(newDate);
            note.Version.Should().Be(initialVersion + 1);
            note.UpdatedAtUtc.Should().BeAfter(initialUpdatedAt);

            var afterMoveVersion = note.Version;
            var afterMoveUpdatedAt = note.UpdatedAtUtc;

            // Moving to the same date: no-op
            var moveResult2 = note.MoveToDate(newDate, now.AddMinutes(2));
            moveResult2.IsSuccess.Should().BeTrue();

            note.Date.Should().Be(newDate);
            note.Version.Should().Be(afterMoveVersion);
            note.UpdatedAtUtc.Should().Be(afterMoveUpdatedAt);
        }

        [Fact]
        public void SoftDelete_and_RestoreNote_are_idempotent_and_increment_version_once_each()
        {
            var now = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc);

            var result = Note.Create(
                userId: Guid.NewGuid(),
                date: new DateOnly(2024, 1, 2),
                title: "Title",
                content: "Content",
                summary: null,
                tags: null,
                utcNow: now);

            result.IsSuccess.Should().BeTrue();
            var note = result.Value;

            var initialVersion = note.Version;

            var deleteResult1 = note.SoftDelete(now.AddMinutes(1));
            deleteResult1.IsSuccess.Should().BeTrue();
            note.IsDeleted.Should().BeTrue();
            note.Version.Should().Be(initialVersion + 1);

            // Second delete is no-op
            var deleteResult2 = note.SoftDelete(now.AddMinutes(2));
            deleteResult2.IsSuccess.Should().BeTrue();
            note.Version.Should().Be(initialVersion + 1);

            var restoreResult1 = note.RestoreNote(now.AddMinutes(3));
            restoreResult1.IsSuccess.Should().BeTrue();
            note.IsDeleted.Should().BeFalse();
            note.Version.Should().Be(initialVersion + 2);

            // Second restore is no-op
            var restoreResult2 = note.RestoreNote(now.AddMinutes(4));
            restoreResult2.IsSuccess.Should().BeTrue();
            note.Version.Should().Be(initialVersion + 2);
        }

        [Fact]
        public void SetSummaryAndTags_always_increments_version_on_success()
        {
            var now = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc);

            var result = Note.Create(
                userId: Guid.NewGuid(),
                date: new DateOnly(2024, 1, 2),
                title: "Title",
                content: "Content",
                summary: null,
                tags: null,
                utcNow: now);

            result.IsSuccess.Should().BeTrue();
            var note = result.Value;

            var initialVersion = note.Version;

            var setResult1 = note.SetSummaryAndTags("Summary", "tag1,tag2", now.AddMinutes(1));
            setResult1.IsSuccess.Should().BeTrue();
            note.Version.Should().Be(initialVersion + 1);
            note.Summary.Should().Be("Summary");
            note.Tags.Should().Be("tag1,tag2");

            var afterFirstSetVersion = note.Version;

            // Calling with same values still increments version
            var setResult2 = note.SetSummaryAndTags("Summary", "tag1,tag2", now.AddMinutes(2));
            setResult2.IsSuccess.Should().BeTrue();
            note.Version.Should().Be(afterFirstSetVersion + 1);
        }

        [Fact]
        public void GetDisplayTitle_prefers_title_then_content_snippet_then_fallback()
        {
            var now = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc);
            var userId = Guid.NewGuid();
            var date = new DateOnly(2024, 1, 2);

            // With title
            var withTitleResult = Note.Create(
                userId,
                date,
                title: "My Title",
                content: "Content",
                summary: null,
                tags: null,
                utcNow: now);

            withTitleResult.IsSuccess.Should().BeTrue();
            withTitleResult.Value.GetDisplayTitle().Should().Be("My Title");

            // Without title but with content
            var withContentResult = Note.Create(
                userId,
                date,
                title: "",
                content: "Content only",
                summary: null,
                tags: null,
                utcNow: now);

            withContentResult.IsSuccess.Should().BeTrue();
            withContentResult.Value.GetDisplayTitle().Should().StartWith("Content");

            // Neither title nor content (this state shouldn’t normally exist due to invariants,
            // but if it did, we still expect a fallback)
            var emptyNote = Note.Create(
                userId,
                date,
                title: "Title",
                content: "Content",
                summary: null,
                tags: null,
                utcNow: now).Value;

            // Force empty for safety test
            typeof(Note).GetProperty(nameof(Note.Title))!.SetValue(emptyNote, string.Empty);
            typeof(Note).GetProperty(nameof(Note.Content))!.SetValue(emptyNote, string.Empty);

            emptyNote.GetDisplayTitle().Should().Be("Untitled note");
        }
    }
}
