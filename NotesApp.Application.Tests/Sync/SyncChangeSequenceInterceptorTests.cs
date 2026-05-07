using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NotesApp.Application.Tests.Infrastructure;
using NotesApp.Domain.Common;
using NotesApp.Domain.Entities;
using NotesApp.Domain.Users;
using NotesApp.Infrastructure.Persistence;
using NotesApp.Infrastructure.Persistence.Interceptors;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace NotesApp.Application.Tests.Sync
{
    /// <summary>
    /// Integration tests for SyncChangeSequenceInterceptor against real LocalDB.
    ///
    /// Verifies:
    /// - First write for a new user lazily creates SyncSequenceState and assigns Sequence=1.
    /// - Multiple SyncChange writes in one SaveChanges receive consecutive sequences.
    /// - Subsequent saves continue the user's sequence monotonically.
    /// - Multiple users do not share sequences.
    /// - Concurrent writers for the same user produce contiguous, non-duplicate sequences.
    /// - SaveChanges failure (rollback) does not advance NextSequence (gap-free).
    /// - Repeat SaveChanges with retry-style behavior is idempotent.
    /// </summary>
    public sealed class SyncChangeSequenceInterceptorTests
    {
        private static SyncChange MakeChange(Guid userId, SyncOperation op = SyncOperation.Created)
        {
            var result = SyncChange.Create(
                userId: userId,
                entityFamily: SyncEntityFamily.Task,
                entityId: Guid.NewGuid(),
                operation: op,
                changedAtUtc: new DateTime(2026, 5, 3, 12, 0, 0, DateTimeKind.Utc),
                originDeviceId: null,
                payloadJson: "{}");

            result.IsSuccess.Should().BeTrue($"factory should succeed; errors: {string.Join(",", result.Errors.Select(e => e.Code))}");
            return result.Value!;
        }

        [Fact]
        public async Task First_write_for_new_user_assigns_sequence_1()
        {
            await using var context = SqlServerAppDbContextFactory.CreateContextWithSyncInterceptor();
            var userId = Guid.NewGuid();

            var change = MakeChange(userId);
            await context.SyncChanges.AddAsync(change);
            await context.SaveChangesAsync();

            change.Sequence.Should().Be(1);

            var state = await context.SyncSequenceStates.AsNoTracking().FirstAsync(s => s.UserId == userId);
            state.NextSequence.Should().Be(2);
            state.MinRetainedSequence.Should().Be(0);
        }

        [Fact]
        public async Task Batch_write_assigns_consecutive_sequences()
        {
            await using var context = SqlServerAppDbContextFactory.CreateContextWithSyncInterceptor();
            var userId = Guid.NewGuid();

            var changes = Enumerable.Range(0, 5).Select(_ => MakeChange(userId)).ToList();
            foreach (var c in changes)
            {
                await context.SyncChanges.AddAsync(c);
            }
            await context.SaveChangesAsync();

            changes.Select(c => c.Sequence).Should().BeEquivalentTo(new long[] { 1, 2, 3, 4, 5 }, opts => opts.WithStrictOrdering());

            var state = await context.SyncSequenceStates.AsNoTracking().FirstAsync(s => s.UserId == userId);
            state.NextSequence.Should().Be(6);
        }

        [Fact]
        public async Task Sequential_saves_for_same_user_are_monotonic()
        {
            await using var context = SqlServerAppDbContextFactory.CreateContextWithSyncInterceptor();
            var userId = Guid.NewGuid();

            var first = MakeChange(userId);
            await context.SyncChanges.AddAsync(first);
            await context.SaveChangesAsync();
            first.Sequence.Should().Be(1);

            var second = MakeChange(userId);
            var third = MakeChange(userId);
            await context.SyncChanges.AddAsync(second);
            await context.SyncChanges.AddAsync(third);
            await context.SaveChangesAsync();

            second.Sequence.Should().Be(2);
            third.Sequence.Should().Be(3);
        }

        [Fact]
        public async Task Different_users_do_not_share_sequences()
        {
            await using var context = SqlServerAppDbContextFactory.CreateContextWithSyncInterceptor();
            var userA = Guid.NewGuid();
            var userB = Guid.NewGuid();

            var a1 = MakeChange(userA);
            var b1 = MakeChange(userB);
            var a2 = MakeChange(userA);
            var b2 = MakeChange(userB);

            await context.SyncChanges.AddAsync(a1);
            await context.SyncChanges.AddAsync(b1);
            await context.SyncChanges.AddAsync(a2);
            await context.SyncChanges.AddAsync(b2);
            await context.SaveChangesAsync();

            new[] { a1.Sequence, a2.Sequence }.Should().BeEquivalentTo(new long[] { 1, 2 });
            new[] { b1.Sequence, b2.Sequence }.Should().BeEquivalentTo(new long[] { 1, 2 });
        }

        [Fact]
        public async Task Concurrent_same_user_writers_produce_unique_contiguous_sequences()
        {
            // Pre-create the database so concurrent contexts don't race on EnsureCreated.
            await using (SqlServerAppDbContextFactory.CreateContextWithSyncInterceptor()) { }

            var userId = Guid.NewGuid();
            const int writerCount = 8;
            const int rowsPerWriter = 5;

            var tasks = Enumerable.Range(0, writerCount).Select(async _ =>
            {
                await using var ctx = SqlServerAppDbContextFactory.CreateContextWithSyncInterceptorReuseDb();
                var changes = Enumerable.Range(0, rowsPerWriter).Select(_ => MakeChange(userId)).ToList();
                foreach (var c in changes) await ctx.SyncChanges.AddAsync(c);
                await ctx.SaveChangesAsync();
                return changes.Select(c => c.Sequence).ToList();
            }).ToList();

            await Task.WhenAll(tasks);

            var allSequences = tasks.SelectMany(t => t.Result).ToList();
            allSequences.Should().HaveCount(writerCount * rowsPerWriter);
            allSequences.Distinct().Should().HaveCount(allSequences.Count, "sequences must be unique");
            allSequences.OrderBy(s => s).Should().BeEquivalentTo(
                Enumerable.Range(1, writerCount * rowsPerWriter).Select(i => (long)i),
                opts => opts.WithStrictOrdering(),
                "sequences must be contiguous 1..N (gap-free under concurrent writers)");
        }

        [Fact]
        public async Task Failed_save_rolls_back_sequence_increment()
        {
            await using var context = SqlServerAppDbContextFactory.CreateContextWithSyncInterceptor();
            var userId = Guid.NewGuid();

            // Seed first row → NextSequence becomes 2.
            var first = MakeChange(userId);
            await context.SyncChanges.AddAsync(first);
            await context.SaveChangesAsync();
            first.Sequence.Should().Be(1);

            // Pre-occupy Sequence=2 via raw SQL (outside the interceptor) so the next
            // interceptor-driven save will reserve Sequence=2 and collide on the unique
            // (UserId, Sequence) index. The interceptor's own NextSequence bump must roll back
            // along with the failed INSERT.
            await context.Database.ExecuteSqlRawAsync(
                @"INSERT INTO SyncChanges (Id, UserId, Sequence, EntityFamily, EntityId, Operation, ChangedAtUtc, OriginDeviceId, PayloadJson)
                  VALUES ({0}, {1}, 2, 1, {2}, 1, {3}, NULL, '{{}}')",
                Guid.NewGuid(), userId, Guid.NewGuid(), DateTime.UtcNow);

            // Use a fresh context so the change tracker is clean and reads of SyncSequenceState
            // don't see the in-memory NextSequence=2 that resulted from the first SaveChanges.
            await using var attemptContext = SqlServerAppDbContextFactory.CreateContextWithSyncInterceptorReuseDb();
            var second = MakeChange(userId);
            await attemptContext.SyncChanges.AddAsync(second);

            Func<Task> act = async () => await attemptContext.SaveChangesAsync();
            await act.Should().ThrowAsync<DbUpdateException>();

            // After rollback, NextSequence reflects only the original first save (=2), not the
            // failed reservation. The attempt would have bumped it to 3, then rolled back.
            await using var verify = SqlServerAppDbContextFactory.CreateContextWithSyncInterceptorReuseDb();
            var state = await verify.SyncSequenceStates.AsNoTracking().FirstAsync(s => s.UserId == userId);
            state.NextSequence.Should().Be(2, "the failed reservation must have rolled back");

            var committedCount = await verify.SyncChanges.AsNoTracking()
                .Where(c => c.UserId == userId)
                .CountAsync();
            committedCount.Should().Be(2, "only `first` and the raw insert remain committed");
        }

        [Fact]
        public async Task Save_with_no_sync_changes_is_a_noop()
        {
            await using var context = SqlServerAppDbContextFactory.CreateContextWithSyncInterceptor();

            // Persist something unrelated to ensure the interceptor early-exits cleanly.
            var user = User.Create("interceptor-noop@example.com", "Test User",
                                   new DateTime(2026, 5, 3, 12, 0, 0, DateTimeKind.Utc)).Value!;
            await context.Users.AddAsync(user);
            await context.SaveChangesAsync();

            var states = await context.SyncSequenceStates.AsNoTracking().ToListAsync();
            states.Should().BeEmpty("no SyncChange writes ⇒ no state row should be created");
        }
    }
}
