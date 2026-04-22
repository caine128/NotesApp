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
            // Change-tracker pattern: load non-deleted subtasks, call domain SoftDelete() on each.
            // The caller's SaveChangesAsync() commits these soft-deletes atomically with any other
            // staged changes (e.g. new Subtask rows, TaskItem soft-delete, outbox message).
            // Does NOT use ExecuteUpdateAsync — that would commit immediately outside the ambient transaction.
            var subtasks = await _context.Subtasks
                .IgnoreQueryFilters()
                .Where(s => s.TaskId == taskId && s.UserId == userId && !s.IsDeleted)
                .ToListAsync(cancellationToken);

            foreach (var subtask in subtasks)
            {
                // SoftDelete() can only fail when IsDeleted is already true, which the
                // filter above excludes — discard the result safely.
                _ = subtask.SoftDelete(utcNow);
            }
            // No SaveChangesAsync here — caller commits everything atomically.
        }
    }
}
