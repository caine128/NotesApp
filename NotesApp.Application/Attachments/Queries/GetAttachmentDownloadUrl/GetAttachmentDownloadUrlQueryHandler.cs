using FluentResults;
using MediatR;
using Microsoft.Extensions.Options;
using NotesApp.Application.Abstractions.Persistence;
using NotesApp.Application.Abstractions.Storage;
using NotesApp.Application.Common.Interfaces;
using NotesApp.Application.Configuration;
using System;

namespace NotesApp.Application.Attachments.Queries.GetAttachmentDownloadUrl
{
    /// <summary>
    /// Generates a pre-signed download URL for an existing task attachment.
    ///
    /// Loads the attachment WITHOUT tracking (read-only operation).
    /// Verifies that the attachment belongs to the current user.
    ///
    /// Returns:
    /// - Result.Ok(url)                    → HTTP 200 OK with the URL string
    /// - Result.Fail (Attachment.NotFound) → HTTP 404 Not Found
    /// - Result.Fail (storage error)       → HTTP 500 via global mapping
    /// </summary>
    public sealed class GetAttachmentDownloadUrlQueryHandler
        : IRequestHandler<GetAttachmentDownloadUrlQuery, Result<string>>
    {
        private readonly IAttachmentRepository _attachmentRepository;
        private readonly IBlobStorageService _blobStorageService;
        private readonly ICurrentUserService _currentUserService;
        private readonly AttachmentStorageOptions _options;

        public GetAttachmentDownloadUrlQueryHandler(IAttachmentRepository attachmentRepository,
                                                    IBlobStorageService blobStorageService,
                                                    ICurrentUserService currentUserService,
                                                    IOptions<AttachmentStorageOptions> options)
        {
            _attachmentRepository = attachmentRepository;
            _blobStorageService = blobStorageService;
            _currentUserService = currentUserService;
            _options = options.Value;
        }

        public async Task<Result<string>> Handle(GetAttachmentDownloadUrlQuery request,
                                                  CancellationToken cancellationToken)
        {
            var userId = await _currentUserService.GetUserIdAsync(cancellationToken);

            // Load WITHOUT tracking — this is a read-only query
            var attachment = await _attachmentRepository.GetByIdUntrackedAsync(
                request.AttachmentId, cancellationToken);

            if (attachment is null || attachment.UserId != userId)
            {
                return Result.Fail(new Error("Attachment not found.")
                    .WithMetadata("ErrorCode", "Attachments.NotFound"));
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
