using Microsoft.EntityFrameworkCore;
using NotesApp.Application.Abstractions.Persistence;
using NotesApp.Domain.Entities;
using System;
using System.Collections.Generic;

namespace NotesApp.Infrastructure.Persistence.Repositories
{
    /// <summary>
    /// EF Core repository for the RecurringTaskException entity.
    ///
    /// Bulk soft-delete methods follow the change-tracker pattern:
    /// load entities → call domain SoftDelete() → caller's SaveChangesAsync() commits atomically.
    /// ExecuteUpdateAsync() is NOT used for these operations.
    /// </summary>
    public sealed class RecurringTaskExceptionRepository : IRecurringTaskExceptionRepository
    {
        private readonly AppDbContext _context;

        public RecurringTaskExceptionRepository(AppDbContext context)
        {
            _context = context;
        }

        // --- IRepository<RecurringTaskException> --------------------------------

        public async Task<RecurringTaskException?> GetByIdAsync(
            Guid id, CancellationToken cancellationToken = default)
        {
            return await _context.RecurringTaskExceptions
                .FirstOrDefaultAsync(e => e.Id == id, cancellationToken);
        }

        public async Task<RecurringTaskException?> GetByIdUntrackedAsync(
            Guid id, CancellationToken cancellationToken = default)
        {
            return await _context.RecurringTaskExceptions
                .AsNoTracking()
                .FirstOrDefaultAsync(e => e.Id == id, cancellationToken);
        }

        public async Task AddAsync(RecurringTaskException entity,
                                   CancellationToken cancellationToken = default)
        {
            await _context.RecurringTaskExceptions.AddAsync(entity, cancellationToken);
        }

        public void Update(RecurringTaskException entity)
        {
            _context.RecurringTaskExceptions.Update(entity);
        }

        public void Remove(RecurringTaskException entity)
        {
            _context.RecurringTaskExceptions.Remove(entity);
        }

        // --- IRecurringTaskExceptionRepository-specific methods -----------------

        /// <inheritdoc />
        public async Task<RecurringTaskException?> GetByOccurrenceAsync(
            Guid seriesId,
            DateOnly occurrenceDate,
            CancellationToken cancellationToken = default)
        {
            return await _context.RecurringTaskExceptions
                .FirstOrDefaultAsync(
                    e => e.SeriesId == seriesId && e.OccurrenceDate == occurrenceDate,
                    cancellationToken);
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<RecurringTaskException>> GetForSeriesInRangeAsync(
            Guid seriesId,
            DateOnly from,
            DateOnly toExclusive,
            CancellationToken cancellationToken = default)
        {
            return await _context.RecurringTaskExceptions
                .Where(e => e.SeriesId == seriesId
                            && e.OccurrenceDate >= from
                            && e.OccurrenceDate < toExclusive)
                .ToListAsync(cancellationToken);
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<RecurringTaskException>> GetChangedSinceAsync(
            Guid userId,
            DateTime? since,
            CancellationToken cancellationToken = default)
        {
            if (since is null)
            {
                return await _context.RecurringTaskExceptions
                    .Where(e => e.UserId == userId)
                    .ToListAsync(cancellationToken);
            }

            return await _context.RecurringTaskExceptions
                .IgnoreQueryFilters()
                .Where(e => e.UserId == userId && e.UpdatedAtUtc > since.Value)
                .ToListAsync(cancellationToken);
        }

        /// <inheritdoc />
        public async Task SoftDeleteFromDateAsync(
            Guid seriesId,
            DateOnly fromInclusive,
            Guid userId,
            DateTime utcNow,
            CancellationToken cancellationToken = default)
        {
            // Change-tracker pattern: load entities, call domain SoftDelete() on each.
            // The caller's SaveChangesAsync() commits all changes atomically.
            var exceptions = await _context.RecurringTaskExceptions
                .Where(e => e.SeriesId == seriesId
                            && e.OccurrenceDate >= fromInclusive
                            && e.UserId == userId)
                .ToListAsync(cancellationToken);

            foreach (var e in exceptions)
            {
                e.SoftDelete(utcNow);
            }
        }

        /// <inheritdoc />
        public async Task SoftDeleteAllForRootAsync(
            Guid rootId,
            Guid userId,
            DateTime utcNow,
            CancellationToken cancellationToken = default)
        {
            // Resolve series IDs for the root, then load all exceptions for those series.
            var seriesIds = await _context.RecurringTaskSeries
                .IgnoreQueryFilters()
                .Where(s => s.RootId == rootId && s.UserId == userId)
                .Select(s => s.Id)
                .ToListAsync(cancellationToken);

            if (seriesIds.Count == 0)
            {
                return;
            }

            var exceptions = await _context.RecurringTaskExceptions
                .Where(e => seriesIds.Contains(e.SeriesId) && e.UserId == userId)
                .ToListAsync(cancellationToken);

            foreach (var e in exceptions)
            {
                e.SoftDelete(utcNow);
            }
        }
    }
}
