using Microsoft.Extensions.Options;
using NotesApp.Application.Abstractions.Persistence;
using NotesApp.Application.Common;
using NotesApp.Application.Configuration;
using NotesApp.Application.Tasks.Services;
using NotesApp.Domain.Entities;
using NotesApp.Worker.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NotesApp.Worker
{
    /// <summary>
    /// Background worker that advances the materialization horizon for recurring task series.
    ///
    /// Follows the same structure as <see cref="ReminderMonitorWorker"/>.
    ///
    /// Each loop iteration:
    /// 1. Computes targetDate = today + HorizonWeeksAhead.
    /// 2. Fetches up to MaxSeriesPerBatch series whose MaterializedUpToDate &lt; targetDate.
    /// 3. For each series: loads its exceptions and template subtasks, calls
    ///    IRecurringTaskMaterializerService.AdvanceHorizon(), persists the new TaskItems + Subtasks,
    ///    advances MaterializedUpToDate, and commits — one SaveChangesAsync() per series
    ///    (limits blast radius in case of failure).
    /// </summary>
    public sealed class RecurringTaskHorizonWorker : BackgroundService
    {
        private readonly ILogger<RecurringTaskHorizonWorker> _logger;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly RecurringTaskHorizonWorkerOptions _workerOptions;
        private readonly RecurringTaskOptions _recurringOptions;

        public RecurringTaskHorizonWorker(
            ILogger<RecurringTaskHorizonWorker> logger,
            IServiceScopeFactory scopeFactory,
            IOptions<RecurringTaskHorizonWorkerOptions> workerOptions,
            IOptions<RecurringTaskOptions> recurringOptions)
        {
            _logger = logger;
            _scopeFactory = scopeFactory;
            _workerOptions = workerOptions.Value;
            _recurringOptions = recurringOptions.Value;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("RecurringTaskHorizonWorker started.");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await AdvanceHorizonOnceAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unexpected error in RecurringTaskHorizonWorker loop.");
                }

                try
                {
                    await Task.Delay(
                        TimeSpan.FromSeconds(_workerOptions.PollingIntervalSeconds),
                        stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }

            _logger.LogInformation("RecurringTaskHorizonWorker stopped.");
        }

        // -------------------------------------------------------------------------
        // Single iteration
        // -------------------------------------------------------------------------

        private async Task AdvanceHorizonOnceAsync(CancellationToken cancellationToken)
        {
            using var scope = _scopeFactory.CreateScope();
            var clock = scope.ServiceProvider.GetRequiredService<ISystemClock>();
            var seriesRepository = scope.ServiceProvider.GetRequiredService<IRecurringTaskSeriesRepository>();

            var utcNow = clock.UtcNow;
            var today = DateOnly.FromDateTime(utcNow);
            var targetDate = today.AddDays(_recurringOptions.HorizonWeeksAhead * 7);

            // Fetch series whose MaterializedUpToDate is behind the target horizon.
            var behindSeries = await seriesRepository.GetSeriesBehindHorizonAsync(
                targetDate, _workerOptions.MaxSeriesPerBatch, cancellationToken);

            if (behindSeries.Count == 0)
            {
                _logger.LogDebug("No recurring series behind horizon (target: {TargetDate}).", targetDate);
                return;
            }

            _logger.LogInformation(
                "RecurringTaskHorizonWorker: advancing horizon to {TargetDate} for {Count} series.",
                targetDate,
                behindSeries.Count);

            foreach (var series in behindSeries)
            {
                if (cancellationToken.IsCancellationRequested) break;

                try
                {
                    await AdvanceSeriesHorizonAsync(series, targetDate, utcNow, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    _logger.LogInformation(
                        "Cancellation requested while advancing series {SeriesId}.", series.Id);
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "Error advancing horizon for series {SeriesId}. Skipping to next.",
                        series.Id);
                }
            }
        }

        // -------------------------------------------------------------------------
        // Per-series horizon advance — one scope + one SaveChangesAsync per series
        // -------------------------------------------------------------------------

        private async Task AdvanceSeriesHorizonAsync(RecurringTaskSeries series,
                                                     DateOnly targetDate,
                                                     DateTime utcNow,
                                                     CancellationToken cancellationToken)
        {
            // Create a fresh scope per series for isolation.
            using var scope = _scopeFactory.CreateScope();
            var sp = scope.ServiceProvider;

            var seriesRepo = sp.GetRequiredService<IRecurringTaskSeriesRepository>();
            var exceptionRepo = sp.GetRequiredService<IRecurringTaskExceptionRepository>();
            var subtaskRepo = sp.GetRequiredService<IRecurringTaskSubtaskRepository>();
            var taskRepo = sp.GetRequiredService<ITaskRepository>();
            var regularSubtaskRepo = sp.GetRequiredService<ISubtaskRepository>();
            var materializerService = sp.GetRequiredService<IRecurringTaskMaterializerService>();
            var unitOfWork = sp.GetRequiredService<IUnitOfWork>();

            // Determine the window to materialize: (MaterializedUpToDate, targetDate].
            var fromInclusive = series.MaterializedUpToDate.AddDays(1);
            var toExclusive = targetDate.AddDays(1); // GenerateOccurrences uses exclusive upper bound

            // Load exceptions and template subtasks for the materialization range.
            var exceptions = await exceptionRepo.GetForSeriesInRangeAsync(series.Id,
                                                                          fromInclusive,
                                                                          toExclusive,
                                                                          cancellationToken);

            var templateSubtasks = await subtaskRepo.GetBySeriesIdAsync(series.Id, cancellationToken);

            // Batch-load exception subtasks to avoid N+1 queries.
            var exceptionIds = exceptions.Select(e => e.Id).ToList();
            IReadOnlyList<RecurringTaskSubtask> allExSubtasks = exceptionIds.Count > 0
                ? await subtaskRepo.GetByExceptionIdsAsync(exceptionIds, cancellationToken)
                : [];

            // Group exception subtasks by ExceptionId for O(1) lookup in the materializer.
            var exceptionSubtasksById = allExSubtasks
                .GroupBy(s => s.ExceptionId!.Value)
                .ToDictionary(
                    g => g.Key,
                    g => (IReadOnlyList<RecurringTaskSubtask>)g.ToList());

            // Materialize the advance batch.
            var batchResult = materializerService.AdvanceHorizon(series: series,
                                                                 templateSubtasks: templateSubtasks,
                                                                 exceptions: exceptions,
                                                                 exceptionSubtasksById: exceptionSubtasksById,
                                                                 targetDate: targetDate,
                                                                 utcNow: utcNow);

            if (batchResult.IsFailed)
            {
                _logger.LogError(
                    "Materializer failed for series {SeriesId}: {Errors}. Skipping series.",
                    series.Id,
                    string.Join("; ", batchResult.Errors.Select(e => e.Message)));
                return;
            }

            var batch = batchResult.Value;

            if (batch.Tasks.Count == 0 && batch.Subtasks.Count == 0)
            {
                // No new occurrences to materialize (e.g., series ended before targetDate).
                // Still advance the horizon so the worker doesn't revisit this series.
            }

            // Persist new TaskItems.
            foreach (var task in batch.Tasks)
            {
                await taskRepo.AddAsync(task, cancellationToken);
            }

            // Persist materialized Subtasks.
            foreach (var subtask in batch.Subtasks)
            {
                await regularSubtaskRepo.AddAsync(subtask, cancellationToken);
            }

            // Advance the MaterializedUpToDate on the series.
            // Re-fetch the series into this scope's change tracker so EF can update it.
            var trackedSeries = await seriesRepo.GetByIdAsync(series.Id, cancellationToken);
            if (trackedSeries is not null)
            {
                trackedSeries.AdvanceMaterializedHorizon(targetDate, utcNow);
                // Already tracked via GetByIdAsync — no explicit Update() needed.
            }

            // Single SaveChangesAsync per series — new tasks + subtasks + series horizon update.
            await unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogDebug(
                "Advanced horizon for series {SeriesId} to {TargetDate}. " +
                "Materialized {TaskCount} task(s) and {SubtaskCount} subtask(s).",
                series.Id,
                targetDate,
                batch.Tasks.Count,
                batch.Subtasks.Count);
        }
    }
}
