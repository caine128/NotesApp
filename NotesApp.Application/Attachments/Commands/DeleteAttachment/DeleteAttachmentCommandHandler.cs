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
using System.Text.Json;

namespace NotesApp.Application.Attachments.Commands.DeleteAttachment
{
    /// <summary>
    /// Handles the <see cref="DeleteAttachmentCommand"/>:
    /// - Resolves the current internal user id from the token.
    /// - Loads the attachment WITHOUT tracking to prevent auto-persistence on failure.
    /// - Ensures the attachment belongs to the current user.
    /// - Soft-deletes the attachment through the domain method.
    /// - Creates outbox message BEFORE persisting.
    /// - Persists changes only after all validations succeed.
    ///
    /// Blob deletion is deferred to the background orphan-cleanup worker
    /// (same pattern as <c>Asset</c> deletion).
    ///
    /// Returns:
    /// - Result.Ok()                       → HTTP 204 No Content
    /// - Result.Fail (Attachment.NotFound) → HTTP 404 Not Found
    /// - Other failures                    → HTTP 400 / 500 via global mapping.
    /// </summary>
    public sealed class DeleteAttachmentCommandHandler : IRequestHandler<DeleteAttachmentCommand, Result>
    {
        private readonly IAttachmentRepository _attachmentRepository;
        private readonly IOutboxRepository _outboxRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly ICurrentUserService _currentUserService;
        private readonly ISystemClock _clock;
        private readonly ILogger<DeleteAttachmentCommandHandler> _logger;

        public DeleteAttachmentCommandHandler(IAttachmentRepository attachmentRepository,
                                              IOutboxRepository outboxRepository,
                                              IUnitOfWork unitOfWork,
                                              ICurrentUserService currentUserService,
                                              ISystemClock clock,
                                              ILogger<DeleteAttachmentCommandHandler> logger)
        {
            _attachmentRepository = attachmentRepository;
            _outboxRepository = outboxRepository;
            _unitOfWork = unitOfWork;
            _currentUserService = currentUserService;
            _clock = clock;
            _logger = logger;
        }

        public async Task<Result> Handle(DeleteAttachmentCommand command, CancellationToken cancellationToken)
        {
            var currentUserId = await _currentUserService.GetUserIdAsync(cancellationToken);

            // Load the attachment WITHOUT tracking
            var attachment = await _attachmentRepository.GetByIdUntrackedAsync(
                command.AttachmentId, cancellationToken);

            if (attachment is null || attachment.UserId != currentUserId)
            {
                _logger.LogWarning(
                    "DeleteAttachment failed: attachment {AttachmentId} not found for user {UserId}.",
                    command.AttachmentId,
                    currentUserId);

                return Result.Fail(
                    new Error("Attachment not found.")
                        .WithMetadata("ErrorCode", "Attachments.NotFound"));
            }

            var utcNow = _clock.UtcNow;

            // Domain soft delete (entity is NOT tracked, so modifications are in-memory only)
            var deleteResult = attachment.SoftDelete(utcNow);

            if (deleteResult.IsFailure)
            {
                // Entity modified but NOT tracked — won't persist
                return deleteResult.ToResult();
            }

            // Create outbox message BEFORE persisting
            var payload = OutboxPayloadBuilder.BuildAttachmentPayload(attachment, Guid.Empty);

            var outboxResult = OutboxMessage.Create<Attachment, AttachmentEventType>(
                aggregate: attachment,
                eventType: AttachmentEventType.Deleted,
                payload: payload,
                utcNow: utcNow);

            if (outboxResult.IsFailure)
            {
                // Entity modified but NOT tracked — won't persist
                return outboxResult.ToResult();
            }

            // SUCCESS: Now explicitly attach and persist
            attachment.ApplyClientRowVersion(command.RowVersion); // REFACTORED: enable stale-page detection
            _attachmentRepository.Update(attachment);
            await _outboxRepository.AddAsync(outboxResult.Value!, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Attachment {AttachmentId} soft-deleted for user {UserId}.",
                                   attachment.Id,
                                   currentUserId);

            return Result.Ok();
        }
    }
}
