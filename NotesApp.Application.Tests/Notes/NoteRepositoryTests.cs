using FluentAssertions;
using NotesApp.Application.Abstractions.Persistence;
using NotesApp.Application.Tests.Infrastructure;
using NotesApp.Domain.Entities;
using NotesApp.Infrastructure.Persistence.Repositories;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Application.Tests.Notes
{
    /// <summary>
    /// Tests for NoteRepository using a SQL Server test AppDbContext.
    /// </summary>
    public sealed class NoteRepositoryTests
    {
        [Fact]
        public async Task GetForDayAsync_returns_only_notes_for_given_user_and_date()
        {
            // Arrange
            await using var context = SqlServerAppDbContextFactory.CreateContext();

            INoteRepository noteRepository = new NoteRepository(context);

            var userId = Guid.NewGuid();
            var otherUserId = Guid.NewGuid();

            var date = new DateOnly(2025, 2, 20);
            var otherDate = new DateOnly(2025, 2, 21);

            // CHANGED: content parameter removed from Note.Create
            // Notes for current user on date
            var n1 = Note.Create(userId, date, "T1", null, null, DateTime.UtcNow).Value!;
            var n2 = Note.Create(userId, date, "T2", null, null, DateTime.UtcNow).Value!;

            // Note for current user on another date
            var nOtherDate = Note.Create(userId, otherDate, "T3", null, null, DateTime.UtcNow).Value!;

            // Note for another user on same date
            var nOtherUser = Note.Create(otherUserId, date, "T4", null, null, DateTime.UtcNow).Value!; ;

            await context.Notes.AddRangeAsync(n1, n2, nOtherDate, nOtherUser);
            await context.SaveChangesAsync();

            // Act
            var result = await noteRepository.GetForDayAsync(userId, date, CancellationToken.None);

            // Assert
            var list = result.ToList();
            list.Should().HaveCount(2);
            list.Select(n => n.Id).Should().BeEquivalentTo(new[] { n1.Id, n2.Id });
        }

        [Fact]
        public async Task GetForDateRangeAsync_respects_user_and_range_boundaries()
        {
            // Arrange
            await using var context = SqlServerAppDbContextFactory.CreateContext();

            INoteRepository noteRepository = new NoteRepository(context);

            var userId = Guid.NewGuid();
            var otherUserId = Guid.NewGuid();

            var start = new DateOnly(2025, 2, 20);
            var endExclusive = new DateOnly(2025, 2, 23);

            // CHANGED: content parameter removed from Note.Create
            // In-range for current user: 20,21,22
            var n1 = Note.Create(userId, new DateOnly(2025, 2, 20), "D20", null, null, DateTime.UtcNow).Value!;
            var n2 = Note.Create(userId, new DateOnly(2025, 2, 21), "D21", null, null, DateTime.UtcNow).Value!;
            var n3 = Note.Create(userId, new DateOnly(2025, 2, 22), "D22", null, null, DateTime.UtcNow).Value!;

            // Out-of-range for current user
            var beforeRange = Note.Create(userId, new DateOnly(2025, 2, 19), "Before", null, null, DateTime.UtcNow).Value!;
            var afterRange = Note.Create(userId, new DateOnly(2025, 2, 23), "After", null, null, DateTime.UtcNow).Value!;

            // In-range for other user
            var otherUserInRange = Note.Create(otherUserId, new DateOnly(2025, 2, 21), "Other", null, null, DateTime.UtcNow).Value!; ;

            await context.Notes.AddRangeAsync(n1, n2, n3, beforeRange, afterRange, otherUserInRange);
            await context.SaveChangesAsync();

            // Act
            var result = await noteRepository.GetForDateRangeAsync(userId, start, endExclusive, CancellationToken.None);

            // Assert
            var list = result.ToList();
            list.Should().HaveCount(3);
            list.Select(n => n.Date).Should().BeEquivalentTo(new[]
            {
                new DateOnly(2025, 2, 20),
                new DateOnly(2025, 2, 21),
                new DateOnly(2025, 2, 22)
            });
        }
    }
}
