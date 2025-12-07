using Microsoft.Extensions.Options;
using NotesApp.Application.Abstractions.Persistence;
using NotesApp.Application.Common;
using NotesApp.Application.Common.Interfaces;
using NotesApp.Domain.Entities;
using NotesApp.Worker.Configuration;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Worker
{
    /// <summary>
    /// Background worker that periodically finds overdue task reminders,
    /// sends reminder notifications, and marks them as sent.
    /// </summary>
    public sealed class ReminderMonitorWorker : BackgroundService
    {
        private readonly ILogger<ReminderMonitorWorker> _logger;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ReminderWorkerOptions _options;

        public ReminderMonitorWorker(
            ILogger<ReminderMonitorWorker> logger,
            IServiceScopeFactory scopeFactory,
            IOptions<ReminderWorkerOptions> options)
        {
            _logger = logger;
            _scopeFactory = scopeFactory;
            _options = options.Value;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("ReminderMonitorWorker started.");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ProcessOverdueRemindersOnceAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    // Normal shutdown.
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unexpected error in ReminderMonitorWorker loop.");
                }

                try
                {
                    await Task.Delay(
                        TimeSpan.FromSeconds(_options.PollingIntervalSeconds),
                        stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }

            _logger.LogInformation("ReminderMonitorWorker stopped.");
        }

        /// <summary>
        /// Single iteration: find overdue reminders, send notifications, mark as sent.
        /// Extracted for testability.
        /// </summary>
        private async Task ProcessOverdueRemindersOnceAsync(CancellationToken cancellationToken)
        {
            using var scope = _scopeFactory.CreateScope();

            var taskRepository = scope.ServiceProvider.GetRequiredService<ITaskRepository>();
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var clock = scope.ServiceProvider.GetRequiredService<ISystemClock>();
            var pushService = scope.ServiceProvider.GetRequiredService<IPushNotificationService>();

            var utcNow = clock.UtcNow;

            var overdueTasks = await taskRepository.GetOverdueRemindersAsync(
                utcNow,
                _options.MaxRemindersPerBatch,
                cancellationToken);

            if (overdueTasks.Count == 0)
            {
                _logger.LogDebug("No overdue task reminders found.");
                return;
            }

            _logger.LogInformation(
                "Processing {Count} overdue task reminder(s).",
                overdueTasks.Count);

            foreach (var task in overdueTasks)
            {
                try
                {
                    await ProcessSingleTaskReminderAsync(
                        task,
                        pushService,
                        taskRepository,
                        unitOfWork,
                        utcNow,
                        cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    _logger.LogInformation(
                        "Cancellation requested while processing reminder for task {TaskId}.",
                        task.Id);
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "Unexpected error while processing reminder for task {TaskId}.",
                        task.Id);
                }
            }
        }

        private async Task ProcessSingleTaskReminderAsync(
            TaskItem task,
            IPushNotificationService pushService,
            ITaskRepository taskRepository,
            IUnitOfWork unitOfWork,
            DateTime utcNow,
            CancellationToken cancellationToken)
        {
            // Compose a simple, generic reminder message.
            var title = "Task reminder";
            var body = task.Title ?? "You have a task to complete.";

            // Send push reminder. For now, let the push service decide which devices to target.
            var pushResult = await pushService.SendTaskReminderAsync(
                task.UserId,
                task.Id,
                title,
                body,
                cancellationToken);

            if (pushResult.IsFailed)
            {
                var firstError = pushResult.Errors.FirstOrDefault()?.Message ?? "Unknown error";

                _logger.LogWarning(
                    "Failed to send reminder notification for task {TaskId}: {Error}",
                    task.Id,
                    firstError);

                // Don't mark as sent; the reminder may be retried later.
                return;
            }

            // Mark as sent in the domain.
            var domainResult = task.MarkReminderSent(utcNow);

            if (domainResult.IsFailure)
            {
                var firstError = domainResult.Errors.FirstOrDefault()?.Message ?? "Unknown error";

                _logger.LogWarning(
                    "Failed to mark reminder as sent for task {TaskId}: {Error}",
                    task.Id,
                    firstError);

                return;
            }

            taskRepository.Update(task);
            await unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Task reminder for task {TaskId} marked as sent.",
                task.Id);
        }
    }
}
