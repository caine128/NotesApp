using NotesApp.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Application.Abstractions.Persistence
{
    /// <summary>
    /// Repository abstraction for Note aggregate.
    /// 
    /// Extends the generic IRepository with Note-specific queries that
    /// the Application layer needs (e.g. "notes for a specific day").
    /// 
    /// Implementation lives in Infrastructure and uses EF Core / AppDbContext.
    /// </summary>
    public interface INoteRepository : IRepository<Note>
    {
        /// <summary>
        /// Returns all notes for a given user and calendar date.
        /// Soft-deleted notes are automatically filtered out by EF global filters.
        /// </summary>
        Task<IReadOnlyList<Note>> GetForDayAsync(Guid userId,
                                                 DateOnly date,
                                                 CancellationToken cancellationToken = default);
    }
}
