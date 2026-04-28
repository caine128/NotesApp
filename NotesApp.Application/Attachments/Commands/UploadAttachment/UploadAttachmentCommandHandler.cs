using FluentResults;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NotesApp.Application.Abstractions.Persistence;
using NotesApp.Application.Abstractions.Storage;
using NotesApp.Application.Attachments.Models;
using NotesApp.Application.Common;
using NotesApp.Application.Common.Interfaces;
using NotesApp.Application.Configuration;
using NotesApp.Domain.Common;
using NotesApp.Domain.Entities;
using System;
using System.Linq;

namespace NotesApp.Application.Attachments.Commands.UploadAttachment
{
    /// <summary>
    /// Handles task attachment upload requests.
    ///
    /// Workflow:
    /// 1. [Validator] Input validation (TaskId, FileName, SizeBytes, Content)
    /// 2. Load task WITHOUT tracking
    /// 3. Validate task ownership and not-deleted state
    /// 4. Validate content type against AllowedContentTypes (runtime-configured)
    /// 5. Count existing attachments and enforce MaxAttachmentsPerTask
    /// 6. Upload binary to blob storage ← POINT OF NO RETURN
    /// 7. Create Attachment entity (in memory)
    /// 8. Create outbox message — FAIL if any error, with blob cleanup
    /// 9. Persist everything atomically
    /// 10. Return download URL (best effort — may be null on transient failure)
    ///
    /// If blob upload fails, the operation returns a failure (no DB changes).
    /// If any step after blob upload fails, the blob is cleaned up (best effort).
    /// </summary>
    public sealed class UploadAttachmentCommandHandler
        : IRequestHandler<UploadAttachmentCommand, Result<UploadAttachmentResultDto>>
    {
        private readonly ITaskRepository _taskRepository;
        private readonly IAttachmentRepository _attachmentRepository;
        private readonly IBlobStorageService _blobStorageService;
        private readonly ICurrentUserService _currentUserService;
        private readonly IOutboxRepository _outboxRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly ISystemClock _clock;
        private readonly AttachmentStorageOptions _options;
        private readonly ILogger<UploadAttachmentCommandHandler> _logger;

        public UploadAttachmentCommandHandler(ITaskRepository taskRepository,
                                              IAttachmentRepository attachmentRepository,
                                              IBlobStorageService blobStorageService,
                                              ICurrentUserService currentUserService,
                                              IOutboxRepository outboxRepository,
                                              IUnitOfWork unitOfWork,
                                              ISystemClock clock,
                                              IOptions<AttachmentStorageOptions> options,
                                              ILogger<UploadAttachmentCommandHandler> logger)
        {
            _taskRepository = taskRepository;
            _attachmentRepository = attachmentRepository;
            _blobStorageService = blobStorageService;
            _currentUserService = currentUserService;
            _outboxRepository = outboxRepository;
            _unitOfWork = unitOfWork;
            _clock = clock;
            _options = options.Value;
            _logger = logger;
        }

        public async Task<Result<UploadAttachmentResultDto>> Handle(UploadAttachmentCommand request,
                                                                    CancellationToken cancellationToken)
        {
            var userId = await _currentUserService.GetUserIdAsync(cancellationToken);
            var utcNow = _clock.UtcNow;

            _logger.LogInformation(
                "Attachment upload requested for task {TaskId} by user {UserId}",
                request.TaskId,
                userId);

            // ═══════════════════════════════════════════════════════════════════
            // PHASE 1: Business Validation (requires database access)
            //
            // Note: Input validation (TaskId, FileName, SizeBytes, Content) is
            // handled by UploadAttachmentCommandValidator.
            // ═══════════════════════════════════════════════════════════════════

            // Load task WITHOUT tracking
            var task = await _taskRepository.GetByIdUntrackedAsync(request.TaskId, cancellationToken);

            if (task is null || task.UserId != userId || task.IsDeleted)
            {
                return Result.Fail(new Error("Task not found.")
                    .WithMetadata("ErrorCode", "Tasks.NotFound"));
            }

            // Validate content type against the configured whitelist (runtime value)
            var normalizedContentType = string.IsNullOrWhiteSpace(request.ContentType)
                ? "application/octet-stream"
                : request.ContentType.Trim();

            if (_options.AllowedContentTypes.Count > 0 &&
                !_options.AllowedContentTypes.Any(ct =>
                    string.Equals(ct, normalizedContentType, StringComparison.OrdinalIgnoreCase)))
            {
                return Result.Fail(new Error("Attachment.ContentType.NotAllowed")
                    .WithMetadata("Message",
                        $"Content type '{normalizedContentType}' is not allowed for task attachments."));
            }

            // Enforce max attachments per task
            var existingCount = await _attachmentRepository.CountForTaskAsync(
                request.TaskId, userId, cancellationToken);

            if (existingCount >= _options.MaxAttachmentsPerTask)
            {
                return Result.Fail(new Error("Attachment.LimitExceeded")
                    .WithMetadata("Message",
                        $"Task already has the maximum number of attachments ({_options.MaxAttachmentsPerTask})."));
            }

            // ═══════════════════════════════════════════════════════════════════
            // PHASE 2: Blob Upload (POINT OF NO RETURN)
            // ═══════════════════════════════════════════════════════════════════

            // Pre-generate the attachment ID so it can be embedded in the blob path.
            // This prevents path collisions when the same filename is uploaded twice.
            var attachmentId = Guid.NewGuid();
            var blobPath = GenerateBlobPath(userId, request.TaskId, attachmentId, request.FileName);

            var uploadResult = await _blobStorageService.UploadAsync(_options.ContainerName,
                                                                     blobPath,
                                                                     request.Content,
                                                                     normalizedContentType,
                                                                     cancellationToken);

            if (uploadResult.IsFailed)
            {
                _logger.LogError(
                    "Failed to upload attachment to blob storage for task {TaskId}: {Errors}",
                    request.TaskId,
                    string.Join(", ", uploadResult.Errors.Select(e => e.Message)));

                return Result.Fail(new Error("Attachment.Upload.Failed")
                    .WithMetadata("Message", "Failed to upload attachment to storage."));
            }

            _logger.LogInformation("Attachment blob uploaded: {BlobPath}, Size: {SizeBytes}",
                                   blobPath,
                                   uploadResult.Value.SizeBytes);

            // ═══════════════════════════════════════════════════════════════════
            // PHASE 3: Create Entity and Outbox (in memory, validate all)
            // ═══════════════════════════════════════════════════════════════════

            // DisplayOrder is 1-based: next after existing non-deleted attachments
            var displayOrder = existingCount + 1;

            var attachmentResult = Attachment.Create(id: attachmentId,
                                                     userId: userId,
                                                     taskId: request.TaskId,
                                                     fileName: request.FileName,
                                                     contentType: normalizedContentType,
                                                     sizeBytes: uploadResult.Value.SizeBytes,
                                                     blobPath: blobPath,
                                                     displayOrder: displayOrder,
                                                     utcNow: utcNow);

            if (attachmentResult.IsFailure)
            {
                _logger.LogError(
                    "Failed to create Attachment entity: {Errors}",
                    string.Join(", ", attachmentResult.Errors.Select(e => e.Message)));

                await CleanupBlobAsync(blobPath, cancellationToken);

                return Result.Fail(new Error("Attachment.Create.Failed")
                    .WithMetadata("Message", "Failed to create attachment record."));
            }

            var attachment = attachmentResult.Value!;

            var payload = OutboxPayloadBuilder.BuildAttachmentPayload(attachment, Guid.Empty);

            var outboxResult = OutboxMessage.Create<Attachment, AttachmentEventType>(
                attachment,
                AttachmentEventType.Created,
                payload,
                utcNow);

            if (outboxResult.IsFailure || outboxResult.Value is null)
            {
                _logger.LogError(
                    "Failed to create Attachment outbox message for task {TaskId}",
                    request.TaskId);

                await CleanupBlobAsync(blobPath, cancellationToken);

                return Result.Fail(new Error("Outbox.Attachment.CreateFailed")
                    .WithMetadata("Message", "Failed to create attachment sync event."));
            }

            // ═══════════════════════════════════════════════════════════════════
            // PHASE 4: Persist Everything (atomic)
            // ═══════════════════════════════════════════════════════════════════

            await _attachmentRepository.AddAsync(attachment, cancellationToken);
            await _outboxRepository.AddAsync(outboxResult.Value, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            // ═══════════════════════════════════════════════════════════════════
            // PHASE 5: Generate Download URL (best effort)
            // ═══════════════════════════════════════════════════════════════════

            // URL generation is a read-only operation that can fail transiently.
            // Since the attachment is already successfully created and persisted,
            // we should not fail the entire operation. Instead:
            // - Try to generate URL
            // - If it fails, return success with null URL
            // - Client can fetch the URL later via GET /api/attachments/{id}/download-url

            string? downloadUrl = null;

            var downloadUrlResult = await _blobStorageService.GenerateDownloadUrlAsync(
                _options.ContainerName,
                blobPath,
                _options.DownloadUrlValidity,
                cancellationToken);

            if (downloadUrlResult.IsFailed)
            {
                _logger.LogWarning(
                    "Failed to generate download URL for attachment {AttachmentId}. " +
                    "Attachment was created successfully — client can fetch URL separately. Errors: {Errors}",
                    attachment.Id,
                    string.Join(", ", downloadUrlResult.Errors.Select(e => e.Message)));
            }
            else
            {
                downloadUrl = downloadUrlResult.Value;
            }

            _logger.LogInformation("Attachment {AttachmentId} created for task {TaskId} (DisplayOrder={DisplayOrder})",
                                   attachment.Id,
                                   request.TaskId,
                                   displayOrder);

            return Result.Ok(new UploadAttachmentResultDto
            {
                AttachmentId = attachment.Id,
                TaskId = attachment.TaskId,
                DisplayOrder = attachment.DisplayOrder,
                DownloadUrl = downloadUrl
            });
        }

        /// <summary>
        /// Attempts to delete an uploaded blob after a downstream failure.
        /// Best effort — does not throw on failure.
        /// </summary>
        private async Task CleanupBlobAsync(string blobPath, CancellationToken cancellationToken)
        {
            var deleteResult = await _blobStorageService.DeleteAsync(
                _options.ContainerName, blobPath, cancellationToken);

            if (deleteResult.IsFailed)
            {
                _logger.LogWarning(
                    "Failed to clean up attachment blob after failure: {BlobPath}. Errors: {Errors}",
                    blobPath,
                    string.Join(", ", deleteResult.Errors.Select(e => e.Message)));
            }
        }

        /// <summary>
        /// Generates a hierarchical blob path for the attachment.
        /// Format: {userId}/task-attachments/{taskId}/{attachmentId}/{sanitizedFileName}
        ///
        /// The attachmentId segment prevents collisions when the same filename is
        /// uploaded multiple times to the same task.
        /// </summary>
        private static string GenerateBlobPath(Guid userId, Guid taskId, Guid attachmentId, string fileName)
        {
            var sanitizedFileName = SanitizeFileName(fileName);
            return $"{userId}/task-attachments/{taskId}/{attachmentId}/{sanitizedFileName}";
        }

        /// <summary>
        /// Removes path separators and other problematic characters from a filename.
        /// </summary>
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
