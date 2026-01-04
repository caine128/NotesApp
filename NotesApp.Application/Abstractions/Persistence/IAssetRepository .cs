using NotesApp.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Application.Abstractions.Persistence
{
    /// <summary>
    /// Repository abstraction for Asset aggregate.
    /// 
    /// Extends the generic IRepository with Asset-specific queries that
    /// the Application layer needs (e.g., lookup by block, sync queries).
    /// 
    /// Note: Asset is immutable after creation (no Version tracking).
    /// </summary>
    public interface IAssetRepository : IRepository<Asset>
    {
        /// <summary>
        /// Returns the asset for the given block (1:1 relationship).
        /// Returns null if no asset exists for the block.
        /// Soft-deleted assets are excluded.
        /// </summary>
        Task<Asset?> GetByBlockIdAsync(Guid blockId,
                                       CancellationToken cancellationToken = default);

        /// <summary>
        /// Returns all assets for a user that have been created since the given timestamp.
        /// 
        /// Semantics:
        /// - When <paramref name="since"/> is null:
        ///   Returns all non-deleted assets for initial sync.
        /// - When <paramref name="since"/> is not null:
        ///   Returns all assets (including soft-deleted ones) where:
        ///     UserId == userId AND UpdatedAtUtc > since.
        /// 
        /// Note: Assets are immutable, so "updated" effectively means created or deleted.
        /// </summary>
        Task<IReadOnlyList<Asset>> GetChangedSinceAsync(Guid userId,
                                                        DateTime? since,
                                                        CancellationToken cancellationToken = default);

        /// <summary>
        /// Returns all assets for the given block IDs.
        /// Used for batch loading when fetching multiple blocks.
        /// </summary>
        Task<IReadOnlyList<Asset>> GetByBlockIdsAsync(IEnumerable<Guid> blockIds,
                                                      CancellationToken cancellationToken = default);


        /// <summary>
        /// Returns all orphan assets (assets where the associated block is deleted).
        /// Used by the cleanup background job.
        /// </summary>
        Task<IReadOnlyList<Asset>> GetOrphanAssetsAsync(int limit,
                                                        CancellationToken cancellationToken = default);
    }
}
