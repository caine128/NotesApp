using Microsoft.EntityFrameworkCore;
using NotesApp.Application.Abstractions.Persistence;
using NotesApp.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Infrastructure.Persistence.Repositories
{
    /// <summary>
    /// EF Core repository for the Asset aggregate.
    /// 
    /// Responsibilities:
    /// - Provide basic CRUD operations via IRepository&lt;Asset&gt;.
    /// - Implement Asset-specific queries such as "asset by block ID".
    /// 
    /// Note: Asset is immutable after creation (no Version tracking).
    /// </summary>
    public sealed class AssetRepository : IAssetRepository
    {
        private readonly AppDbContext _dbContext;

        public AssetRepository(AppDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        // --- IRepository<Asset> implementation --------------------------------

        public async Task<Asset?> GetByIdAsync(Guid id,
                                               CancellationToken cancellationToken = default)
        {
            return await _dbContext.Assets
                .FirstOrDefaultAsync(a => a.Id == id && !a.IsDeleted, cancellationToken);
        }

        public async Task AddAsync(Asset entity,
                                   CancellationToken cancellationToken = default)
        {
            await _dbContext.Assets.AddAsync(entity, cancellationToken);
        }

        [Obsolete("Asset is immutable and cannot be updated.", error: true)]
        public void Update(Asset entity)
        {
            // Assets are immutable - this should not be called in normal operations
            // but we implement it for interface compliance
            throw new NotSupportedException("Asset is immutable and cannot be updated.");
        }

        public void Remove(Asset entity)
        {
            _dbContext.Assets.Remove(entity);
        }



        // --- IAssetRepository-specific methods ---------------------------------

        /// <inheritdoc />
        public async Task<Asset?> GetByBlockIdAsync(Guid blockId,
                                                    CancellationToken cancellationToken = default)
        {
            return await _dbContext.Assets
                .FirstOrDefaultAsync(a => a.BlockId == blockId && !a.IsDeleted, cancellationToken);
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<Asset>> GetChangedSinceAsync(Guid userId,
                                                                     DateTime? since,
                                                                     CancellationToken cancellationToken = default)
        {
            // Initial sync: all non-deleted assets for the user.
            if (since is null)
            {
                return await _dbContext.Assets
                    .Where(a => a.UserId == userId && !a.IsDeleted)
                    .ToListAsync(cancellationToken);
            }

            // Incremental sync: include soft-deleted assets as well.
            // Assets are immutable, so "updated" means created or deleted.
            return await _dbContext.Assets
                .IgnoreQueryFilters()
                .Where(a => a.UserId == userId && a.UpdatedAtUtc > since.Value)
                .ToListAsync(cancellationToken);
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<Asset>> GetByBlockIdsAsync(IEnumerable<Guid> blockIds,
                                                                   CancellationToken cancellationToken = default)
        {
            var idList = blockIds.ToList();
            if (idList.Count == 0)
                return Array.Empty<Asset>();

            return await _dbContext.Assets
                .Where(a => idList.Contains(a.BlockId) && !a.IsDeleted)
                .ToListAsync(cancellationToken);
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<Asset>> GetOrphanAssetsAsync(int limit,
                                                                     CancellationToken cancellationToken = default)
        {
            // Find assets where the associated block is deleted.
            // This requires ignoring the query filter on blocks to see deleted ones.
            return await _dbContext.Assets
                .Where(a => !a.IsDeleted)
                .Where(a => _dbContext.Blocks
                    .IgnoreQueryFilters()
                    .Any(b => b.Id == a.BlockId && b.IsDeleted))
                .Take(limit)
                .ToListAsync(cancellationToken);
        }
    }
}
