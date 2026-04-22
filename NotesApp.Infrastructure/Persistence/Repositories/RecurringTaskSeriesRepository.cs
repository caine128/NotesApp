using Microsoft.EntityFrameworkCore;
using NotesApp.Application.Abstractions.Persistence;
using NotesApp.Domain.Entities;
using System;
using System.Collections.Generic;

namespace NotesApp.Infrastructure.Persistence.Repositories
{
    /// <summary>
    /// EF Core repository for the RecurringTaskSeries entity.
    ///
    /// Bulk soft-delete methods follow the change-tracker pattern:
    /// load entities → call domain SoftDelete() → caller's SaveChangesAsync() commits atomically.
    /// ExecuteUpdateAsync() is NOT used for these operations.
    /// </summary>
    public sealed class RecurringTaskSeriesRepository : IRecurringTaskSeriesRepository
    {
        private readonly AppDbContext _context;

        public RecurringTaskSeriesRepository(AppDbContext context)
        {
            _context = context;
        }

        // --- IRepository<RecurringTaskSeries> -----------------------------------

        public async Task<RecurringTaskSeries?> GetByIdAsync(
            Guid id, CancellationToken cancellationToken = default)
        {
            return await _context.RecurringTaskSeries
                .FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
        }

        public async Task<RecurringTaskSeries?> GetByIdUntrackedAsync(
            Guid id, CancellationToken cancellationToken = default)
        {
            return await _context.RecurringTaskSeries
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
        }

        public async Task AddAsync(RecurringTaskSeries entity,
                                   CancellationToken cancellationToken = default)
        {
            await _context.RecurringTaskSeries.AddAsync(entity, cancellationToken);
        }

        public void Update(RecurringTaskSeries entity)
        {
            _context.RecurringTaskSeries.Update(entity);
        }

        public void Remove(RecurringTaskSeries entity)
        {
            _context.RecurringTaskSeries.Remove(entity);
        }

        // --- IRecurringTaskSeriesRepository-specific methods --------------------

        /// <inheritdoc />
        public async Task<IReadOnlyList<RecurringTaskSeries>> GetActiveByRootIdAsync(
            Guid rootId,
            Guid userId,
            CancellationToken cancellationToken = default)
        {
            return await _context.RecurringTaskSeries
                .Where(s => s.RootId == rootId && s.UserId == userId)
                .ToListAsync(cancellationToken);
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<RecurringTaskSeries>> GetSeriesBehindHorizonAsync(
            DateOnly targetDate,
            int batchSize,
            CancellationToken cancellationToken = default)
        {
            return await _context.RecurringTaskSeries
                .Where(s => s.MaterializedUpToDate < targetDate)
                .Take(batchSize)
                .ToListAsync(cancellationToken);
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<RecurringTaskSeries>> GetOverlappingDateRangeAsync(
            Guid userId,
            DateOnly from,
            DateOnly toExclusive,
            CancellationToken cancellationToken = default)
        {
            // A series overlaps [from, toExclusive) when:
            //   StartsOnDate < toExclusive   (series starts before the window ends)
            //   AND (EndsBeforeDate IS NULL   (series has no end)
            //        OR EndsBeforeDate > from) (series ends after the window starts)
            return await _context.RecurringTaskSeries
                .Where(s => s.UserId == userId
                            && s.StartsOnDate < toExclusive
                            && (s.EndsBeforeDate == null || s.EndsBeforeDate > from))
                .ToListAsync(cancellationToken);
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<RecurringTaskSeries>> GetChangedSinceAsync(
            Guid userId,
            DateTime? since,
            CancellationToken cancellationToken = default)
        {
            if (since is null)
            {
                return await _context.RecurringTaskSeries
                    .Where(s => s.UserId == userId)
                    .ToListAsync(cancellationToken);
            }

            return await _context.RecurringTaskSeries
                .IgnoreQueryFilters()
                .Where(s => s.UserId == userId && s.UpdatedAtUtc > since.Value)
                .ToListAsync(cancellationToken);
        }

        /// <inheritdoc />
        public async Task SoftDeleteAllForRootAsync(
            Guid rootId,
            Guid userId,
            DateTime utcNow,
            CancellationToken cancellationToken = default)
        {
            // Change-tracker pattern: load entities, call domain SoftDelete() on each.
            // The caller's SaveChangesAsync() commits all changes atomically.
            var series = await _context.RecurringTaskSeries
                .Where(s => s.RootId == rootId && s.UserId == userId)
                .ToListAsync(cancellationToken);

            foreach (var s in series)
            {
                s.SoftDelete(utcNow);
            }
        }
    }
}
