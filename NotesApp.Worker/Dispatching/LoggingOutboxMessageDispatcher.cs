using FluentResults;
using NotesApp.Application.Common.Interfaces;
using NotesApp.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;

namespace NotesApp.Worker.Dispatching
{
    /// <summary>
    /// Simple dispatcher that logs Outbox messages and, for certain
    /// Note/Task events, triggers sync-needed push notifications.
    /// 
    /// This is intentionally conservative: it only triggers pushes for
    /// Created/Updated/Deleted/CompletionChanged events.
    /// </summary>
    public sealed class LoggingOutboxMessageDispatcher : IOutboxMessageDispatcher
    {
        private readonly ILogger<LoggingOutboxMessageDispatcher> _logger;
        private readonly IPushNotificationService _pushNotificationService;

        // Outbox message types that should cause a sync-needed push.
        private static readonly HashSet<string> SyncRelatedMessageTypes =
            new(StringComparer.OrdinalIgnoreCase)
            {
                "Note.Created",
                "Note.Updated",
                "Note.Deleted",
                "TaskItem.Created",
                "TaskItem.Updated",
                "TaskItem.Deleted",
                "TaskItem.CompletionChanged"
            };

        /// <summary>
        /// Minimal projection of our Outbox payloads; we only care about OriginDeviceId.
        /// OutboxPayloadBuilder currently includes this property for Task/Note payloads.
        /// </summary>
        private sealed class SyncPayloadEnvelope
        {
            public Guid? OriginDeviceId { get; init; }
        }

        public LoggingOutboxMessageDispatcher(ILogger<LoggingOutboxMessageDispatcher> logger,
                                              IPushNotificationService pushNotificationService)
        {
            _logger = logger;
            _pushNotificationService = pushNotificationService;
        }

        public async Task<Result> DispatchAsync(OutboxMessage message,
                                                CancellationToken cancellationToken)
        {
            _logger.LogInformation(
                "Dispatching Outbox message {MessageId}. Type={MessageType}, AggregateType={AggregateType}, UserId={UserId}",
                message.Id,
                message.MessageType,
                message.AggregateType,
                message.UserId);

            // Phase 7: sync-needed pushes for note & task changes
            if (SyncRelatedMessageTypes.Contains(message.MessageType))
            {
                Guid? originDeviceId = null;

                if (!string.IsNullOrWhiteSpace(message.Payload))
                {
                    try
                    {
                        var envelope = JsonSerializer.Deserialize<SyncPayloadEnvelope>(message.Payload);
                        originDeviceId = envelope?.OriginDeviceId;
                    }
                    catch (JsonException ex)
                    {
                        // We do NOT fail the dispatch if payload cannot be parsed;
                        // we just fall back to "no origin device" (notify all devices).
                        _logger.LogWarning(
                            ex,
                            "Failed to deserialize Outbox payload for message {MessageId} when extracting OriginDeviceId. " +
                            "Proceeding without origin device filter.",
                            message.Id);
                    }
                }

                var pushResult = await _pushNotificationService
                    .SendSyncNeededAsync(message.UserId, originDeviceId, cancellationToken);

                if (pushResult.IsFailed)
                {
                    // We log, but do not propagate failure up to the worker for now,
                    // to avoid blocking Outbox processing on notification issues.
                    var firstError = pushResult.Errors.FirstOrDefault()?.Message ?? "Unknown error";

                    _logger.LogWarning(
                        "SyncNeeded push for Outbox message {MessageId} reported failure: {Error}",
                        message.Id,
                        firstError);
                }
            }

            // Logging dispatcher itself always reports success so the worker
            // can mark the message as processed.
            return Result.Ok();
        }
    }
}
