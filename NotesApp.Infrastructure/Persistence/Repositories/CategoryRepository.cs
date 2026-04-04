using Microsoft.EntityFrameworkCore;
using NotesApp.Application.Abstractions.Persistence;
using NotesApp.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Infrastructure.Persistence.Repositories
{
    /// <summary>
    /// EF Core repository for the TaskCategory aggregate.
    ///
    /// Responsibilities:
    /// - Provide basic CRUD operations via IRepository&lt;TaskCategory&gt;.
    /// - Implement category-specific queries: per-user list and sync-change detection.
    ///
    /// Intentionally thin — ownership / multi-tenant checks are performed in command handlers.
    /// </summary>
    public sealed class CategoryRepository : ICategoryRepository
    {
        private readonly AppDbContext _context;

        public CategoryRepository(AppDbContext context)
        {
            _context = context;
        }

        // --- IRepository<TaskCategory> implementation --------------------------------

        public async Task<TaskCategory?> GetByIdAsync(Guid id,
                                                      CancellationToken cancellationToken = default)
        {
            return await _context.TaskCategories
                .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        }

        public async Task<TaskCategory?> GetByIdUntrackedAsync(Guid id,
                                                               CancellationToken cancellationToken = default)
        {
            return await _context.TaskCategories
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        }

        public async Task AddAsync(TaskCategory entity,
                                   CancellationToken cancellationToken = default)
        {
            await _context.TaskCategories.AddAsync(entity, cancellationToken);
        }

        public void Update(TaskCategory entity)
        {
            _context.TaskCategories.Update(entity);
        }

        public void Remove(TaskCategory entity)
        {
            _context.TaskCategories.Remove(entity);
        }

        // --- ICategoryRepository-specific methods ------------------------------------

        /// <inheritdoc />
        public async Task<IReadOnlyList<TaskCategory>> GetAllForUserAsync(
            Guid userId, CancellationToken cancellationToken = default)
        {
            return await _context.TaskCategories
                .Where(c => c.UserId == userId)
                .OrderBy(c => c.Name)
                .ToListAsync(cancellationToken);
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<TaskCategory>> GetChangedSinceAsync(
            Guid userId, DateTime? sinceUtc, CancellationToken cancellationToken = default)
        {
            if (sinceUtc is null)
            {
                // Initial sync: return all non-deleted categories for the user.
                return await _context.TaskCategories
                    .Where(c => c.UserId == userId)
                    .ToListAsync(cancellationToken);
            }

            // Incremental sync: include soft-deleted categories so the caller can
            // bucket them into the "deleted" collection via IsDeleted == true.
            return await _context.TaskCategories
                .IgnoreQueryFilters()
                .Where(c => c.UserId == userId && c.UpdatedAtUtc > sinceUtc.Value)
                .ToListAsync(cancellationToken);
        }
    }
}
