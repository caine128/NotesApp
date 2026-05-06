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
    /// All work happens inside a single transaction shared with the EF SaveChanges SQL, so the
    /// row lock is released only at commit. Idempotent under EF execution-strategy retry: on
    /// replay, entities re-enter Added state with stale Sequence values which we reset to 0
    /// before reassigning.
    /// </summary>
    public sealed class SyncChangeSequenceInterceptor : ISaveChangesInterceptor
    {
        private IDbContextTransaction? _ownedTransaction;

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
            {
                return result;
            }

            var addedGroups = context.ChangeTracker
                .Entries<SyncChange>()
                .Where(e => e.State == EntityState.Added)
                .GroupBy(e => e.Entity.UserId)
                .ToList();

            if (addedGroups.Count == 0)
            {
                return result;
            }

            // Reset placeholder Sequence values in case of execution-strategy retry replay.
            foreach (var group in addedGroups)
            {
                foreach (var entry in group)
                {
                    entry.Property(nameof(SyncChange.Sequence)).CurrentValue = 0L;
                }
            }

            // Ensure a single transaction wraps both our reservation work and EF's subsequent
            // INSERTs. If the caller already started one, use it; otherwise we start (and own)
            // one to be committed in SavedChangesAsync / rolled back in SaveChangesFailedAsync.
            if (context.Database.CurrentTransaction is null)
            {
                _ownedTransaction = await context.Database.BeginTransactionAsync(cancellationToken);
            }

            foreach (var group in addedGroups)
            {
                var userId = group.Key;
                var ordered = group.ToList();
                var firstSequence = await ReserveSequencesAsync(context, userId, ordered.Count, cancellationToken);

                for (var i = 0; i < ordered.Count; i++)
                {
                    ordered[i].Property(nameof(SyncChange.Sequence)).CurrentValue = firstSequence + i;
                }
            }

            return result;
        }

        public int SavedChanges(SaveChangesCompletedEventData eventData, int result)
        {
            return SavedChangesAsync(eventData, result, default).AsTask().GetAwaiter().GetResult();
        }

        public async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData,
                                                      int result,
                                                      CancellationToken cancellationToken = default)
        {
            if (_ownedTransaction is not null)
            {
                await _ownedTransaction.CommitAsync(cancellationToken);
                await _ownedTransaction.DisposeAsync();
                _ownedTransaction = null;
            }
            return result;
        }

        public void SaveChangesFailed(DbContextErrorEventData eventData)
        {
            SaveChangesFailedAsync(eventData, default).GetAwaiter().GetResult();
        }

        public async Task SaveChangesFailedAsync(DbContextErrorEventData eventData,
                                                 CancellationToken cancellationToken = default)
        {
            if (_ownedTransaction is not null)
            {
                try
                {
                    await _ownedTransaction.RollbackAsync(cancellationToken);
                }
                catch
                {
                    // Connection may already be in a faulted state; the transaction is dead either way.
                }
                await _ownedTransaction.DisposeAsync();
                _ownedTransaction = null;
            }
        }

        /// <summary>
        /// Atomically reserves <paramref name="count"/> consecutive sequence numbers for the user
        /// and returns the first one. Inserts a SyncSequenceStates row on first write for the user
        /// or increments NextSequence on subsequent writes — both paths in one round trip.
        ///
        /// Uses raw ADO.NET on the DbContext's connection and transaction so the lock is held
        /// inside the same transaction as EF's subsequent INSERTs.
        /// </summary>
        private static async Task<long> ReserveSequencesAsync(DbContext context,
                                                              Guid userId,
                                                              int count,
                                                              CancellationToken cancellationToken)
        {
            var connection = context.Database.GetDbConnection();
            var transaction = context.Database.CurrentTransaction?.GetDbTransaction();

            if (connection.State != ConnectionState.Open)
            {
                await connection.OpenAsync(cancellationToken);
            }

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
            {
                throw new InvalidOperationException(
                    $"SyncChangeSequenceInterceptor: MERGE returned no value for user {userId}.");
            }

            return Convert.ToInt64(raw);
        }
    }
}
