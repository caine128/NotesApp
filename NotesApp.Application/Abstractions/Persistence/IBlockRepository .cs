using NotesApp.Domain.Common;
using NotesApp.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Application.Abstractions.Persistence
{
    /// <summary>
    /// Repository abstraction for Block aggregate.
    /// 
    /// Extends the generic IRepository with Block-specific queries that
    /// the Application layer needs (e.g., blocks for a parent, sync queries).
    /// 
    /// Note: Block does NOT extend ICalendarEntityRepository since it's not
    /// a calendar entity (no Date property, not shown in calendar views).
    /// </summary>
    public interface IBlockRepository : IRepository<Block>
    {
        /// <summary>
        /// Returns all blocks for the given parent (Note or Task), ordered by Position.
        /// Soft-deleted blocks are excluded.
        /// </summary>
        Task<IReadOnlyList<Block>> GetForParentAsync(Guid parentId,
                                                     BlockParentType parentType,
                                                     CancellationToken cancellationToken = default);

        /// <summary>
        /// Returns all blocks for the given parent (Note only), ordered by Position.
        /// Soft-deleted blocks are excluded.
        /// 
        /// UNTRACKED: Entities are NOT tracked by EF Core change tracker.
        /// Use this for operations where you want to control persistence atomicity
        /// (e.g., cascade deletion where you want all-or-nothing behavior).
        /// </summary>
        Task<IReadOnlyList<Block>> GetForParentUntrackedAsync(Guid parentId,
                                                              BlockParentType parentType,
                                                              CancellationToken cancellationToken = default);

        /// <summary>
        /// Returns all blocks for a user that have changed since the given timestamp.
        /// 
        /// Semantics:
        /// - When <paramref name="since"/> is null:
        ///   Returns all non-deleted blocks for initial sync.
        /// - When <paramref name="since"/> is not null:
        ///   Returns all blocks (including soft-deleted ones) where:
        ///     UserId == userId AND UpdatedAtUtc > since.
        /// 
        /// The caller is responsible for categorising them into
        /// created / updated / deleted buckets.
        /// </summary>
        Task<IReadOnlyList<Block>> GetChangedSinceAsync(Guid userId,
                                                        DateTime? since,
                                                        CancellationToken cancellationToken = default);

        /// <summary>
        /// Returns blocks by their IDs for the given user.
        /// Useful for batch operations and conflict resolution.
        /// </summary>
        Task<IReadOnlyList<Block>> GetByIdsAsync(Guid userId,
                                                 IEnumerable<Guid> ids,
                                                 CancellationToken cancellationToken = default);

        /// <summary>
        /// Returns the block with the highest position value for a parent.
        /// Used to generate the next position when appending.
        /// Returns null if no blocks exist for the parent.
        /// </summary>
        Task<Block?> GetLastBlockForParentAsync(Guid parentId,
                                                BlockParentType parentType,
                                                CancellationToken cancellationToken = default);
    }
}
