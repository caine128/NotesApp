using FluentResults;
using MediatR;
using Microsoft.Extensions.Logging;
using NotesApp.Application.Abstractions.Persistence;
using NotesApp.Application.Common;
using NotesApp.Application.Common.Interfaces;
using NotesApp.Domain.Common;
using NotesApp.Domain.Entities;
using System;
using System.Linq;

namespace NotesApp.Application.RecurringAttachments.Commands.DeleteRecurringTaskOccurrenceAttachment
{
    /// <summary>
    /// Handles deletion of an attachment from a specific recurring task occurrence.
    ///
    /// Two cases depending on which FK the attachment carries:
    ///
    /// Case A — attachment has SeriesId (series template attachment):
    ///   The occurrence currently inherits the series attachment list. Deleting "for this occurrence"
    ///   means: promote to exception, copy all series attachments EXCEPT the deleted one as
    ///   exception-scoped rows, mark HasAttachmentOverride. The series template attachment is NOT
    ///   modified (other occurrences still inherit it).
    ///
    /// Case B — attachment has ExceptionId (already an exception attachment):
    ///   Simple soft-delete. The exception already owns its attachment list.
    ///   The blob is cleaned up later by the orphan-cleanup worker (shared-blob guard applies).
    /// </summary>
    // REFACTORED: added for recurring-task-attachments feature
    public sealed class DeleteRecurringTaskOccurrenceAttachmentCommandHandler
        : IRequestHandler<DeleteRecurringTaskOccurrenceAttachmentCommand, Result>
    {
        private readonly IRecurringTaskSeriesRepository _seriesRepository;
        private readonly IRecurringTaskExceptionRepository _exceptionRepository;
        private readonly IRecurringTaskAttachmentRepository _attachmentRepository;
        private readonly IOutboxRepository _outboxRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly ICurrentUserService _currentUserService;
        private readonly ISystemClock _clock;
        private readonly ILogger<DeleteRecurringTaskOccurrenceAttachmentCommandHandler> _logger;

        public DeleteRecurringTaskOccurrenceAttachmentCommandHandler(
            IRecurringTaskSeriesRepository seriesRepository,
            IRecurringTaskExceptionRepository exceptionRepository,
            IRecurringTaskAttachmentRepository attachmentRepository,
            IOutboxRepository outboxRepository,
            IUnitOfWork unitOfWork,
            ICurrentUserService currentUserService,
            ISystemClock clock,
            ILogger<DeleteRecurringTaskOccurrenceAttachmentCommandHandler> logger)
        {
            _seriesRepository = seriesRepository;
            _exceptionRepository = exceptionRepository;
            _attachmentRepository = attachmentRepository;
            _outboxRepository = outboxRepository;
            _unitOfWork = unitOfWork;
            _currentUserService = currentUserService;
            _clock = clock;
            _logger = logger;
        }

        public async Task<Result> Handle(
            DeleteRecurringTaskOccurrenceAttachmentCommand command,
            CancellationToken cancellationToken)
        {
            var userId = await _currentUserService.GetUserIdAsync(cancellationToken);
            var utcNow = _clock.UtcNow;

            var attachment = await _attachmentRepository.GetByIdUntrackedAsync(
                command.AttachmentId, cancellationToken);

            if (attachment is null || attachment.UserId != userId)
            {
                _logger.LogWarning(
                    "DeleteRecurringOccurrenceAttachment failed: attachment {AttachmentId} not found for user {UserId}.",
                    command.AttachmentId, userId);

                return Result.Fail(
                    new Error("Recurring attachment not found.")
                        .WithMetadata("ErrorCode", "RecurringAttachments.NotFound"));
            }

            if (attachment.SeriesId.HasValue)
                return await HandleCaseAAsync(attachment, command, userId, utcNow, cancellationToken);

            if (attachment.ExceptionId.HasValue)
                return await HandleCaseBAsync(attachment, command, userId, utcNow, cancellationToken);

            // Defensive: DB check constraint guarantees exactly one FK is set.
            return Result.Fail(
                new Error("Recurring attachment is in an invalid state.")
                    .WithMetadata("ErrorCode", "RecurringAttachments.Invalid"));
        }

        // ─── Case A — series template attachment ─────────────────────────────────

        /// <summary>
        /// The attachment is a series template attachment. The occurrence currently inherits the
        /// series list. Promote the occurrence to an exception and copy all series attachments
        /// EXCEPT the one being deleted. The series template attachment itself is left intact.
        /// </summary>
        private async Task<Result> HandleCaseAAsync(
            RecurringTaskAttachment attachment,
            DeleteRecurringTaskOccurrenceAttachmentCommand command,
            Guid userId,
            DateTime utcNow,
            CancellationToken cancellationToken)
        {
            if (attachment.SeriesId != command.SeriesId)
            {
                return Result.Fail(
                    new Error("Recurring attachment not found.")
                        .WithMetadata("ErrorCode", "RecurringAttachments.NotFound"));
            }

            var exception = await _exceptionRepository.GetByOccurrenceAsync(
                command.SeriesId, command.OccurrenceDate, cancellationToken);

            if (exception is not null && exception.IsDeletion)
            {
                return Result.Fail(new Error("RecurringOccurrence.Deleted")
                    .WithMetadata("Message", "Cannot delete an attachment from a deleted occurrence."));
            }

            // If the occurrence already has its own attachment list (HasAttachmentOverride=true),
            // the series template attachment is not visible for this occurrence.
            // The client should be targeting an exception attachment ID instead.
            if (exception is not null && exception.HasAttachmentOverride)
            {
                return Result.Fail(
                    new Error("Recurring attachment not found.")
                        .WithMetadata("ErrorCode", "RecurringAttachments.NotFound"));
            }

            RecurringTaskException target;

            if (exception is null)
            {
                // No exception exists yet — create one inheriting all series template fields.
                var series = await _seriesRepository.GetByIdUntrackedAsync(command.SeriesId, cancellationToken);

                if (series is null || series.UserId != userId)
                {
                    return Result.Fail(
                        new Error("Recurring series not found.")
                            .WithMetadata("ErrorCode", "RecurringSeries.NotFound"));
                }

                var exResult = RecurringTaskException.CreateOverride(
                    userId: userId,
                    seriesId: command.SeriesId,
                    occurrenceDate: command.OccurrenceDate,
                    overrideTitle: series.Title,
                    overrideDescription: series.Description,
                    overrideDate: null,
                    overrideStartTime: series.StartTime,
                    overrideEndTime: series.EndTime,
                    overrideLocation: series.Location,
                    overrideTravelTime: series.TravelTime,
                    overrideCategoryId: series.CategoryId,
                    overridePriority: series.Priority,
                    overrideMeetingLink: series.MeetingLink,
                    overrideReminderAtUtc: null,
                    isCompleted: false,
                    materializedTaskItemId: null,
                    utcNow: utcNow);

                if (exResult.IsFailure)
                    return Result.Fail(exResult.Errors.Select(e => new Error(e.Message)));

                target = exResult.Value;
                await _exceptionRepository.AddAsync(target, cancellationToken);
            }
            else
            {
                target = exception;
            }

            // Copy all series template attachments EXCEPT the one being deleted.
            var seriesAttachments = await _attachmentRepository.GetBySeriesIdAsync(
                command.SeriesId, cancellationToken);

            foreach (var template in seriesAttachments.Where(a => a.Id != command.AttachmentId))
            {
                var copyResult = RecurringTaskAttachment.CreateForException(
                    id: Guid.NewGuid(),
                    userId: userId,
                    exceptionId: target.Id,
                    fileName: template.FileName,
                    contentType: template.ContentType,
                    sizeBytes: template.SizeBytes,
                    blobPath: template.BlobPath,
                    displayOrder: template.DisplayOrder,
                    utcNow: utcNow);

                if (copyResult.IsFailure)
                    return Result.Fail(copyResult.Errors.Select(e => new Error(e.Message)));

                var copy = copyResult.Value!;
                var copyPayload = OutboxPayloadBuilder.BuildRecurringAttachmentPayload(copy, Guid.Empty);

                var copyOutboxResult = OutboxMessage.Create<RecurringTaskAttachment, RecurringAttachmentEventType>(
                    copy, RecurringAttachmentEventType.Created, copyPayload, utcNow);

                if (copyOutboxResult.IsFailure || copyOutboxResult.Value is null)
                    return Result.Fail(copyOutboxResult.Errors.Select(e => new Error(e.Message)));

                await _attachmentRepository.AddAsync(copy, cancellationToken);
                await _outboxRepository.AddAsync(copyOutboxResult.Value, cancellationToken);
            }

            target.MarkAttachmentsOverridden(utcNow);

            if (exception is not null)
            {
                // Existing exception was loaded untracked — attach it to the change tracker.
                _exceptionRepository.Update(target);
            }
            // Newly created exception is already tracked via AddAsync;
            // MarkAttachmentsOverridden's in-memory mutation will be included in SaveChangesAsync.

            _logger.LogInformation(
                "Recurring occurrence attachment Case A: series attachment {AttachmentId} excluded from copy for series {SeriesId}, date {OccurrenceDate}. Exception {ExceptionId}.",
                command.AttachmentId, command.SeriesId, command.OccurrenceDate, target.Id);

            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result.Ok();
        }

        // ─── Case B — exception attachment ───────────────────────────────────────

        /// <summary>
        /// The attachment already belongs to an exception. Simple soft-delete.
        /// Verifies that the attachment's ExceptionId matches the exception for the given
        /// (seriesId, occurrenceDate) to prevent cross-occurrence manipulation.
        /// </summary>
        private async Task<Result> HandleCaseBAsync(
            RecurringTaskAttachment attachment,
            DeleteRecurringTaskOccurrenceAttachmentCommand command,
            Guid userId,
            DateTime utcNow,
            CancellationToken cancellationToken)
        {
            var exception = await _exceptionRepository.GetByOccurrenceAsync(
                command.SeriesId, command.OccurrenceDate, cancellationToken);

            if (exception is null || exception.Id != attachment.ExceptionId)
            {
                _logger.LogWarning(
                    "DeleteRecurringOccurrenceAttachment Case B: attachment {AttachmentId} does not belong to occurrence ({SeriesId}, {OccurrenceDate}) for user {UserId}.",
                    command.AttachmentId, command.SeriesId, command.OccurrenceDate, userId);

                return Result.Fail(
                    new Error("Recurring attachment not found.")
                        .WithMetadata("ErrorCode", "RecurringAttachments.NotFound"));
            }

            var deleteResult = attachment.SoftDelete(utcNow);

            if (deleteResult.IsFailure)
                return deleteResult.ToResult();

            var payload = OutboxPayloadBuilder.BuildRecurringAttachmentPayload(attachment, Guid.Empty);

            var outboxResult = OutboxMessage.Create<RecurringTaskAttachment, RecurringAttachmentEventType>(
                aggregate: attachment,
                eventType: RecurringAttachmentEventType.Deleted,
                payload: payload,
                utcNow: utcNow);

            if (outboxResult.IsFailure)
                return outboxResult.ToResult();

            attachment.ApplyClientRowVersion(command.RowVersion);
            _attachmentRepository.Update(attachment);
            await _outboxRepository.AddAsync(outboxResult.Value!, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Recurring exception attachment {AttachmentId} soft-deleted for user {UserId}.",
                attachment.Id, userId);

            return Result.Ok();
        }
    }
}
