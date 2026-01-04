using Microsoft.EntityFrameworkCore;
using NotesApp.Application.Abstractions.Persistence;
using NotesApp.Domain.Common;
using NotesApp.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Infrastructure.Persistence.Repositories
{
    /// <summary>
    /// EF Core repository for the Block aggregate.
    /// 
    /// Responsibilities:
    /// - Provide basic CRUD operations via IRepository&lt;Block&gt;.
    /// - Implement Block-specific queries such as "blocks for a parent".
    /// 
    /// This class is intentionally thin: it delegates identity / multi-tenant
    /// concerns to higher layers (ICurrentUserService + handlers) and focuses
    /// only on persistence.
    /// </summary>
    public sealed class BlockRepository : IBlockRepository
    {
        private readonly AppDbContext _dbContext;

        public BlockRepository(AppDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        // --- IRepository<Block> implementation --------------------------------

        public async Task<Block?> GetByIdAsync(Guid id,
                                               CancellationToken cancellationToken = default)
        {
            return await _dbContext.Blocks
                .FirstOrDefaultAsync(b => b.Id == id && !b.IsDeleted, cancellationToken);
        }

        public async Task AddAsync(Block entity,
                                   CancellationToken cancellationToken = default)
        {
            await _dbContext.Blocks.AddAsync(entity, cancellationToken);
        }

        public void Update(Block entity)
        {
            _dbContext.Blocks.Update(entity);
        }

        public void Remove(Block entity)
        {
            _dbContext.Blocks.Remove(entity);
        }


        // --- IBlockRepository-specific methods ---------------------------------

        /// <inheritdoc />
        public async Task<IReadOnlyList<Block>> GetForParentAsync(Guid parentId,
                                                                  BlockParentType parentType,
                                                                  CancellationToken cancellationToken = default)
        {
            return await _dbContext.Blocks
                .Where(b => b.ParentId == parentId
                            && b.ParentType == parentType
                            && !b.IsDeleted)
                .OrderBy(b => b.Position)
                .ToListAsync(cancellationToken);
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<Block>> GetChangedSinceAsync(Guid userId,
                                                                     DateTime? since,
                                                                     CancellationToken cancellationToken = default)
        {
            // Initial sync: all non-deleted blocks for the user.
            if (since is null)
            {
                return await _dbContext.Blocks
                    .Where(b => b.UserId == userId && !b.IsDeleted)
                    .ToListAsync(cancellationToken);
            }

            // Incremental sync: include soft-deleted blocks as well, so the caller
            // can categorise them as "deleted" if IsDeleted == true.
            return await _dbContext.Blocks
                .IgnoreQueryFilters()
                .Where(b => b.UserId == userId && b.UpdatedAtUtc > since.Value)
                .ToListAsync(cancellationToken);
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<Block>> GetByIdsAsync(Guid userId,
                                                              IEnumerable<Guid> ids,
                                                              CancellationToken cancellationToken = default)
        {
            var idList = ids.ToList();
            if (idList.Count == 0)
                return Array.Empty<Block>();

            return await _dbContext.Blocks
                .Where(b => b.UserId == userId && idList.Contains(b.Id) && !b.IsDeleted)
                .ToListAsync(cancellationToken);
        }

        /// <inheritdoc />
        public async Task<Block?> GetLastBlockForParentAsync(Guid parentId,
                                                             BlockParentType parentType,
                                                             CancellationToken cancellationToken = default)
        {
            return await _dbContext.Blocks
                .Where(b => b.ParentId == parentId
                            && b.ParentType == parentType
                            && !b.IsDeleted)
                .OrderByDescending(b => b.Position)
                .FirstOrDefaultAsync(cancellationToken);
        }
    }
}
