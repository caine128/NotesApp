using NotesApp.Domain.Entities;
using System;
using System.Collections.Generic;

namespace NotesApp.Application.Abstractions.Persistence
{
    /// <summary>
    /// Repository abstraction for <see cref="TaskCategory"/> entities.
    ///
    /// Categories are per-user name labels and are not date-scoped, so this
    /// interface extends <see cref="IRepository{TEntity}"/> directly rather than
    /// <see cref="ICalendarEntityRepository{TEntity}"/>.
    /// </summary>
    public interface ICategoryRepository : IRepository<TaskCategory>
    {
        /// <summary>
        /// Returns all non-deleted categories owned by the given user, ordered by name.
        /// </summary>
        Task<IReadOnlyList<TaskCategory>> GetAllForUserAsync(
            Guid userId,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Returns categories that have changed since the given timestamp (for sync pull).
        ///
        /// Semantics:
        /// - When <paramref name="sinceUtc"/> is null:
        ///   Returns all non-deleted categories (initial sync).
        /// - When <paramref name="sinceUtc"/> has a value:
        ///   Returns all categories (including soft-deleted) where
        ///   UserId == userId AND UpdatedAtUtc &gt; sinceUtc.
        ///
        /// The caller is responsible for categorising results into
        /// created / updated / deleted buckets.
        /// </summary>
        Task<IReadOnlyList<TaskCategory>> GetChangedSinceAsync(
            Guid userId,
            DateTime? sinceUtc,
            CancellationToken cancellationToken = default);
    }
}
