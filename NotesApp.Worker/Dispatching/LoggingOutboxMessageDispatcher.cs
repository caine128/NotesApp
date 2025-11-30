using FluentResults;
using NotesApp.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Worker.Dispatching
{
    /// <summary>
    /// Temporary implementation of IOutboxMessageDispatcher that only logs.
    /// Replace this with real implementations that talk to
    /// mobile sync, AI summarization, message brokers, etc.
    /// </summary>
    public sealed class LoggingOutboxMessageDispatcher : IOutboxMessageDispatcher
    {
        private readonly ILogger<LoggingOutboxMessageDispatcher> _logger;

        public LoggingOutboxMessageDispatcher(ILogger<LoggingOutboxMessageDispatcher> logger)
        {
            _logger = logger;
        }

        public Task<Result> DispatchAsync(OutboxMessage message, CancellationToken cancellationToken)
        {
            _logger.LogInformation(
                "Dispatching Outbox message {MessageId}: {AggregateType}/{AggregateId}, {MessageType}, PayloadLength={PayloadLength}",
                message.Id,
                message.AggregateType,
                message.AggregateId,
                message.MessageType,
                message.Payload?.Length ?? 0);

            // Always succeed for now.
            // Later, this is where you deserialize Payload and call external systems.
            return Task.FromResult(Result.Ok());
        }
    }
}
