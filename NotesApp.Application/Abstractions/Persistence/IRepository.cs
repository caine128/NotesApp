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
        /// <summary>
        /// Retrieves an entity by ID with change tracking enabled.
        /// Modifications to the returned entity will be persisted on SaveChanges.
        /// </summary>
        Task<TEntity?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

        /// <summary>
        /// Retrieves an entity by ID without change tracking.
        /// Modifications to the returned entity will NOT be automatically persisted.
        /// Use <see cref="Update"/> to explicitly attach and mark for persistence.
        /// Useful for scenarios where persistence should be conditional (e.g., outbox pattern).
        /// </summary>
        Task<TEntity?> GetByIdUntrackedAsync(Guid id, CancellationToken cancellationToken = default);

        Task AddAsync(TEntity entity, CancellationToken cancellationToken = default);

        /// <summary>
        /// Attaches an entity to the context and marks it as modified.
        /// Required for persisting changes to entities loaded with <see cref="GetByIdUntrackedAsync"/>.
        /// </summary>
        void Update(TEntity entity);

        void Remove(TEntity entity);
    }
}
