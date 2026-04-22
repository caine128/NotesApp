using Microsoft.EntityFrameworkCore;
using NotesApp.Application.Abstractions.Persistence;
using NotesApp.Domain.Entities;
using System;
using System.Collections.Generic;

namespace NotesApp.Infrastructure.Persistence.Repositories
{
    /// <summary>
    /// EF Core repository for the RecurringTaskRoot entity.
    /// </summary>
    public sealed class RecurringTaskRootRepository : IRecurringTaskRootRepository
    {
        private readonly AppDbContext _context;

        public RecurringTaskRootRepository(AppDbContext context)
        {
            _context = context;
        }

        // --- IRepository<RecurringTaskRoot> -------------------------------------

        public async Task<RecurringTaskRoot?> GetByIdAsync(
            Guid id, CancellationToken cancellationToken = default)
        {
            return await _context.RecurringTaskRoots
                .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        }

        public async Task<RecurringTaskRoot?> GetByIdUntrackedAsync(
            Guid id, CancellationToken cancellationToken = default)
        {
            return await _context.RecurringTaskRoots
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        }

        public async Task AddAsync(RecurringTaskRoot entity,
                                   CancellationToken cancellationToken = default)
        {
            await _context.RecurringTaskRoots.AddAsync(entity, cancellationToken);
        }

        public void Update(RecurringTaskRoot entity)
        {
            _context.RecurringTaskRoots.Update(entity);
        }

        public void Remove(RecurringTaskRoot entity)
        {
            _context.RecurringTaskRoots.Remove(entity);
        }

        // --- IRecurringTaskRootRepository-specific methods ----------------------

        /// <inheritdoc />
        public async Task<IReadOnlyList<RecurringTaskRoot>> GetChangedSinceAsync(
            Guid userId,
            DateTime? since,
            CancellationToken cancellationToken = default)
        {
            if (since is null)
            {
                return await _context.RecurringTaskRoots
                    .Where(r => r.UserId == userId)
                    .ToListAsync(cancellationToken);
            }

            return await _context.RecurringTaskRoots
                .IgnoreQueryFilters()
                .Where(r => r.UserId == userId && r.UpdatedAtUtc > since.Value)
                .ToListAsync(cancellationToken);
        }
    }
}
