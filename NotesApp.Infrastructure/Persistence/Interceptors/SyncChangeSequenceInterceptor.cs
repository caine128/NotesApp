using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using NotesApp.Domain.Entities;
using System;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace NotesApp.Infrastructure.Persistence.Interceptors
{
    /// <summary>
    /// Per-user sequence allocator for the SyncChange feed. Runs at SaveChanges flush time:
    /// finds all Added SyncChange entries grouped by UserId, reserves a contiguous block of
    /// sequences for each user via a row-locked MERGE on SyncSequenceStates, and assigns the
    /// reserved values to the entities in tracking order before EF emits its INSERTs.
    ///
    /// The MERGE uses HOLDLOCK which serializes concurrent allocators at the statement level —
    /// no outer transaction is required for uniqueness. If the caller has an active transaction
    /// (e.g. from CreateExecutionStrategy().Execute()) the MERGE joins it; otherwise it runs in
    /// autocommit. Sequence gaps from a rolled-back EF save are acceptable.
    ///
    /// Idempotent under EF execution-strategy retry: on replay, Added SyncChange entries have
    /// stale Sequence values which are reset to 0 before reassigning.
    ///
    /// Does NOT call BeginTransactionAsync — that would conflict with SqlServerRetryingExecutionStrategy
    /// (installed by EnableRetryOnFailure), which forbids user-initiated transactions.
    /// </summary>
    public sealed class SyncChangeSequenceInterceptor : ISaveChangesInterceptor
    {
        public InterceptionResult<int> SavingChanges(DbContextEventData eventData,
                                                     InterceptionResult<int> result)
        {
            // Sync EF callers are not expected in this codebase. Run the async path synchronously
            // as a safety net; if it ever fires we still get correct behavior.
            return SavingChangesAsync(eventData, result, default).AsTask().GetAwaiter().GetResult();
        }

        public async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
                                                                            InterceptionResult<int> result,
                                                                            CancellationToken cancellationToken = default)
        {
            var context = eventData.Context;
            if (context is null)
                return result;

            var addedGroups = context.ChangeTracker
                .Entries<SyncChange>()
                .Where(e => e.State == EntityState.Added)
                .GroupBy(e => e.Entity.UserId)
                .ToList();

            if (addedGroups.Count == 0)
                return result;

            // Reset placeholder Sequence values in case of execution-strategy retry replay.
            foreach (var group in addedGroups)
                foreach (var entry in group)
                    entry.Property(nameof(SyncChange.Sequence)).CurrentValue = 0L;

            foreach (var group in addedGroups)
            {
                var userId = group.Key;
                var ordered = group.ToList();
                var firstSequence = await ReserveSequencesAsync(context, userId, ordered.Count, cancellationToken);

                for (var i = 0; i < ordered.Count; i++)
                    ordered[i].Property(nameof(SyncChange.Sequence)).CurrentValue = firstSequence + i;
            }

            return result;
        }

        public int SavedChanges(SaveChangesCompletedEventData eventData, int result) => result;

        public ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData,
                                                int result,
                                                CancellationToken cancellationToken = default)
            => ValueTask.FromResult(result);

        public void SaveChangesFailed(DbContextErrorEventData eventData) { }

        public Task SaveChangesFailedAsync(DbContextErrorEventData eventData,
                                           CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        /// <summary>
        /// Atomically reserves <paramref name="count"/> consecutive sequence numbers for the user
        /// and returns the first one. Runs on the DbContext's connection, joining the active
        /// transaction if one exists or running in autocommit otherwise.
        /// </summary>
        private static async Task<long> ReserveSequencesAsync(DbContext context,
                                                              Guid userId,
                                                              int count,
                                                              CancellationToken cancellationToken)
        {
            var connection = context.Database.GetDbConnection();
            var transaction = context.Database.CurrentTransaction?.GetDbTransaction();

            if (connection.State != ConnectionState.Open)
                await connection.OpenAsync(cancellationToken);

            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"
MERGE SyncSequenceStates WITH (HOLDLOCK) AS target
USING (SELECT @userId AS UserId) AS src
ON target.UserId = src.UserId
WHEN MATCHED THEN UPDATE SET NextSequence = NextSequence + @count
WHEN NOT MATCHED THEN INSERT (UserId, NextSequence, MinRetainedSequence) VALUES (src.UserId, @count + 1, 0)
OUTPUT ISNULL(deleted.NextSequence, CAST(1 AS bigint)) AS FirstSequence;";

            var pUserId = command.CreateParameter();
            pUserId.ParameterName = "@userId";
            pUserId.Value = userId;
            command.Parameters.Add(pUserId);

            var pCount = command.CreateParameter();
            pCount.ParameterName = "@count";
            pCount.Value = count;
            command.Parameters.Add(pCount);

            var raw = await command.ExecuteScalarAsync(cancellationToken);
            if (raw is null || raw is DBNull)
                throw new InvalidOperationException(
                    $"SyncChangeSequenceInterceptor: MERGE returned no value for user {userId}.");

            return Convert.ToInt64(raw);
        }
    }
}
