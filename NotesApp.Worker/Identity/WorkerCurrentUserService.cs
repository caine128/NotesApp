using NotesApp.Application.Common.Interfaces;
using NotesApp.Worker.Outbox;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Worker.Identity
{
    /// <summary>
    /// Worker-specific implementation of ICurrentUserService.
    /// 
    /// In the worker there is no HTTP context; instead, we derive the "current user"
    /// from the ambient OutboxProcessingContext for the message being processed.
    /// </summary>
    public sealed class WorkerCurrentUserService : ICurrentUserService
    {
        private readonly IOutboxProcessingContextAccessor _contextAccessor;

        public WorkerCurrentUserService(IOutboxProcessingContextAccessor contextAccessor)
        {
            _contextAccessor = contextAccessor;
        }

        public Task<Guid> GetUserIdAsync(CancellationToken cancellationToken = default)
        {
            var context = _contextAccessor.Current;

            if (context is null)
            {
                throw new InvalidOperationException(
                    "No OutboxProcessingContext is available in the worker. " +
                    "ICurrentUserService can only be used while processing an Outbox message. " +
                    "Background jobs must obtain user Ids from Outbox messages or their own inputs.");
            }

            return Task.FromResult(context.UserId);
        }
    }
}
