using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Worker.Outbox
{
    /// <summary>
    /// Accessor for the ambient OutboxProcessingContext, backed by AsyncLocal.
    /// </summary>
    public interface IOutboxProcessingContextAccessor
    {
        /// <summary>
        /// The current Outbox processing context, or null if no message
        /// is being processed on this async flow.
        /// </summary>
        OutboxProcessingContext? Current { get; }

        /// <summary>
        /// Sets the current context. Intended to be called by the Outbox worker.
        /// </summary>
        void Set(OutboxProcessingContext? context);
    }
}
