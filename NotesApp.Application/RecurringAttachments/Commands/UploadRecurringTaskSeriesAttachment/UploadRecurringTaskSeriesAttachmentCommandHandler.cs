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

namespace NotesApp.Application.RecurringAttachments.Commands.UploadRecurringTaskSeriesAttachment
{
    /// <summary>
    /// Handles recurring task series template attachment uploads.
    /// Mirrors <c>UploadAttachmentCommandHandler</c>, replacing TaskItem → RecurringTaskSeries.
    ///
    /// Workflow:
    /// 1. [Validator] Input validation
    /// 2. Load series WITHOUT tracking; verify ownership
    /// 3. Validate content type against AllowedContentTypes
    /// 4. Count existing series template attachments and enforce MaxAttachmentsPerTask
    /// 5. Upload binary to blob storage ← POINT OF NO RETURN
    /// 6. Create RecurringTaskAttachment entity and outbox message
    /// 7. Persist everything atomically
    /// 8. Return download URL (best effort — may be null on transient failure)
    /// </summary>
    // REFACTORED: added for recurring-task-attachments feature
    public sealed class UploadRecurringTaskSeriesAttachmentCommandHandler
        : IRequestHandler<UploadRecurringTaskSeriesAttachmentCommand, Result<UploadRecurringAttachmentResultDto>>
    {
        private readonly IRecurringTaskSeriesRepository _seriesRepository;
        private readonly IRecurringTaskAttachmentRepository _attachmentRepository;
        private readonly IBlobStorageService _blobStorageService;
        private readonly ICurrentUserService _currentUserService;
        private readonly IOutboxRepository _outboxRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly ISystemClock _clock;
        private readonly AttachmentStorageOptions _options;
        private readonly ILogger<UploadRecurringTaskSeriesAttachmentCommandHandler> _logger;

        public UploadRecurringTaskSeriesAttachmentCommandHandler(
            IRecurringTaskSeriesRepository seriesRepository,
            IRecurringTaskAttachmentRepository attachmentRepository,
            IBlobStorageService blobStorageService,
            ICurrentUserService currentUserService,
            IOutboxRepository outboxRepository,
            IUnitOfWork unitOfWork,
            ISystemClock clock,
            IOptions<AttachmentStorageOptions> options,
            ILogger<UploadRecurringTaskSeriesAttachmentCommandHandler> logger)
        {
            _seriesRepository = seriesRepository;
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
            UploadRecurringTaskSeriesAttachmentCommand request,
            CancellationToken cancellationToken)
        {
            var userId = await _currentUserService.GetUserIdAsync(cancellationToken);
            var utcNow = _clock.UtcNow;

            _logger.LogInformation(
                "Recurring series attachment upload requested for series {SeriesId} by user {UserId}",
                request.SeriesId, userId);

            // Load series WITHOUT tracking
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

            var existingCount = await _attachmentRepository.CountForSeriesAsync(
                request.SeriesId, userId, cancellationToken);

            if (existingCount >= _options.MaxAttachmentsPerTask)
            {
                return Result.Fail(new Error("RecurringAttachment.LimitExceeded")
                    .WithMetadata("Message",
                        $"Series already has the maximum number of attachments ({_options.MaxAttachmentsPerTask})."));
            }

            // ─── POINT OF NO RETURN: blob upload ────────────────────────────────
            var attachmentId = Guid.NewGuid();
            var blobPath = GenerateSeriesBlobPath(userId, request.SeriesId, attachmentId, request.FileName);

            var uploadResult = await _blobStorageService.UploadAsync(
                _options.ContainerName, blobPath, request.Content, normalizedContentType, cancellationToken);

            if (uploadResult.IsFailed)
            {
                _logger.LogError(
                    "Failed to upload recurring series attachment to blob storage for series {SeriesId}: {Errors}",
                    request.SeriesId, string.Join(", ", uploadResult.Errors.Select(e => e.Message)));

                return Result.Fail(new Error("RecurringAttachment.Upload.Failed")
                    .WithMetadata("Message", "Failed to upload attachment to storage."));
            }

            _logger.LogInformation("Recurring series attachment blob uploaded: {BlobPath}, Size: {SizeBytes}",
                                   blobPath, uploadResult.Value.SizeBytes);

            var displayOrder = existingCount + 1;

            var attachmentResult = RecurringTaskAttachment.CreateForSeries(
                id: attachmentId,
                userId: userId,
                seriesId: request.SeriesId,
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
                    "Failed to create recurring attachment outbox message for series {SeriesId}",
                    request.SeriesId);

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
                "Recurring series attachment {AttachmentId} created for series {SeriesId} (DisplayOrder={DisplayOrder})",
                attachment.Id, request.SeriesId, displayOrder);

            return Result.Ok(new UploadRecurringAttachmentResultDto
            {
                AttachmentId = attachment.Id,
                SeriesId = attachment.SeriesId,
                ExceptionId = null,
                DisplayOrder = attachment.DisplayOrder,
                DownloadUrl = downloadUrl
            });
        }

        private async Task CleanupBlobAsync(string blobPath, CancellationToken cancellationToken)
        {
            var deleteResult = await _blobStorageService.DeleteAsync(
                _options.ContainerName, blobPath, cancellationToken);

            if (deleteResult.IsFailed)
            {
                _logger.LogWarning(
                    "Failed to clean up recurring series attachment blob after failure: {BlobPath}. Errors: {Errors}",
                    blobPath, string.Join(", ", deleteResult.Errors.Select(e => e.Message)));
            }
        }

        private static string GenerateSeriesBlobPath(Guid userId, Guid seriesId, Guid attachmentId, string fileName)
        {
            var sanitizedFileName = SanitizeFileName(fileName);
            return $"{userId}/recurring-series-attachments/{seriesId}/{attachmentId}/{sanitizedFileName}";
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
