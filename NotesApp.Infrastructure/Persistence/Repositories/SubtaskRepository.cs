using Microsoft.EntityFrameworkCore;
using NotesApp.Application.Abstractions.Persistence;
using NotesApp.Domain.Entities;
using System;
using System.Collections.Generic;

namespace NotesApp.Infrastructure.Persistence.Repositories
{
    /// <summary>
    /// EF Core repository for the Subtask entity.
    ///
    /// Responsibilities:
    /// - Provide basic CRUD operations via IRepository&lt;Subtask&gt;.
    /// - Implement subtask-specific queries: per-task list and sync-change detection.
    /// - Bulk soft-delete for cascade when a parent task is deleted.
    ///
    /// Intentionally thin — ownership / multi-tenant checks are performed in command handlers.
    /// </summary>
    public sealed class SubtaskRepository : ISubtaskRepository
    {
        private readonly AppDbContext _context;

        public SubtaskRepository(AppDbContext context)
        {
            _context = context;
        }

        // --- IRepository<Subtask> implementation -------------------------------------

        public async Task<Subtask?> GetByIdAsync(Guid id,
                                                  CancellationToken cancellationToken = default)
        {
            return await _context.Subtasks
                .FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
        }

        public async Task<Subtask?> GetByIdUntrackedAsync(Guid id,
                                                           CancellationToken cancellationToken = default)
        {
            return await _context.Subtasks
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
        }

        public async Task AddAsync(Subtask entity,
                                   CancellationToken cancellationToken = default)
        {
            await _context.Subtasks.AddAsync(entity, cancellationToken);
        }

        public void Update(Subtask entity)
        {
            _context.Subtasks.Update(entity);
        }

        public void Remove(Subtask entity)
        {
            _context.Subtasks.Remove(entity);
        }

        // --- ISubtaskRepository-specific methods -------------------------------------

        /// <inheritdoc />
        public async Task<IReadOnlyList<Subtask>> GetAllForTaskAsync(
            Guid taskId, Guid userId, CancellationToken cancellationToken = default)
        {
            return await _context.Subtasks
                .Where(s => s.TaskId == taskId && s.UserId == userId)
                .OrderBy(s => s.Position)
                .ToListAsync(cancellationToken);
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<Subtask>> GetChangedSinceAsync(
            Guid userId, DateTime? sinceUtc, CancellationToken cancellationToken = default)
        {
            if (sinceUtc is null)
            {
                // Initial sync: return all non-deleted subtasks for the user.
                return await _context.Subtasks
                    .Where(s => s.UserId == userId)
                    .ToListAsync(cancellationToken);
            }

            // Incremental sync: include soft-deleted subtasks so the caller can
            // bucket them into the "deleted" collection via IsDeleted == true.
            return await _context.Subtasks
                .IgnoreQueryFilters()
                .Where(s => s.UserId == userId && s.UpdatedAtUtc > sinceUtc.Value)
                .ToListAsync(cancellationToken);
        }

        /// <inheritdoc />
        public async Task SoftDeleteAllForTaskAsync(
            Guid taskId, Guid userId, DateTime utcNow, CancellationToken cancellationToken = default)
        {
            // Bulk update: marks all non-deleted subtasks for the task as deleted.
            // ExecuteUpdateAsync bypasses the global query filter so we can target by taskId/userId
            // without loading entities into memory, then updates in a single SQL statement.
            await _context.Subtasks
                .IgnoreQueryFilters()
                .Where(s => s.TaskId == taskId && s.UserId == userId && !s.IsDeleted)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(s => s.IsDeleted, true)
                        .SetProperty(s => s.UpdatedAtUtc, utcNow)
                        .SetProperty(s => s.Version, s => s.Version + 1),
                    cancellationToken);
        }
    }
}
