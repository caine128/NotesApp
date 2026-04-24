using FluentResults;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NotesApp.Application.Abstractions.Persistence;
using NotesApp.Application.Abstractions.Storage;
using NotesApp.Application.Common;
using NotesApp.Application.Common.Interfaces;
using NotesApp.Application.Configuration;
using NotesApp.Application.RecurringAttachments.Models;
using NotesApp.Domain.Common;
using NotesApp.Domain.Entities;
using System;
using System.Linq;

namespace NotesApp.Application.RecurringAttachments.Commands.UploadRecurringTaskOccurrenceAttachment
{
    /// <summary>
    /// Handles upload of an attachment to a specific recurring task occurrence.
    ///
    /// Workflow:
    /// 1. [Validator] Input validation
    /// 2. Load series WITHOUT tracking; verify ownership
    /// 3. Validate content type against AllowedContentTypes
    /// 4. Load exception for (seriesId, occurrenceDate); reject deletion exceptions
    /// 5. Determine needsFirstTouch = exception is null || !exception.HasAttachmentOverride
    /// 6. Count effective attachments (series count if needsFirstTouch; else exception count) and enforce limit
    /// 7. If needsFirstTouch:
    ///    a. Create exception if absent (inheriting all series template fields)
    ///    b. Copy all series template attachments to exception as CreateForException rows
    ///    c. MarkAttachmentsOverridden on exception
    /// 8. Upload binary to blob storage ← POINT OF NO RETURN
    /// 9. CreateForException for the new attachment + outbox Created
    /// 10. Persist everything atomically (exception + copies + new attachment + all outbox messages)
    /// 11. Return download URL (best effort — may be null on transient failure)
    /// </summary>
    // REFACTORED: added for recurring-task-attachments feature
    public sealed class UploadRecurringTaskOccurrenceAttachmentCommandHandler
        : IRequestHandler<UploadRecurringTaskOccurrenceAttachmentCommand, Result<UploadRecurringAttachmentResultDto>>
    {
        private readonly IRecurringTaskSeriesRepository _seriesRepository;
        private readonly IRecurringTaskExceptionRepository _exceptionRepository;
        private readonly IRecurringTaskAttachmentRepository _attachmentRepository;
        private readonly IBlobStorageService _blobStorageService;
        private readonly ICurrentUserService _currentUserService;
        private readonly IOutboxRepository _outboxRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly ISystemClock _clock;
        private readonly AttachmentStorageOptions _options;
        private readonly ILogger<UploadRecurringTaskOccurrenceAttachmentCommandHandler> _logger;

        public UploadRecurringTaskOccurrenceAttachmentCommandHandler(
            IRecurringTaskSeriesRepository seriesRepository,
            IRecurringTaskExceptionRepository exceptionRepository,
            IRecurringTaskAttachmentRepository attachmentRepository,
            IBlobStorageService blobStorageService,
            ICurrentUserService currentUserService,
            IOutboxRepository outboxRepository,
            IUnitOfWork unitOfWork,
            ISystemClock clock,
            IOptions<AttachmentStorageOptions> options,
            ILogger<UploadRecurringTaskOccurrenceAttachmentCommandHandler> logger)
        {
            _seriesRepository = seriesRepository;
            _exceptionRepository = exceptionRepository;
            _attachmentRepository = attachmentRepository;
            _blobStorageService = blobStorageService;
            _currentUserService = currentUserService;
            _outboxRepository = outboxRepository;
            _unitOfWork = unitOfWork;
            _clock = clock;
            _options = options.Value;
            _logger = logger;
        }

        public async Task<Result<UploadRecurringAttachmentResultDto>> Handle(
            UploadRecurringTaskOccurrenceAttachmentCommand request,
            CancellationToken cancellationToken)
        {
            var userId = await _currentUserService.GetUserIdAsync(cancellationToken);
            var utcNow = _clock.UtcNow;

            _logger.LogInformation(
                "Recurring occurrence attachment upload requested for series {SeriesId}, date {OccurrenceDate} by user {UserId}",
                request.SeriesId, request.OccurrenceDate, userId);

            var series = await _seriesRepository.GetByIdUntrackedAsync(request.SeriesId, cancellationToken);

            if (series is null || series.UserId != userId || series.IsDeleted)
            {
                return Result.Fail(new Error("RecurringSeries.NotFound")
                    .WithMetadata("Message", "Recurring series not found."));
            }

            var normalizedContentType = string.IsNullOrWhiteSpace(request.ContentType)
                ? "application/octet-stream"
                : request.ContentType.Trim();

            if (_options.AllowedContentTypes.Count > 0 &&
                !_options.AllowedContentTypes.Any(ct =>
                    string.Equals(ct, normalizedContentType, StringComparison.OrdinalIgnoreCase)))
            {
                return Result.Fail(new Error("RecurringAttachment.ContentType.NotAllowed")
                    .WithMetadata("Message",
                        $"Content type '{normalizedContentType}' is not allowed for recurring task attachments."));
            }

            var exception = await _exceptionRepository.GetByOccurrenceAsync(
                request.SeriesId, request.OccurrenceDate, cancellationToken);

            if (exception is not null && exception.IsDeletion)
            {
                return Result.Fail(new Error("RecurringOccurrence.Deleted")
                    .WithMetadata("Message", "Cannot upload an attachment to a deleted occurrence."));
            }

            var needsFirstTouch = exception is null || !exception.HasAttachmentOverride;

            int existingCount;

            if (needsFirstTouch)
            {
                existingCount = await _attachmentRepository.CountForSeriesAsync(
                    request.SeriesId, userId, cancellationToken);
            }
            else
            {
                existingCount = await _attachmentRepository.CountForExceptionAsync(
                    exception!.Id, userId, cancellationToken);
            }

            if (existingCount >= _options.MaxAttachmentsPerTask)
            {
                return Result.Fail(new Error("RecurringAttachment.LimitExceeded")
                    .WithMetadata("Message",
                        $"Occurrence already has the maximum number of attachments ({_options.MaxAttachmentsPerTask})."));
            }

            if (needsFirstTouch)
            {
                var stageResult = await StageFirstTouchAsync(
                    series, exception, request.SeriesId, request.OccurrenceDate,
                    userId, utcNow, cancellationToken);

                if (stageResult.IsFailed)
                    return stageResult.ToResult();

                exception = stageResult.Value;
            }

            // ─── POINT OF NO RETURN: blob upload ────────────────────────────────
            var attachmentId = Guid.NewGuid();
            var blobPath = GenerateExceptionBlobPath(userId, exception!.Id, attachmentId, request.FileName);

            var uploadResult = await _blobStorageService.UploadAsync(
                _options.ContainerName, blobPath, request.Content, normalizedContentType, cancellationToken);

            if (uploadResult.IsFailed)
            {
                _logger.LogError(
                    "Failed to upload recurring occurrence attachment to blob storage for series {SeriesId}, date {OccurrenceDate}: {Errors}",
                    request.SeriesId, request.OccurrenceDate,
                    string.Join(", ", uploadResult.Errors.Select(e => e.Message)));

                return Result.Fail(new Error("RecurringAttachment.Upload.Failed")
                    .WithMetadata("Message", "Failed to upload attachment to storage."));
            }

            _logger.LogInformation("Recurring occurrence attachment blob uploaded: {BlobPath}, Size: {SizeBytes}",
                                   blobPath, uploadResult.Value.SizeBytes);

            var displayOrder = existingCount + 1;

            var attachmentResult = RecurringTaskAttachment.CreateForException(
                id: attachmentId,
                userId: userId,
                exceptionId: exception.Id,
                fileName: request.FileName,
                contentType: normalizedContentType,
                sizeBytes: uploadResult.Value.SizeBytes,
                blobPath: blobPath,
                displayOrder: displayOrder,
                utcNow: utcNow);

            if (attachmentResult.IsFailure)
            {
                _logger.LogError(
                    "Failed to create RecurringTaskAttachment entity: {Errors}",
                    string.Join(", ", attachmentResult.Errors.Select(e => e.Message)));

                await CleanupBlobAsync(blobPath, cancellationToken);

                return Result.Fail(new Error("RecurringAttachment.Create.Failed")
                    .WithMetadata("Message", "Failed to create attachment record."));
            }

            var attachment = attachmentResult.Value!;

            var payload = OutboxPayloadBuilder.BuildRecurringAttachmentPayload(attachment, Guid.Empty);

            var outboxResult = OutboxMessage.Create<RecurringTaskAttachment, RecurringAttachmentEventType>(
                attachment, RecurringAttachmentEventType.Created, payload, utcNow);

            if (outboxResult.IsFailure || outboxResult.Value is null)
            {
                _logger.LogError(
                    "Failed to create recurring attachment outbox message for occurrence {OccurrenceDate} of series {SeriesId}",
                    request.OccurrenceDate, request.SeriesId);

                await CleanupBlobAsync(blobPath, cancellationToken);

                return Result.Fail(new Error("Outbox.RecurringAttachment.CreateFailed")
                    .WithMetadata("Message", "Failed to create attachment sync event."));
            }

            await _attachmentRepository.AddAsync(attachment, cancellationToken);
            await _outboxRepository.AddAsync(outboxResult.Value, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            // Best-effort download URL (failure here does not roll back the upload)
            string? downloadUrl = null;

            var downloadUrlResult = await _blobStorageService.GenerateDownloadUrlAsync(
                _options.ContainerName, blobPath, _options.DownloadUrlValidity, cancellationToken);

            if (downloadUrlResult.IsFailed)
            {
                _logger.LogWarning(
                    "Failed to generate download URL for recurring attachment {AttachmentId}. " +
                    "Attachment was created successfully — client can fetch URL separately. Errors: {Errors}",
                    attachment.Id, string.Join(", ", downloadUrlResult.Errors.Select(e => e.Message)));
            }
            else
            {
                downloadUrl = downloadUrlResult.Value;
            }

            _logger.LogInformation(
                "Recurring occurrence attachment {AttachmentId} created for series {SeriesId}, date {OccurrenceDate} (DisplayOrder={DisplayOrder})",
                attachment.Id, request.SeriesId, request.OccurrenceDate, displayOrder);

            return Result.Ok(new UploadRecurringAttachmentResultDto
            {
                AttachmentId = attachment.Id,
                SeriesId = null,
                ExceptionId = attachment.ExceptionId,
                DisplayOrder = attachment.DisplayOrder,
                DownloadUrl = downloadUrl
            });
        }

        /// <summary>
        /// Promotes the occurrence to a RecurringTaskException (if not already one) and copies
        /// all current series template attachments as exception-scoped rows (first-touch copy).
        /// Returns the exception entity, already staged in the EF change tracker.
        /// </summary>
        private async Task<Result<RecurringTaskException>> StageFirstTouchAsync(
            RecurringTaskSeries series,
            RecurringTaskException? existingException,
            Guid seriesId,
            DateOnly occurrenceDate,
            Guid userId,
            DateTime utcNow,
            CancellationToken cancellationToken)
        {
            RecurringTaskException exception;

            if (existingException is null)
            {
                var exResult = RecurringTaskException.CreateOverride(
                    userId: userId,
                    seriesId: seriesId,
                    occurrenceDate: occurrenceDate,
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

                exception = exResult.Value;
                await _exceptionRepository.AddAsync(exception, cancellationToken);
            }
            else
            {
                // Exception exists but HasAttachmentOverride = false.
                exception = existingException;
            }

            // Copy all series template attachments as exception-scoped rows (same BlobPath, new IDs).
            var seriesAttachments = await _attachmentRepository.GetBySeriesIdAsync(seriesId, cancellationToken);

            foreach (var template in seriesAttachments)
            {
                var copyResult = RecurringTaskAttachment.CreateForException(
                    id: Guid.NewGuid(),
                    userId: userId,
                    exceptionId: exception.Id,
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

            exception.MarkAttachmentsOverridden(utcNow);

            if (existingException is not null)
            {
                // Existing exception was loaded untracked — attach it to the change tracker.
                _exceptionRepository.Update(exception);
            }
            // Newly created exception is already tracked via AddAsync;
            // MarkAttachmentsOverridden's in-memory mutation will be included in SaveChangesAsync.

            return Result.Ok(exception);
        }

        private async Task CleanupBlobAsync(string blobPath, CancellationToken cancellationToken)
        {
            var deleteResult = await _blobStorageService.DeleteAsync(
                _options.ContainerName, blobPath, cancellationToken);

            if (deleteResult.IsFailed)
            {
                _logger.LogWarning(
                    "Failed to clean up recurring occurrence attachment blob after failure: {BlobPath}. Errors: {Errors}",
                    blobPath, string.Join(", ", deleteResult.Errors.Select(e => e.Message)));
            }
        }

        private static string GenerateExceptionBlobPath(Guid userId, Guid exceptionId, Guid attachmentId, string fileName)
        {
            var sanitizedFileName = SanitizeFileName(fileName);
            return $"{userId}/recurring-exception-attachments/{exceptionId}/{attachmentId}/{sanitizedFileName}";
        }

        private static string SanitizeFileName(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
                return "file";

            var invalid = new[] { '/', '\\', ':', '*', '?', '"', '<', '>', '|' };
            var sanitized = fileName;

            foreach (var c in invalid)
                sanitized = sanitized.Replace(c, '_');

            return string.IsNullOrWhiteSpace(sanitized) ? "file" : sanitized.Trim();
        }
    }
}
