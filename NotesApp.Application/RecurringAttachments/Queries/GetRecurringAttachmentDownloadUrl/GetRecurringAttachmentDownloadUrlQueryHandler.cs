using FluentResults;
using MediatR;
using Microsoft.Extensions.Options;
using NotesApp.Application.Abstractions.Persistence;
using NotesApp.Application.Abstractions.Storage;
using NotesApp.Application.Common.Interfaces;
using NotesApp.Application.Configuration;
using System;

namespace NotesApp.Application.RecurringAttachments.Queries.GetRecurringAttachmentDownloadUrl
{
    /// <summary>
    /// Generates a pre-signed download URL for an existing recurring task attachment.
    /// Works for both series template attachments and exception attachment overrides.
    /// Mirrors <c>GetAttachmentDownloadUrlQueryHandler</c>.
    ///
    /// Returns:
    /// - Result.Ok(url)                            → HTTP 200 OK with the URL string
    /// - Result.Fail (RecurringAttachment.NotFound) → HTTP 404 Not Found
    /// - Result.Fail (storage error)               → HTTP 500 via global mapping
    /// </summary>
    // REFACTORED: added for recurring-task-attachments feature
    public sealed class GetRecurringAttachmentDownloadUrlQueryHandler
        : IRequestHandler<GetRecurringAttachmentDownloadUrlQuery, Result<string>>
    {
        private readonly IRecurringTaskAttachmentRepository _attachmentRepository;
        private readonly IBlobStorageService _blobStorageService;
        private readonly ICurrentUserService _currentUserService;
        private readonly AttachmentStorageOptions _options;

        public GetRecurringAttachmentDownloadUrlQueryHandler(
            IRecurringTaskAttachmentRepository attachmentRepository,
            IBlobStorageService blobStorageService,
            ICurrentUserService currentUserService,
            IOptions<AttachmentStorageOptions> options)
        {
            _attachmentRepository = attachmentRepository;
            _blobStorageService = blobStorageService;
            _currentUserService = currentUserService;
            _options = options.Value;
        }

        public async Task<Result<string>> Handle(
            GetRecurringAttachmentDownloadUrlQuery request,
            CancellationToken cancellationToken)
        {
            var userId = await _currentUserService.GetUserIdAsync(cancellationToken);

            var attachment = await _attachmentRepository.GetByIdUntrackedAsync(
                request.AttachmentId, cancellationToken);

            if (attachment is null || attachment.UserId != userId)
            {
                return Result.Fail(new Error("Recurring attachment not found.")
                    .WithMetadata("ErrorCode", "RecurringAttachments.NotFound"));
            }

            var urlResult = await _blobStorageService.GenerateDownloadUrlAsync(
                _options.ContainerName,
                attachment.BlobPath,
                _options.DownloadUrlValidity,
                cancellationToken);

            if (urlResult.IsFailed)
                return Result.Fail(urlResult.Errors);

            return Result.Ok(urlResult.Value);
        }
    }
}
