using Microsoft.EntityFrameworkCore;
using NotesApp.Application.Abstractions.Persistence;
using NotesApp.Domain.Entities;
using System;
using System.Collections.Generic;

namespace NotesApp.Infrastructure.Persistence.Repositories
{
    /// <summary>
    /// EF Core repository for the RecurringTaskSubtask entity.
    /// Covers both series template subtasks (SeriesId set) and
    /// exception subtask overrides (ExceptionId set).
    /// </summary>
    public sealed class RecurringTaskSubtaskRepository : IRecurringTaskSubtaskRepository
    {
        private readonly AppDbContext _context;

        public RecurringTaskSubtaskRepository(AppDbContext context)
        {
            _context = context;
        }

        // --- IRepository<RecurringTaskSubtask> ----------------------------------

        public async Task<RecurringTaskSubtask?> GetByIdAsync(
            Guid id, CancellationToken cancellationToken = default)
        {
            return await _context.RecurringTaskSubtasks
                .FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
        }

        public async Task<RecurringTaskSubtask?> GetByIdUntrackedAsync(
            Guid id, CancellationToken cancellationToken = default)
        {
            return await _context.RecurringTaskSubtasks
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
        }

        public async Task AddAsync(RecurringTaskSubtask entity,
                                   CancellationToken cancellationToken = default)
        {
            await _context.RecurringTaskSubtasks.AddAsync(entity, cancellationToken);
        }

        public void Update(RecurringTaskSubtask entity)
        {
            _context.RecurringTaskSubtasks.Update(entity);
        }

        public void Remove(RecurringTaskSubtask entity)
        {
            _context.RecurringTaskSubtasks.Remove(entity);
        }

        // --- IRecurringTaskSubtaskRepository-specific methods -------------------

        /// <inheritdoc />
        public async Task<IReadOnlyList<RecurringTaskSubtask>> GetBySeriesIdAsync(
            Guid seriesId,
            CancellationToken cancellationToken = default)
        {
            return await _context.RecurringTaskSubtasks
                .Where(s => s.SeriesId == seriesId)
                .OrderBy(s => s.Position)
                .ToListAsync(cancellationToken);
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<RecurringTaskSubtask>> GetByExceptionIdAsync(
            Guid exceptionId,
            CancellationToken cancellationToken = default)
        {
            return await _context.RecurringTaskSubtasks
                .Where(s => s.ExceptionId == exceptionId)
                .OrderBy(s => s.Position)
                .ToListAsync(cancellationToken);
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<RecurringTaskSubtask>> GetByExceptionIdsAsync(
            IReadOnlyList<Guid> exceptionIds,
            CancellationToken cancellationToken = default)
        {
            if (exceptionIds.Count == 0)
            {
                return Array.Empty<RecurringTaskSubtask>();
            }

            return await _context.RecurringTaskSubtasks
                .Where(s => s.ExceptionId != null && exceptionIds.Contains(s.ExceptionId.Value))
                .OrderBy(s => s.Position)
                .ToListAsync(cancellationToken);
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<RecurringTaskSubtask>> GetChangedSinceAsync(
            Guid userId,
            DateTime? since,
            CancellationToken cancellationToken = default)
        {
            if (since is null)
            {
                return await _context.RecurringTaskSubtasks
                    .Where(s => s.UserId == userId)
                    .ToListAsync(cancellationToken);
            }

            return await _context.RecurringTaskSubtasks
                .IgnoreQueryFilters()
                .Where(s => s.UserId == userId && s.UpdatedAtUtc > since.Value)
                .ToListAsync(cancellationToken);
        }
    }
}
