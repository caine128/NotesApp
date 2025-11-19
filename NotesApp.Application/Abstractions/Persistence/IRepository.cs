using NotesApp.Domain.Common;
using System;
using System.Collections.Generic;
using System.Security.Principal;
using System.Text;

namespace NotesApp.Application.Abstractions.Persistence
{
    public interface IRepository<TEntity>
                        where TEntity : class, IEntity<Guid>
    {
        Task<TEntity?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

        Task AddAsync(TEntity entity, CancellationToken cancellationToken = default);

        void Update(TEntity entity);

        void Remove(TEntity entity);
    }
}
