using FluentResults;
using MediatR;
using Microsoft.Extensions.Logging;
using NotesApp.Application.Abstractions.Persistence;
using NotesApp.Application.Common;
using NotesApp.Application.Common.Interfaces;
using NotesApp.Domain.Common;
using NotesApp.Domain.Entities;
using System;

namespace NotesApp.Application.RecurringAttachments.Commands.DeleteRecurringTaskSeriesAttachment
{
    /// <summary>
    /// Handles soft-deletion of a recurring task series template attachment.
    /// Mirrors <c>DeleteAttachmentCommandHandler</c>.
    ///
    /// Rejects requests targeting exception-scoped attachments — use the
    /// <c>DeleteRecurringTaskOccurrenceAttachmentCommand</c> endpoint for those.
    ///
    /// Returns:
    /// - Result.Ok()                            → HTTP 204 No Content
    /// - Result.Fail (RecurringAttachment.NotFound) → HTTP 404 Not Found
    /// </summary>
    // REFACTORED: added for recurring-task-attachments feature
    public sealed class DeleteRecurringTaskSeriesAttachmentCommandHandler
        : IRequestHandler<DeleteRecurringTaskSeriesAttachmentCommand, Result>
    {
        private readonly IRecurringTaskAttachmentRepository _attachmentRepository;
        private readonly IOutboxRepository _outboxRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly ICurrentUserService _currentUserService;
        private readonly ISystemClock _clock;
        private readonly ILogger<DeleteRecurringTaskSeriesAttachmentCommandHandler> _logger;

        public DeleteRecurringTaskSeriesAttachmentCommandHandler(
            IRecurringTaskAttachmentRepository attachmentRepository,
            IOutboxRepository outboxRepository,
            IUnitOfWork unitOfWork,
            ICurrentUserService currentUserService,
            ISystemClock clock,
            ILogger<DeleteRecurringTaskSeriesAttachmentCommandHandler> logger)
        {
            _attachmentRepository = attachmentRepository;
            _outboxRepository = outboxRepository;
            _unitOfWork = unitOfWork;
            _currentUserService = currentUserService;
            _clock = clock;
            _logger = logger;
        }

        public async Task<Result> Handle(
            DeleteRecurringTaskSeriesAttachmentCommand command,
            CancellationToken cancellationToken)
        {
            var currentUserId = await _currentUserService.GetUserIdAsync(cancellationToken);

            var attachment = await _attachmentRepository.GetByIdUntrackedAsync(
                command.AttachmentId, cancellationToken);

            if (attachment is null || attachment.UserId != currentUserId || !attachment.SeriesId.HasValue)
            {
                _logger.LogWarning(
                    "DeleteRecurringSeriesAttachment failed: attachment {AttachmentId} not found for user {UserId}.",
                    command.AttachmentId, currentUserId);

                return Result.Fail(
                    new Error("Recurring attachment not found.")
                        .WithMetadata("ErrorCode", "RecurringAttachments.NotFound"));
            }

            var utcNow = _clock.UtcNow;

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
                "Recurring series attachment {AttachmentId} soft-deleted for user {UserId}.",
                attachment.Id, currentUserId);

            return Result.Ok();
        }
    }
}
