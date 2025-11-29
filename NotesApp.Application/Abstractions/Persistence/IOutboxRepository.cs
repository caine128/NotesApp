using NotesApp.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Application.Abstractions.Persistence
{
    public interface IOutboxRepository : IRepository<OutboxMessage>
    {
        /// <summary>
        /// Returns a batch of pending messages ordered by CreatedAtUtc.
        /// Will be used by the Worker.
        /// </summary>
        Task<IReadOnlyList<OutboxMessage>> GetPendingBatchAsync(
            int maxCount,
            CancellationToken cancellationToken = default);
    }
}
