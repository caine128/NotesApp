using NotesApp.Domain.Entities;
using System;
using System.Collections.Generic;

namespace NotesApp.Application.Abstractions.Persistence
{
    /// <summary>
    /// Repository for <see cref="RecurringTaskRoot"/> entities.
    ///
    /// RecurringTaskRoot is a thin identity anchor — no domain-specific query methods
    /// beyond the base CRUD and sync pull are needed.
    /// </summary>
    public interface IRecurringTaskRootRepository : IRepository<RecurringTaskRoot>
    {
        /// <summary>
        /// Returns all roots for the given user that have changed since the specified timestamp.
        ///
        /// Semantics:
        /// - When <paramref name="since"/> is null: returns all non-deleted roots (initial sync).
        /// - When <paramref name="since"/> is not null: returns all roots (including soft-deleted)
        ///   where UpdatedAtUtc &gt; since (incremental sync — caller buckets into created/updated/deleted).
        /// </summary>
        Task<IReadOnlyList<RecurringTaskRoot>> GetChangedSinceAsync(
            Guid userId,
            DateTime? since,
            CancellationToken cancellationToken = default);
    }
}
