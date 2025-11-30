using FluentResults;
using NotesApp.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Worker.Dispatching
{
    /// <summary>
    /// Abstraction for dispatching Outbox messages to external systems
    /// (mobile sync, AI summarization, message broker, etc.).
    ///
    /// Implementations must be idempotent, because the same OutboxMessage
    /// can be dispatched more than once (at-least-once delivery).
    /// </summary>
    public interface IOutboxMessageDispatcher
    {
        /// <summary>
        /// Dispatches the specified Outbox message.
        /// </summary>
        /// <param name="message">The Outbox message to dispatch.</param>
        /// <param name="cancellationToken">Cancellation token for cooperative cancellation.</param>
        /// <returns>
        /// A Result indicating success or failure. On failure, the worker will increment
        /// AttemptCount and retry the message later.
        /// </returns>
        Task<Result> DispatchAsync(OutboxMessage message, CancellationToken cancellationToken);
    }
}
