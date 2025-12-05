using NotesApp.Domain;
using NotesApp.Domain.Common;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Application.Abstractions.Persistence
{
    /// <summary>
    /// Generic repository abstraction for calendar entities (tasks, notes, etc.).
    /// 
    /// Provides:
    /// - Basic CRUD via <see cref="IRepository{TEntity}"/>.
    /// - Calendar-oriented queries (day, range).
    /// - Sync-oriented query for "changed since" based on UpdatedAtUtc.
    public interface ICalendarEntityRepository<TEntity> : IRepository<TEntity>
                where TEntity : class, IEntity<Guid>, ICalendarEntity
    {
        /// <summary>
        /// Returns all calendar entities for the given user on a specific date.
        /// Soft-deleted entities are excluded.
        /// </summary>
        Task<IReadOnlyList<TEntity>> GetForDayAsync(Guid userId,
                                                    DateOnly date,
                                                    CancellationToken cancellationToken = default);


        /// <summary>
        /// Returns all calendar entities for the given user within the specified
        /// date range [fromInclusive, toExclusive).
        /// Soft-deleted entities are excluded.
        /// </summary>
        Task<IReadOnlyList<TEntity>> GetForDateRangeAsync(Guid userId,
                                                          DateOnly fromInclusive,
                                                          DateOnly toExclusive,
                                                          CancellationToken cancellationToken = default);

        /// <summary>
        /// Returns all calendar entities for a user that have changed since the
        /// given timestamp, based on UpdatedAtUtc.
        /// 
        /// Semantics:
        /// - When <paramref name="since"/> is null:
        ///   Returns all non-deleted entities for initial sync.
        /// - When <paramref name="since"/> is not null:
        ///   Returns all entities (including soft-deleted ones) where:
        ///     UserId == userId AND UpdatedAtUtc &gt; since.
        /// 
        /// The caller is responsible for categorising them into
        /// created / updated / deleted buckets.
        /// </summary>
        Task<IReadOnlyList<TEntity>> GetChangedSinceAsync(Guid userId,
                                                          DateTime? since,
                                                          CancellationToken cancellationToken = default);
    }
}
