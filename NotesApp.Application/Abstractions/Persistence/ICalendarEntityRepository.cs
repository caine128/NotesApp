using NotesApp.Domain;
using NotesApp.Domain.Common;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Application.Abstractions.Persistence
{
    public interface ICalendarEntityRepository<TEntity> : IRepository<TEntity>
                where TEntity : class, IEntity<Guid>, ICalendarEntity
    {
        Task<IReadOnlyList<TEntity>> GetForDayAsync(
            Guid userId,
            DateOnly date,
            CancellationToken cancellationToken = default);

        Task<IReadOnlyList<TEntity>> GetForDateRangeAsync(
            Guid userId,
            DateOnly fromInclusive,
            DateOnly toExclusive,
            CancellationToken cancellationToken = default);
    }
}
