using Microsoft.EntityFrameworkCore;
using NotesApp.Application.Abstractions.Persistence;
using NotesApp.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Infrastructure.Persistence.Repositories
{
    /// <summary>
    /// EF Core repository for the Note aggregate.
    /// 
    /// Responsibilities:
    /// - Provide basic CRUD operations via IRepository&lt;Note&gt;.
    /// - Implement Note-specific queries such as "notes for a given day".
    /// 
    /// This class is intentionally thin: it delegates identity / multi-tenant
    /// concerns to higher layers (ICurrentUserService + handlers) and focuses
    /// only on persistence.
    /// </summary>
    public sealed class NoteRepository : INoteRepository
    {
        private readonly AppDbContext _dbContext;

        public NoteRepository(AppDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        // --- IRepository<Note> implementation --------------------------------

        public async Task<Note?> GetByIdAsync(Guid id,
                                              CancellationToken cancellationToken = default)
        {
            return await _dbContext.Notes
               .FirstOrDefaultAsync(n => n.Id == id && !n.IsDeleted, cancellationToken);
        }

        public async Task<Note?> GetByIdUntrackedAsync(Guid id,
                                                       CancellationToken cancellationToken = default)
        {
            return await _dbContext.Notes
                .AsNoTracking()
                .FirstOrDefaultAsync(n => n.Id == id && !n.IsDeleted, cancellationToken);
        }

        public async Task AddAsync(Note entity,
                                   CancellationToken cancellationToken = default)
        {
            // We do not call SaveChanges here: UnitOfWork is responsible for that.
            await _dbContext.Notes.AddAsync(entity, cancellationToken);
        }

        public void Update(Note entity)
        {
            _dbContext.Notes.Update(entity);
        }

        public void Remove(Note entity)
        {
            _dbContext.Notes.Remove(entity);
        }


        // --- INoteRepository-specific methods ---------------------------------

        public async Task<IReadOnlyList<Note>> GetForDayAsync(Guid userId,
                                                              DateOnly date,
                                                              CancellationToken cancellationToken = default)
        {
            return await _dbContext.Notes
                .Where(n => n.UserId == userId
                            && n.Date == date
                            && !n.IsDeleted)
                .ToListAsync(cancellationToken);
        }

        public async Task<IReadOnlyList<Note>> GetForDateRangeAsync(Guid userId,
                                                                    DateOnly fromInclusive,
                                                                    DateOnly toExclusive,
                                                                    CancellationToken cancellationToken = default)
        {
            return await _dbContext.Notes
                .Where(n => n.UserId == userId
                            && n.Date >= fromInclusive
                            && n.Date < toExclusive
                            && !n.IsDeleted)
                .ToListAsync(cancellationToken);
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<Note>> GetChangedSinceAsync(Guid userId,
                                                                    DateTime? since,
                                                                    CancellationToken cancellationToken = default)
        {
            // Initial sync: all non-deleted notes for the user.
            if (since is null)
            {
                return await _dbContext.Notes
                    .Where(n => n.UserId == userId && !n.IsDeleted)
                    .ToListAsync(cancellationToken);
            }

            // Incremental sync: include soft-deleted notes as well, so the caller
            // can categorise them as "deleted" if IsDeleted == true.
            return await _dbContext.Notes
                .IgnoreQueryFilters()
                .Where(n => n.UserId == userId && n.UpdatedAtUtc > since.Value)
                .ToListAsync(cancellationToken);
        }
    }
}
