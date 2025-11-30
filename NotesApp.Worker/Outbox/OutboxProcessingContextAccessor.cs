using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Worker.Outbox
{
    /// <summary>
    /// AsyncLocal-based implementation of IOutboxProcessingContextAccessor.
    /// This allows us to flow OutboxProcessingContext along an async call chain.
    /// </summary>
    public sealed class OutboxProcessingContextAccessor : IOutboxProcessingContextAccessor
    {
        private readonly AsyncLocal<OutboxProcessingContext?> _current = new();

        public OutboxProcessingContext? Current => _current.Value;

        public void Set(OutboxProcessingContext? context)
        {
            _current.Value = context;
        }
    }
}
