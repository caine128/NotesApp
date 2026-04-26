using FluentResults;
using MediatR;
using NotesApp.Application.Abstractions.Persistence;
using NotesApp.Application.Common;
using NotesApp.Application.Common.Interfaces;
using NotesApp.Domain.Common;
using NotesApp.Domain.Entities;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace NotesApp.Application.Tasks.Commands.DeleteRecurringTaskOccurrence
{
    /// <summary>
    /// Handles <see cref="DeleteRecurringTaskOccurrenceCommand"/>.
    ///
    /// Single scope       — creates a deletion RecurringTaskException (tombstone); also soft-deletes
    ///                      the materialized TaskItem when TaskItemId is provided.
    /// ThisAndFollowing   — terminates the series at the given date and bulk-soft-deletes all
    ///                      materialized TaskItems + exceptions from that date forward (change-tracker).
    /// All                — soft-deletes the root, all series, all TaskItems, all exceptions for the
    ///                      entire recurring family (change-tracker). Fully atomic.
    ///
    /// All scope operations are committed in a single SaveChangesAsync() call.
    /// </summary>
    public sealed class DeleteRecurringTaskOccurrenceCommandHandler
        : IRequestHandler<DeleteRecurringTaskOccurrenceCommand, Result>
    {
        private readonly ITaskRepository _taskRepository;
        private readonly IRecurringTaskRootRepository _rootRepository;
        private readonly IRecurringTaskSeriesRepository _seriesRepository;
        private readonly IRecurringTaskExceptionRepository _exceptionRepository;
        // REFACTORED: added for recurring-task-attachments feature
        private readonly IRecurringTaskAttachmentRepository _recurringAttachmentRepository;
        private readonly IOutboxRepository _outboxRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly ICurrentUserService _currentUserService;
        private readonly ISystemClock _clock;

        public DeleteRecurringTaskOccurrenceCommandHandler(ITaskRepository taskRepository,
                                                           IRecurringTaskRootRepository rootRepository,
                                                           IRecurringTaskSeriesRepository seriesRepository,
                                                           IRecurringTaskExceptionRepository exceptionRepository,
                                                           IRecurringTaskAttachmentRepository recurringAttachmentRepository,
                                                           IOutboxRepository outboxRepository,
                                                           IUnitOfWork unitOfWork,
                                                           ICurrentUserService currentUserService,
                                                           ISystemClock clock)
        {
            _taskRepository = taskRepository;
            _rootRepository = rootRepository;
            _seriesRepository = seriesRepository;
            _exceptionRepository = exceptionRepository;
            _recurringAttachmentRepository = recurringAttachmentRepository;
            _outboxRepository = outboxRepository;
            _unitOfWork = unitOfWork;
            _currentUserService = currentUserService;
            _clock = clock;
        }

        public async Task<Result> Handle(DeleteRecurringTaskOccurrenceCommand command,
                                         CancellationToken cancellationToken)
        {
            var userId = await _currentUserService.GetUserIdAsync(cancellationToken);
            var utcNow = _clock.UtcNow;

            return command.Scope switch
            {
                RecurringDeleteScope.Single => await HandleSingleAsync(command, userId, utcNow, cancellationToken),
                RecurringDeleteScope.ThisAndFollowing => await HandleThisAndFollowingAsync(command, userId, utcNow, cancellationToken),
                RecurringDeleteScope.All => await HandleAllAsync(command, userId, utcNow, cancellationToken),
                _ => Result.Fail($"Unknown RecurringDeleteScope: {command.Scope}")
            };
        }

        // -------------------------------------------------------------------------
        // Single scope
        // -------------------------------------------------------------------------

        private async Task<Result> HandleSingleAsync(DeleteRecurringTaskOccurrenceCommand command,
                                                     Guid userId,
                                                     DateTime utcNow,
                                                     CancellationToken cancellationToken)
        {
            // Check whether an exception already exists for this occurrence (upsert semantics).
            var existing = await _exceptionRepository.GetByOccurrenceAsync(command.SeriesId,
                                                                           command.OccurrenceDate,
                                                                           cancellationToken);

            // Determine the materialized TaskItem FK to record in the exception (may be null for virtual).
            Guid? materializedTaskItemId = null;

            if (command.TaskItemId.HasValue)
            {
                // Materialized occurrence — load and soft-delete the TaskItem.
                var task = await _taskRepository.GetByIdAsync(command.TaskItemId.Value, cancellationToken);

                if (task is null || task.UserId != userId)
                {
                    return Result.Fail(new Error("Task not found or does not belong to you.")
                        .WithMetadata("ErrorCode", "Tasks.NotFound"));
                }

                var deleteResult = task.SoftDelete(utcNow);
                if (deleteResult.IsFailure)
                {
                    return Result.Fail(deleteResult.Errors.Select(e => new Error(e.Message)));
                }

                // Task is already tracked; EF will persist the soft-delete on SaveChangesAsync.
                materializedTaskItemId = task.Id;
            }

            if (existing is not null)
            {
                // Convert the existing exception into a deletion in-place (idempotent on already-deletion rows).
                // Avoids leaving a duplicate (soft-deleted override + new live deletion) row pair.
                var convertResult = existing.ConvertToDeletion(materializedTaskItemId, utcNow);
                if (convertResult.IsFailure)
                {
                    return Result.Fail(convertResult.Errors.Select(e => new Error(e.Message)));
                }

                _exceptionRepository.Update(existing);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                return Result.Ok();
            }

            // No existing exception — create a fresh deletion tombstone.
            var exceptionResult = RecurringTaskException.CreateDeletion(userId: userId,
                                                                        seriesId: command.SeriesId,
                                                                        occurrenceDate: command.OccurrenceDate,
                                                                        materializedTaskItemId: materializedTaskItemId,
                                                                        utcNow: utcNow);

            if (exceptionResult.IsFailure)
            {
                return Result.Fail(exceptionResult.Errors.Select(e => new Error(e.Message)));
            }

            await _exceptionRepository.AddAsync(exceptionResult.Value, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result.Ok();
        }

        // -------------------------------------------------------------------------
        // ThisAndFollowing scope
        // -------------------------------------------------------------------------

        private async Task<Result> HandleThisAndFollowingAsync(DeleteRecurringTaskOccurrenceCommand command,
                                                               Guid userId,
                                                               DateTime utcNow,
                                                               CancellationToken cancellationToken)
        {
            // Load the series (untracked) to terminate it.
            var series = await _seriesRepository.GetByIdUntrackedAsync(command.SeriesId, cancellationToken);

            if (series is null || series.UserId != userId)
            {
                return Result.Fail(new Error("Recurring series not found or does not belong to you.")
                    .WithMetadata("ErrorCode", "RecurringSeries.NotFound"));
            }

            // Terminate the series at occurrenceDate (exclusive upper bound for this segment).
            var terminateResult = series.Terminate(command.OccurrenceDate, utcNow);
            if (terminateResult.IsFailure)
            {
                return Result.Fail(terminateResult.Errors.Select(e => new Error(e.Message)));
            }

            _seriesRepository.Update(series);

            // Bulk soft-delete all materialized TaskItems from this date forward (change-tracker pattern).
            await _taskRepository.SoftDeleteRecurringFromDateAsync(command.SeriesId,
                                                                   command.OccurrenceDate,
                                                                   userId,
                                                                   utcNow,
                                                                   cancellationToken);

            // Soft-delete exception attachments for each exception being removed.
            // Required because ExceptionId FK uses DeleteBehavior.Restrict (no DB-level cascade).
            var exceptionsToRemove = await _exceptionRepository.GetForSeriesInRangeAsync(
                command.SeriesId, command.OccurrenceDate, DateOnly.MaxValue, cancellationToken);

            foreach (var ex in exceptionsToRemove)
            {
                await _recurringAttachmentRepository.SoftDeleteAllForExceptionAsync(
                    ex.Id, userId, utcNow, cancellationToken);
            }

            // Bulk soft-delete all exceptions from this date forward (change-tracker pattern).
            await _exceptionRepository.SoftDeleteFromDateAsync(command.SeriesId,
                                                               command.OccurrenceDate,
                                                               userId,
                                                               utcNow,
                                                               cancellationToken);

            // Single SaveChangesAsync — terminates series + deletes tasks + exception attachments + exceptions atomically.
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result.Ok();
        }

        // -------------------------------------------------------------------------
        // All scope
        // -------------------------------------------------------------------------

        private async Task<Result> HandleAllAsync(DeleteRecurringTaskOccurrenceCommand command,
                                                  Guid userId,
                                                  DateTime utcNow,
                                                  CancellationToken cancellationToken)
        {
            // Resolve the series to get the RootId.
            var series = await _seriesRepository.GetByIdUntrackedAsync(command.SeriesId, cancellationToken);

            if (series is null || series.UserId != userId)
            {
                return Result.Fail(new Error("Recurring series not found or does not belong to you.")
                    .WithMetadata("ErrorCode", "RecurringSeries.NotFound"));
            }

            var rootId = series.RootId;

            // Load and soft-delete the root.
            var root = await _rootRepository.GetByIdAsync(rootId, cancellationToken);

            if (root is null || root.UserId != userId)
            {
                return Result.Fail(new Error("Recurring root not found or does not belong to you.")
                    .WithMetadata("ErrorCode", "RecurringRoot.NotFound"));
            }

            var rootSoftDeleteResult = root.SoftDelete(utcNow);
            if (rootSoftDeleteResult.IsFailure)
            {
                return Result.Fail(rootSoftDeleteResult.Errors.Select(e => new Error(e.Message)));
            }
            // root is already tracked via GetByIdAsync.

            // Bulk soft-delete all materialized TaskItems for the root (change-tracker pattern).
            await _taskRepository.SoftDeleteAllForRootAsync(rootId, userId, utcNow, cancellationToken);

            // Soft-delete all recurring attachments (series template + exception-scoped) for the root.
            // Required because ExceptionId FK uses DeleteBehavior.Restrict (no DB-level cascade).
            var allSeries = await _seriesRepository.GetActiveByRootIdAsync(rootId, userId, cancellationToken);

            foreach (var s in allSeries)
            {
                await _recurringAttachmentRepository.SoftDeleteAllForSeriesAsync(
                    s.Id, userId, utcNow, cancellationToken);

                var seriesExceptions = await _exceptionRepository.GetForSeriesInRangeAsync(
                    s.Id, DateOnly.MinValue, DateOnly.MaxValue, cancellationToken);

                foreach (var ex in seriesExceptions)
                {
                    await _recurringAttachmentRepository.SoftDeleteAllForExceptionAsync(
                        ex.Id, userId, utcNow, cancellationToken);
                }
            }

            // Bulk soft-delete all exceptions for the root (change-tracker pattern).
            await _exceptionRepository.SoftDeleteAllForRootAsync(rootId, userId, utcNow, cancellationToken);

            // Bulk soft-delete all series segments for the root (change-tracker pattern).
            await _seriesRepository.SoftDeleteAllForRootAsync(rootId, userId, utcNow, cancellationToken);

            // Single SaveChangesAsync — root + all series + all tasks + all attachments + all exceptions atomically.
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result.Ok();
        }
    }
}
