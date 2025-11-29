using NotesApp.Domain.Common;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Domain.Entities
{
    /// <summary>
    /// Generic outbox message for asynchronous processing (embeddings, notifications, etc.).
    /// Invariants:
    /// - UserId must be non-empty.
    /// - AggregateId must be non-empty.
    /// - AggregateType, MessageType, and Payload must be non-empty strings.
    /// - AttemptCount is always >= 0.
    /// </summary>
    public sealed class OutboxMessage : Entity<Guid>
    {
        public Guid UserId { get; private set; }

        /// <summary>
        /// The domain aggregate type this message refers to, e.g. "Note" or "Task".
        /// </summary>
        public string AggregateType { get; private set; } = string.Empty;

        /// <summary>
        /// Id of the aggregate instance this message is about, e.g. Note.Id.
        /// </summary>
        public Guid AggregateId { get; private set; }

        /// <summary>
        /// Logical message type, e.g. "Note.Created", "Note.Updated", "Note.EmbeddingRequested".
        /// </summary>
        public string MessageType { get; private set; } = string.Empty;

        /// <summary>
        /// JSON payload with details needed by the worker (e.g. note text, changed fields).
        /// </summary>
        public string Payload { get; private set; } = string.Empty;

        /// <summary>
        /// When this message was successfully processed. Null means "pending".
        /// </summary>
        public DateTime? ProcessedAtUtc { get; private set; }

        /// <summary>
        /// Number of processing attempts made so far.
        /// </summary>
        public int AttemptCount { get; private set; }

        // EF Core constructor
        private OutboxMessage()
        {
        }

        private OutboxMessage(Guid id,
                              Guid userId,
                              string aggregateType,
                              Guid aggregateId,
                              string messageType,
                              string payload,
                              DateTime utcNow)
            : base(id, utcNow)
        {
            UserId = userId;
            AggregateType = aggregateType;
            AggregateId = aggregateId;
            MessageType = messageType;
            Payload = payload;
            AttemptCount = 0;
            ProcessedAtUtc = null;
        }

        /// <summary>
        /// Generic factory for calendar aggregates.
        /// 
        /// - AggregateType is auto-populated from TAggregate.Name (e.g., "Note", "TaskItem").
        /// - MessageType is auto-built as "AggregateType.EventName" (e.g., "Note.Created").
        /// - UserId is taken from the aggregate's UserId.
        /// - AggregateId is taken from the aggregate's Id.
        /// </summary>
        public static DomainResult<OutboxMessage> Create<TAggregate, TEvent>(TAggregate aggregate,
                                                                             TEvent eventType,
                                                                             string payload,
                                                                             DateTime utcNow)
            where TAggregate : Entity<Guid>, ICalendarEntity
            where TEvent : struct, Enum
        {
            var errors = new List<DomainError>();

            if (aggregate is null)
            {
                errors.Add(new DomainError(
                    "OutboxMessage.Aggregate.Null",
                    "Aggregate must not be null."));
            }
           
            var normalizedPayload = payload?.Trim() ?? string.Empty;
            if (normalizedPayload.Length == 0)
            {
                errors.Add(new DomainError(
                    "OutboxMessage.Payload.Empty",
                    "Payload must be a non-empty string (typically JSON)."));
            }

            var eventName = eventType.ToString()?.Trim() ?? string.Empty;
            if (eventName.Length == 0)
            {
                errors.Add(new DomainError(
                    "OutboxMessage.EventName.Empty",
                    "Event name must be a non-empty enum value."));
            }

            if (aggregate != null)
            {
                if (aggregate.Id == Guid.Empty)
                {
                    errors.Add(new DomainError(
                        "OutboxMessage.AggregateId.Empty",
                        "AggregateId must be a non-empty GUID."));
                }

                if (aggregate.UserId == Guid.Empty)
                {
                    errors.Add(new DomainError(
                        "OutboxMessage.UserId.Empty",
                        "UserId must be a non-empty GUID."));
                }
            }
 
            if (errors.Count > 0)
            {
                return DomainResult<OutboxMessage>.Failure(errors);
            }

            // At this point aggregate is not null due to earlier checks.
            var aggregateType = typeof(TAggregate).Name;                // e.g. "Note", "TaskItem"
            var aggregateId = aggregate!.Id;                            // from Entity<Guid>
            var userId = aggregate.UserId;                              // from ICalendarEntity
            var messageType = $"{aggregateType}.{eventName}";           // e.g. "Note.Created"

            var id = Guid.NewGuid();

            var message = new OutboxMessage(id,
                                            userId,
                                            aggregateType,
                                            aggregateId,
                                            messageType,
                                            normalizedPayload,
                                            utcNow);

            return DomainResult<OutboxMessage>.Success(message);
        }

        /// <summary>
        /// Mark this message as successfully processed.
        /// </summary>
        public DomainResult MarkProcessed(DateTime utcNow)
        {
            // Idempotent: calling this multiple times is fine.
            ProcessedAtUtc = utcNow;
            Touch(utcNow);
            return DomainResult.Success();
        }

        /// <summary>
        /// Increment the attempt count after a failed processing attempt.
        /// </summary>
        public DomainResult IncrementAttempt(DateTime utcNow)
        {
            AttemptCount++;
            Touch(utcNow);
            return DomainResult.Success();
        }
    }
}
