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
    public interface INoteRepository : ICalendarEntityRepository<Note>
    {
      
    }
}
