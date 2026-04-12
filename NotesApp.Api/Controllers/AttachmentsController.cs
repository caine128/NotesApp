using FluentResults.Extensions.AspNetCore;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NotesApp.Application.Attachments.Commands.DeleteAttachment;
using NotesApp.Application.Attachments.Commands.UploadAttachment;
using NotesApp.Application.Attachments.Models;
using NotesApp.Application.Attachments.Queries.GetAttachmentDownloadUrl;

namespace NotesApp.Api.Controllers
{
    /// <summary>
    /// Handles file attachment upload, deletion, and download URL generation for tasks.
    ///
    /// Attachments are email-style (attached to the task as a whole, not block-embedded).
    /// File bytes always arrive via this REST endpoint; the outbox propagates the creation
    /// to all devices via the next sync pull. Mobile clients delete attachments via either
    /// this endpoint or the sync push endpoint (AttachmentDeleted items).
    /// </summary>
    // REFACTORED: added AttachmentsController for task-attachments feature
    [Route("api/[controller]")]
    [ApiController]
    [Authorize(Policy = "ApiScope")]
    [Authorize]
    public class AttachmentsController : ControllerBase
    {
        private readonly ISender _mediator;

        public AttachmentsController(ISender mediator)
        {
            _mediator = mediator;
        }


        /// <summary>
        /// Uploads a file attachment for a task using multipart form data.
        /// </summary>
        /// <param name="taskId">ID of the task this attachment belongs to.</param>
        /// <param name="file">The file to upload.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Upload result with attachment ID, display order, and a best-effort download URL.</returns>
        [HttpPost("{taskId:guid}")]
        [ProducesResponseType(typeof(UploadAttachmentResultDto), StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [RequestSizeLimit(52_428_800)] // 50 MB
        public async Task<IActionResult> Upload([FromRoute] Guid taskId,
                                                IFormFile file,
                                                CancellationToken cancellationToken)
        {
            // Validation is handled by UploadAttachmentCommandValidator via the MediatR pipeline.
            // Controller only extracts data from IFormFile and builds the command.

            using var stream = file.OpenReadStream();

            var command = new UploadAttachmentCommand
            {
                TaskId = taskId,
                Content = stream,
                FileName = file.FileName,
                ContentType = file.ContentType ?? "application/octet-stream",
                SizeBytes = file.Length
            };

            var result = await _mediator.Send(command, cancellationToken);
            if (result.IsFailed)
                return result.ToActionResult();

            var dto = result.Value;

            // 201 Created + Location header pointing to GET /api/attachments/{id}/download-url
            return CreatedAtAction(
                nameof(GetDownloadUrl),
                new { id = dto.AttachmentId },
                dto);
        }


        /// <summary>
        /// Uploads a file attachment for a task using a raw byte stream.
        /// Useful for larger files or when the client needs more control over the upload.
        /// </summary>
        /// <param name="taskId">ID of the task this attachment belongs to.</param>
        /// <param name="fileName">Original filename.</param>
        /// <param name="contentType">MIME type of the content.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Upload result with attachment ID, display order, and a best-effort download URL.</returns>
        [HttpPost("{taskId:guid}/stream")]
        [ProducesResponseType(typeof(UploadAttachmentResultDto), StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [RequestSizeLimit(52_428_800)] // 50 MB
        public async Task<IActionResult> UploadStream([FromRoute] Guid taskId,
                                                      [FromQuery] string fileName,
                                                      [FromQuery] string? contentType,
                                                      CancellationToken cancellationToken)
        {
            // Validation is handled by UploadAttachmentCommandValidator via the MediatR pipeline.
            // Controller only extracts data from Request and builds the command.

            if (!Request.ContentLength.HasValue)
                return BadRequest("Content-Length header is required for stream uploads.");

            var command = new UploadAttachmentCommand
            {
                TaskId = taskId,
                Content = Request.Body,
                FileName = fileName,
                ContentType = contentType ?? Request.ContentType ?? "application/octet-stream",
                SizeBytes = Request.ContentLength.Value
            };

            var result = await _mediator.Send(command, cancellationToken);
            if (result.IsFailed)
                return result.ToActionResult();

            var dto = result.Value;

            return CreatedAtAction(
                nameof(GetDownloadUrl),
                new { id = dto.AttachmentId },
                dto);
        }


        /// <summary>
        /// Soft-deletes an attachment.
        /// The associated blob is cleaned up asynchronously by the background orphan-cleanup worker.
        /// </summary>
        /// <param name="id">ID of the attachment to delete.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>204 NoContent on success.</returns>
        [HttpDelete("{id:guid}")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> Delete([FromRoute] Guid id,
                                                CancellationToken cancellationToken)
        {
            var result = await _mediator.Send(new DeleteAttachmentCommand { AttachmentId = id }, cancellationToken);
            if (result.IsFailed)
                return result.ToActionResult();

            // Successful soft-delete => 204 NoContent
            return NoContent();
        }


        /// <summary>
        /// Generates a pre-signed download URL for an attachment.
        /// The URL is valid for a limited time (configured via AttachmentStorage:DownloadUrlValidityMinutes).
        /// </summary>
        /// <param name="id">ID of the attachment.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Pre-signed download URL string.</returns>
        [HttpGet("{id:guid}/download-url")]
        [ProducesResponseType(typeof(string), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetDownloadUrl([FromRoute] Guid id,
                                                        CancellationToken cancellationToken)
        {
            var result = await _mediator.Send(new GetAttachmentDownloadUrlQuery(id), cancellationToken);
            return result.ToActionResult();
        }
    }
}
