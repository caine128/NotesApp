using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Worker.Outbox
{
    /// <summary>
    /// Per-message context for Outbox processing.
    /// This is the "ambient" data that worker-side code may need
    /// while dispatching a single OutboxMessage.
    /// </summary>
    public sealed class OutboxProcessingContext
    {
        /// <summary>
        /// Id of the OutboxMessage being processed.
        /// Useful for logging and diagnostics.
        /// </summary>
        public Guid MessageId { get; }

        /// <summary>
        /// Id of the user associated with this Outbox message.
        /// </summary>
        public Guid UserId { get; }

        // Extension point: TenantId, Plan, etc. can be added later.

        public OutboxProcessingContext(Guid messageId, Guid userId)
        {
            MessageId = messageId;
            UserId = userId;
        }

        /// <summary>
        /// Creates a context from the Outbox message.
        /// Later we can enrich this (e.g. plan, tenant) without touching callers.
        /// </summary>
        public static OutboxProcessingContext FromMessage(NotesApp.Domain.Entities.OutboxMessage message)
        {
            return new OutboxProcessingContext(
                message.Id,
                message.UserId);
        }
    }
}
