using Microsoft.EntityFrameworkCore;
using NotesApp.Application.Abstractions.Persistence;
using NotesApp.Domain.Entities;
using System;
using System.Collections.Generic;

namespace NotesApp.Infrastructure.Persistence.Repositories
{
    /// <summary>
    /// EF Core repository for the RecurringTaskAttachment entity.
    /// Covers both series template attachments (SeriesId set) and
    /// exception attachment overrides (ExceptionId set).
    /// </summary>
    // REFACTORED: added for recurring-task-attachments feature
    public sealed class RecurringTaskAttachmentRepository : IRecurringTaskAttachmentRepository
    {
        private readonly AppDbContext _context;

        public RecurringTaskAttachmentRepository(AppDbContext context)
        {
            _context = context;
        }

        // --- IRepository<RecurringTaskAttachment> -----------------------------------

        public async Task<RecurringTaskAttachment?> GetByIdAsync(
            Guid id, CancellationToken cancellationToken = default)
        {
            return await _context.RecurringTaskAttachments
                .FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
        }

        public async Task<RecurringTaskAttachment?> GetByIdUntrackedAsync(
            Guid id, CancellationToken cancellationToken = default)
        {
            return await _context.RecurringTaskAttachments
                .AsNoTracking()
                .FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
        }

        public async Task AddAsync(RecurringTaskAttachment entity,
                                   CancellationToken cancellationToken = default)
        {
            await _context.RecurringTaskAttachments.AddAsync(entity, cancellationToken);
        }

        public void Update(RecurringTaskAttachment entity)
        {
            _context.RecurringTaskAttachments.Update(entity);
        }

        public void Remove(RecurringTaskAttachment entity)
        {
            _context.RecurringTaskAttachments.Remove(entity);
        }

        // --- IRecurringTaskAttachmentRepository-specific methods --------------------

        /// <inheritdoc />
        public async Task<IReadOnlyList<RecurringTaskAttachment>> GetBySeriesIdAsync(
            Guid seriesId,
            CancellationToken cancellationToken = default)
        {
            return await _context.RecurringTaskAttachments
                .Where(a => a.SeriesId == seriesId)
                .OrderBy(a => a.DisplayOrder)
                .ToListAsync(cancellationToken);
        }

        /// <inheritdoc />
        public async Task<int> CountForSeriesAsync(
            Guid seriesId,
            Guid userId,
            CancellationToken cancellationToken = default)
        {
            return await _context.RecurringTaskAttachments
                .CountAsync(a => a.SeriesId == seriesId && a.UserId == userId, cancellationToken);
        }

        /// <inheritdoc />
        public async Task SoftDeleteAllForSeriesAsync(
            Guid seriesId,
            Guid userId,
            DateTime utcNow,
            CancellationToken cancellationToken = default)
        {
            var attachments = await _context.RecurringTaskAttachments
                .IgnoreQueryFilters()
                .Where(a => a.SeriesId == seriesId && a.UserId == userId && !a.IsDeleted)
                .ToListAsync(cancellationToken);

            foreach (var attachment in attachments)
            {
                _ = attachment.SoftDelete(utcNow);
            }
            // No SaveChangesAsync here — caller commits everything atomically.
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<RecurringTaskAttachment>> GetByExceptionIdAsync(
            Guid exceptionId,
            CancellationToken cancellationToken = default)
        {
            return await _context.RecurringTaskAttachments
                .Where(a => a.ExceptionId == exceptionId)
                .OrderBy(a => a.DisplayOrder)
                .ToListAsync(cancellationToken);
        }

        /// <inheritdoc />
        public async Task<int> CountForExceptionAsync(
            Guid exceptionId,
            Guid userId,
            CancellationToken cancellationToken = default)
        {
            return await _context.RecurringTaskAttachments
                .CountAsync(a => a.ExceptionId == exceptionId && a.UserId == userId, cancellationToken);
        }

        /// <inheritdoc />
        public async Task SoftDeleteAllForExceptionAsync(
            Guid exceptionId,
            Guid userId,
            DateTime utcNow,
            CancellationToken cancellationToken = default)
        {
            var attachments = await _context.RecurringTaskAttachments
                .IgnoreQueryFilters()
                .Where(a => a.ExceptionId == exceptionId && a.UserId == userId && !a.IsDeleted)
                .ToListAsync(cancellationToken);

            foreach (var attachment in attachments)
            {
                _ = attachment.SoftDelete(utcNow);
            }
            // No SaveChangesAsync here — caller commits everything atomically.
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<RecurringTaskAttachment>> GetByExceptionIdsAsync(
            IReadOnlyList<Guid> exceptionIds,
            CancellationToken cancellationToken = default)
        {
            if (exceptionIds.Count == 0)
            {
                return Array.Empty<RecurringTaskAttachment>();
            }

            return await _context.RecurringTaskAttachments
                .Where(a => a.ExceptionId != null && exceptionIds.Contains(a.ExceptionId.Value))
                .OrderBy(a => a.DisplayOrder)
                .ToListAsync(cancellationToken);
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<RecurringTaskAttachment>> GetChangedSinceAsync(
            Guid userId,
            DateTime? sinceUtc,
            CancellationToken cancellationToken = default)
        {
            if (sinceUtc is null)
            {
                // Initial sync: return all non-deleted attachments for the user.
                return await _context.RecurringTaskAttachments
                    .Where(a => a.UserId == userId)
                    .ToListAsync(cancellationToken);
            }

            // Incremental sync: include soft-deleted attachments so the caller can
            // bucket them into the "deleted" collection via IsDeleted == true.
            return await _context.RecurringTaskAttachments
                .IgnoreQueryFilters()
                .Where(a => a.UserId == userId && a.UpdatedAtUtc > sinceUtc.Value)
                .ToListAsync(cancellationToken);
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<RecurringTaskAttachment>> GetOrphanRecurringAttachmentsAsync(
            int limit,
            CancellationToken cancellationToken = default)
        {
            // Series template attachments whose parent series is soft-deleted.
            var orphanedByDeletedSeries = _context.RecurringTaskAttachments
                .Where(a => a.SeriesId != null && !a.IsDeleted)
                .Where(a => _context.RecurringTaskSeries
                    .IgnoreQueryFilters()
                    .Any(s => s.Id == a.SeriesId && s.IsDeleted));

            // Exception attachment overrides whose parent exception is soft-deleted.
            var orphanedByDeletedException = _context.RecurringTaskAttachments
                .Where(a => a.ExceptionId != null && !a.IsDeleted)
                .Where(a => _context.RecurringTaskExceptions
                    .IgnoreQueryFilters()
                    .Any(e => e.Id == a.ExceptionId && e.IsDeleted));

            return await orphanedByDeletedSeries
                .Union(orphanedByDeletedException)
                .Take(limit)
                .ToListAsync(cancellationToken);
        }

        /// <inheritdoc />
        public async Task<bool> ExistsNonDeletedWithBlobPathAsync(
            string blobPath,
            CancellationToken cancellationToken = default)
        {
            return await _context.RecurringTaskAttachments
                .AnyAsync(a => a.BlobPath == blobPath, cancellationToken);
        }
    }
}
