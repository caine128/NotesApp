using Microsoft.EntityFrameworkCore;
using NotesApp.Application.Abstractions.Persistence;
using NotesApp.Domain.Entities;
using System;
using System.Collections.Generic;

namespace NotesApp.Infrastructure.Persistence.Repositories
{
    /// <summary>
    /// EF Core repository for the Attachment entity.
    ///
    /// Responsibilities:
    /// - Provide basic CRUD operations via IRepository&lt;Attachment&gt;.
    /// - Implement attachment-specific queries: per-task list, count, sync-change detection,
    ///   bulk soft-delete, and orphan detection for the background cleanup worker.
    ///
    /// Intentionally thin — ownership / multi-tenant checks are performed in command handlers.
    /// </summary>
    // REFACTORED: added AttachmentRepository for task-attachments feature
    public sealed class AttachmentRepository : IAttachmentRepository
    {
        private readonly AppDbContext _context;

        public AttachmentRepository(AppDbContext context)
        {
            _context = context;
        }

        // --- IRepository<Attachment> implementation -------------------------------------

        public async Task<Attachment?> GetByIdAsync(Guid id,
                                                     CancellationToken cancellationToken = default)
        {
            return await _context.Attachments
                .FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
        }

        public async Task<Attachment?> GetByIdUntrackedAsync(Guid id,
                                                              CancellationToken cancellationToken = default)
        {
            return await _context.Attachments
                .AsNoTracking()
                .FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
        }

        public async Task AddAsync(Attachment entity,
                                   CancellationToken cancellationToken = default)
        {
            await _context.Attachments.AddAsync(entity, cancellationToken);
        }

        public void Update(Attachment entity)
        {
            _context.Attachments.Update(entity);
        }

        public void Remove(Attachment entity)
        {
            _context.Attachments.Remove(entity);
        }

        // --- IAttachmentRepository-specific methods -------------------------------------

        /// <inheritdoc />
        public async Task<IReadOnlyList<Attachment>> GetAllForTaskAsync(
            Guid taskId, Guid userId, CancellationToken cancellationToken = default)
        {
            return await _context.Attachments
                .Where(a => a.TaskId == taskId && a.UserId == userId)
                .OrderBy(a => a.DisplayOrder)
                .ToListAsync(cancellationToken);
        }

        /// <inheritdoc />
        public async Task<int> CountForTaskAsync(
            Guid taskId, Guid userId, CancellationToken cancellationToken = default)
        {
            return await _context.Attachments
                .CountAsync(a => a.TaskId == taskId && a.UserId == userId, cancellationToken);
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<Attachment>> GetChangedSinceAsync(
            Guid userId, DateTime? sinceUtc, CancellationToken cancellationToken = default)
        {
            if (sinceUtc is null)
            {
                // Initial sync: return all non-deleted attachments for the user.
                return await _context.Attachments
                    .Where(a => a.UserId == userId)
                    .ToListAsync(cancellationToken);
            }

            // Incremental sync: include soft-deleted attachments so the caller can
            // bucket them into the "deleted" collection via IsDeleted == true.
            return await _context.Attachments
                .IgnoreQueryFilters()
                .Where(a => a.UserId == userId && a.UpdatedAtUtc > sinceUtc.Value)
                .ToListAsync(cancellationToken);
        }

        /// <inheritdoc />
        public async Task SoftDeleteAllForTaskAsync(
            Guid taskId, Guid userId, DateTime utcNow, CancellationToken cancellationToken = default)
        {
            // Change-tracker pattern: load non-deleted attachments, call domain SoftDelete() on each.
            // The caller's SaveChangesAsync() commits these soft-deletes atomically with any other
            // staged changes (e.g. TaskItem soft-delete, outbox message).
            // Does NOT use ExecuteUpdateAsync — that would commit immediately outside the ambient transaction.
            // Attachment.SoftDelete() is idempotent and sets IsDeleted + UpdatedAtUtc (no Version column —
            // attachments are immutable after creation).
            var attachments = await _context.Attachments
                .IgnoreQueryFilters()
                .Where(a => a.TaskId == taskId && a.UserId == userId && !a.IsDeleted)
                .ToListAsync(cancellationToken);

            foreach (var attachment in attachments)
            {
                // SoftDelete() is idempotent — cannot fail for non-deleted entities (filter above).
                _ = attachment.SoftDelete(utcNow);
            }
            // No SaveChangesAsync here — caller commits everything atomically.
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<Attachment>> GetOrphanAttachmentsAsync(
            int limit, CancellationToken cancellationToken = default)
        {
            // Find non-deleted attachments whose parent task has been soft-deleted.
            // These are candidates for blob deletion by the background cleanup worker.
            return await _context.Attachments
                .Where(a => !a.IsDeleted)
                .Where(a => _context.Tasks
                    .IgnoreQueryFilters()
                    .Any(t => t.Id == a.TaskId && t.IsDeleted))
                .Take(limit)
                .ToListAsync(cancellationToken);
        }
    }
}
