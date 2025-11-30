using FluentResults;
using Microsoft.Extensions.Options;
using NotesApp.Application.Abstractions.Persistence;
using NotesApp.Application.Common;
using NotesApp.Domain.Entities;
using NotesApp.Worker.Configuration;
using NotesApp.Worker.Dispatching;
using NotesApp.Worker.Outbox;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Worker
{
    /// <summary>
    /// Background worker that periodically reads pending Outbox messages
    /// from the database and dispatches them using IOutboxMessageDispatcher.
    ///
    /// This is the "publisher" side of the transactional outbox pattern.
    /// </summary>
    public sealed class OutboxProcessingWorker : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly OutboxWorkerOptions _options;
        private readonly ILogger<OutboxProcessingWorker> _logger;
        private readonly IOutboxProcessingContextAccessor _contextAccessor;

        public OutboxProcessingWorker(IServiceScopeFactory scopeFactory,
                                      IOptions<OutboxWorkerOptions> options,
                                      ILogger<OutboxProcessingWorker> logger,
                                      IOutboxProcessingContextAccessor contextAccessor)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
            _options = options.Value;
            _contextAccessor = contextAccessor;
        }

        /// <summary>
        /// Main worker loop. Runs until the host is shut down.
        /// </summary>
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("OutboxProcessingWorker started.");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ProcessPendingMessagesOnceAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    // Normal shutdown – break the loop gracefully.
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unexpected error in OutboxProcessingWorker main loop.");
                }

                // Wait for the next polling cycle
                try
                {
                    await Task.Delay(_options.PollingIntervalMilliseconds, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }

            _logger.LogInformation("OutboxProcessingWorker stopped.");
        }

        /// <summary>
        /// Processes one batch of pending Outbox messages in a fresh DI scope.
        /// </summary>
        private async Task ProcessPendingMessagesOnceAsync(CancellationToken cancellationToken)
        {
            using var scope = _scopeFactory.CreateScope();

            var outboxRepository = scope.ServiceProvider.GetRequiredService<IOutboxRepository>();
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var dispatcher = scope.ServiceProvider.GetRequiredService<IOutboxMessageDispatcher>();
            var clock = scope.ServiceProvider.GetRequiredService<ISystemClock>();

            var pendingMessages = await outboxRepository
                .GetPendingBatchAsync(_options.MaxBatchSize, cancellationToken);

            if (pendingMessages.Count == 0)
            {
                _logger.LogDebug("No pending Outbox messages found.");
                return;
            }

            _logger.LogInformation("Processing {Count} pending Outbox messages.", pendingMessages.Count);

            foreach (var message in pendingMessages)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                await ProcessSingleMessageAsync(
                    message,
                    outboxRepository,
                    dispatcher,
                    unitOfWork,
                    clock,
                    cancellationToken);
            }
        }

        /// <summary>
        /// Processes a single Outbox message:
        /// - Dispatches it using the dispatcher.
        /// - On success, marks as processed.
        /// - On failure, increments AttemptCount for retry.
        /// </summary>
        private async Task ProcessSingleMessageAsync(
            OutboxMessage message,
            IOutboxRepository outboxRepository,
            IOutboxMessageDispatcher dispatcher,
            IUnitOfWork unitOfWork,
            ISystemClock clock,
            CancellationToken cancellationToken)
        {

            // Capture previous context so we can restore it after this message
            var previousContext = _contextAccessor.Current;

            // Set new context for this message
            var messageContext = OutboxProcessingContext.FromMessage(message);
            _contextAccessor.Set(messageContext);

            try
            {
                _logger.LogDebug(
                    "Dispatching Outbox message {MessageId} ({MessageType}) for aggregate {AggregateType}/{AggregateId}.",
                    message.Id,
                    message.MessageType,
                    message.AggregateType,
                    message.AggregateId);

                Result dispatchResult = await dispatcher.DispatchAsync(message, cancellationToken);

                if (dispatchResult.IsSuccess)
                {
                    message.MarkProcessed(clock.UtcNow);
                    outboxRepository.Update(message);

                    await unitOfWork.SaveChangesAsync(cancellationToken);

                    _logger.LogInformation(
                        "Successfully processed Outbox message {MessageId}.",
                        message.Id);
                }
                else
                {
                    await HandleDispatchFailureAsync(
                        message,
                        outboxRepository,
                        unitOfWork,
                        clock,
                        dispatchResult,
                        cancellationToken);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                _logger.LogInformation(
                    "Cancellation requested while processing Outbox message {MessageId}.",
                    message.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Unexpected error while processing Outbox message {MessageId}.",
                    message.Id);

                await HandleDispatchFailureAsync(
                    message,
                    outboxRepository,
                    unitOfWork,
                    clock,
                    Result.Fail(ex.Message),
                    cancellationToken);
            }
            finally
            {
                // Always restore the previous context (which may be null)
                _contextAccessor.Set(previousContext);
            }
        }

        /// <summary>
        /// Handles a failed dispatch:
        /// - Increments AttemptCount.
        /// - Persists changes.
        /// - Logs warning and, if threshold reached, logs an error for alerting.
        /// </summary>
        private async Task HandleDispatchFailureAsync(
            OutboxMessage message,
            IOutboxRepository outboxRepository,
            IUnitOfWork unitOfWork,
            ISystemClock clock,
            Result failureResult,
            CancellationToken cancellationToken)
        {
            message.IncrementAttempt(clock.UtcNow);
            outboxRepository.Update(message);

            await unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogWarning(
                "Failed to dispatch Outbox message {MessageId}. AttemptCount={AttemptCount}. Errors={Errors}",
                message.Id,
                message.AttemptCount,
                string.Join("; ", failureResult.Errors.Select(e => e.Message)));

            if (message.AttemptCount >= _options.MaxRetryAttempts)
            {
                // Extension point:
                // - Move message to a dead-letter table
                // - Raise an alert / metric
                _logger.LogError(
                    "Outbox message {MessageId} has reached MaxRetryAttempts ({MaxRetryAttempts}).",
                    message.Id,
                    _options.MaxRetryAttempts);
            }
        }
    }
}
