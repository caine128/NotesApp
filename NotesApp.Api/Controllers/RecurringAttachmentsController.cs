using FluentResults.Extensions.AspNetCore;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NotesApp.Application.RecurringAttachments.Commands.DeleteRecurringTaskOccurrenceAttachment;
using NotesApp.Application.RecurringAttachments.Commands.DeleteRecurringTaskSeriesAttachment;
using NotesApp.Application.RecurringAttachments.Commands.UploadRecurringTaskOccurrenceAttachment;
using NotesApp.Application.RecurringAttachments.Commands.UploadRecurringTaskSeriesAttachment;
using NotesApp.Application.RecurringAttachments.Models;
using NotesApp.Application.RecurringAttachments.Queries.GetRecurringAttachmentDownloadUrl;
using System;

namespace NotesApp.Api.Controllers
{
    /// <summary>
    /// Handles file attachment upload, deletion, and download URL generation for recurring tasks.
    ///
    /// Two resource paths:
    /// - /series/{seriesId}   — series template attachments (inherited by all occurrences by default)
    /// - /occurrences/{seriesId}/{occurrenceDate} — occurrence-specific attachments (promotes to exception)
    ///
    /// File bytes always arrive via this REST endpoint; the outbox propagates creation to devices
    /// via the next sync pull. Mobile clients delete attachments via this endpoint or sync push.
    /// </summary>
    // REFACTORED: added for recurring-task-attachments feature
    [Route("api/recurring-attachments")]
    [ApiController]
    [Authorize(Policy = "ApiScope")]
    [Authorize]
    public class RecurringAttachmentsController : ControllerBase
    {
        private readonly ISender _mediator;

        public RecurringAttachmentsController(ISender mediator)
        {
            _mediator = mediator;
        }


        // =====================================================================
        // Series template attachments
        // =====================================================================


        /// <summary>
        /// Uploads a file attachment to a recurring task series template using multipart form data.
        /// All occurrences that have not yet overridden their attachment list will inherit this file.
        /// </summary>
        /// <param name="seriesId">ID of the recurring task series.</param>
        /// <param name="file">The file to upload.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Upload result with attachment ID, display order, and a best-effort download URL.</returns>
        [HttpPost("series/{seriesId:guid}")]
        [ProducesResponseType(typeof(UploadRecurringAttachmentResultDto), StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [RequestSizeLimit(52_428_800)] // 50 MB
        public async Task<IActionResult> UploadToSeries([FromRoute] Guid seriesId,
                                                         IFormFile file,
                                                         CancellationToken cancellationToken)
        {
            using var stream = file.OpenReadStream();

            var command = new UploadRecurringTaskSeriesAttachmentCommand
            {
                SeriesId = seriesId,
                Content = stream,
                FileName = file.FileName,
                ContentType = file.ContentType ?? "application/octet-stream",
                SizeBytes = file.Length
            };

            var result = await _mediator.Send(command, cancellationToken);
            if (result.IsFailed)
                return result.ToActionResult();

            var dto = result.Value;

            return CreatedAtAction(
                nameof(GetSeriesDownloadUrl),
                new { id = dto.AttachmentId },
                dto);
        }


        /// <summary>
        /// Uploads a file attachment to a recurring task series template using a raw byte stream.
        /// </summary>
        /// <param name="seriesId">ID of the recurring task series.</param>
        /// <param name="fileName">Original filename.</param>
        /// <param name="contentType">MIME type of the content.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Upload result with attachment ID, display order, and a best-effort download URL.</returns>
        [HttpPost("series/{seriesId:guid}/stream")]
        [ProducesResponseType(typeof(UploadRecurringAttachmentResultDto), StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [RequestSizeLimit(52_428_800)] // 50 MB
        public async Task<IActionResult> UploadToSeriesStream([FromRoute] Guid seriesId,
                                                               [FromQuery] string fileName,
                                                               [FromQuery] string? contentType,
                                                               CancellationToken cancellationToken)
        {
            if (!Request.ContentLength.HasValue)
                return BadRequest("Content-Length header is required for stream uploads.");

            var command = new UploadRecurringTaskSeriesAttachmentCommand
            {
                SeriesId = seriesId,
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
                nameof(GetSeriesDownloadUrl),
                new { id = dto.AttachmentId },
                dto);
        }


        /// <summary>
        /// Soft-deletes a series template attachment.
        /// Occurrences that have not yet overridden their attachment list will no longer inherit this file.
        /// The blob is cleaned up asynchronously by the background orphan-cleanup worker.
        /// </summary>
        /// <param name="id">ID of the series template attachment to delete.</param>
        /// <param name="command">Delete command body (RowVersion for concurrency protection).</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>204 NoContent on success.</returns>
        [HttpDelete("series/{id:guid}")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        public async Task<IActionResult> DeleteFromSeries([FromRoute] Guid id,
                                                           [FromBody] DeleteRecurringTaskSeriesAttachmentCommand command,
                                                           CancellationToken cancellationToken)
        {
            command.AttachmentId = id;

            var result = await _mediator.Send(command, cancellationToken);
            if (result.IsFailed)
                return result.ToActionResult();

            return NoContent();
        }


        /// <summary>
        /// Generates a pre-signed download URL for a series template attachment.
        /// The URL is valid for a limited time (configured via AttachmentStorage:DownloadUrlValidityMinutes).
        /// </summary>
        /// <param name="id">ID of the attachment.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Pre-signed download URL string.</returns>
        [HttpGet("series/{id:guid}/download-url")]
        [ProducesResponseType(typeof(string), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetSeriesDownloadUrl([FromRoute] Guid id,
                                                               CancellationToken cancellationToken)
        {
            var result = await _mediator.Send(new GetRecurringAttachmentDownloadUrlQuery(id), cancellationToken);
            return result.ToActionResult();
        }


        // =====================================================================
        // Occurrence-specific attachments
        // =====================================================================


        /// <summary>
        /// Uploads a file to a specific recurring task occurrence using multipart form data.
        /// If the occurrence has no prior attachment override, it is promoted to a
        /// RecurringTaskException and all current series template attachments are copied first.
        /// </summary>
        /// <param name="seriesId">ID of the recurring task series.</param>
        /// <param name="occurrenceDate">Date of the specific occurrence (yyyy-MM-dd).</param>
        /// <param name="file">The file to upload.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Upload result with attachment ID, display order, and a best-effort download URL.</returns>
        [HttpPost("occurrences/{seriesId:guid}/{occurrenceDate}")]
        [ProducesResponseType(typeof(UploadRecurringAttachmentResultDto), StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [RequestSizeLimit(52_428_800)] // 50 MB
        public async Task<IActionResult> UploadToOccurrence([FromRoute] Guid seriesId,
                                                             [FromRoute] DateOnly occurrenceDate,
                                                             IFormFile file,
                                                             CancellationToken cancellationToken)
        {
            using var stream = file.OpenReadStream();

            var command = new UploadRecurringTaskOccurrenceAttachmentCommand
            {
                SeriesId = seriesId,
                OccurrenceDate = occurrenceDate,
                Content = stream,
                FileName = file.FileName,
                ContentType = file.ContentType ?? "application/octet-stream",
                SizeBytes = file.Length
            };

            var result = await _mediator.Send(command, cancellationToken);
            if (result.IsFailed)
                return result.ToActionResult();

            var dto = result.Value;

            return CreatedAtAction(
                nameof(GetOccurrenceDownloadUrl),
                new { id = dto.AttachmentId },
                dto);
        }


        /// <summary>
        /// Uploads a file to a specific recurring task occurrence using a raw byte stream.
        /// </summary>
        /// <param name="seriesId">ID of the recurring task series.</param>
        /// <param name="occurrenceDate">Date of the specific occurrence (yyyy-MM-dd).</param>
        /// <param name="fileName">Original filename.</param>
        /// <param name="contentType">MIME type of the content.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Upload result with attachment ID, display order, and a best-effort download URL.</returns>
        [HttpPost("occurrences/{seriesId:guid}/{occurrenceDate}/stream")]
        [ProducesResponseType(typeof(UploadRecurringAttachmentResultDto), StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [RequestSizeLimit(52_428_800)] // 50 MB
        public async Task<IActionResult> UploadToOccurrenceStream([FromRoute] Guid seriesId,
                                                                   [FromRoute] DateOnly occurrenceDate,
                                                                   [FromQuery] string fileName,
                                                                   [FromQuery] string? contentType,
                                                                   CancellationToken cancellationToken)
        {
            if (!Request.ContentLength.HasValue)
                return BadRequest("Content-Length header is required for stream uploads.");

            var command = new UploadRecurringTaskOccurrenceAttachmentCommand
            {
                SeriesId = seriesId,
                OccurrenceDate = occurrenceDate,
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
                nameof(GetOccurrenceDownloadUrl),
                new { id = dto.AttachmentId },
                dto);
        }


        /// <summary>
        /// Deletes an attachment from a specific recurring task occurrence.
        /// If the attachment is a series template attachment, the occurrence is promoted to a
        /// RecurringTaskException with all series attachments minus the deleted one copied across.
        /// If the attachment is already exception-specific, it is simply soft-deleted.
        /// </summary>
        /// <param name="seriesId">ID of the recurring task series.</param>
        /// <param name="occurrenceDate">Date of the specific occurrence (yyyy-MM-dd).</param>
        /// <param name="id">ID of the attachment to delete.</param>
        /// <param name="command">Delete command body (RowVersion for concurrency protection).</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>204 NoContent on success.</returns>
        [HttpDelete("occurrences/{seriesId:guid}/{occurrenceDate}/{id:guid}")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        public async Task<IActionResult> DeleteFromOccurrence([FromRoute] Guid seriesId,
                                                               [FromRoute] DateOnly occurrenceDate,
                                                               [FromRoute] Guid id,
                                                               [FromBody] DeleteRecurringTaskOccurrenceAttachmentCommand command,
                                                               CancellationToken cancellationToken)
        {
            command.SeriesId = seriesId;
            command.OccurrenceDate = occurrenceDate;
            command.AttachmentId = id;

            var result = await _mediator.Send(command, cancellationToken);
            if (result.IsFailed)
                return result.ToActionResult();

            return NoContent();
        }


        /// <summary>
        /// Generates a pre-signed download URL for an exception attachment override.
        /// The URL is valid for a limited time (configured via AttachmentStorage:DownloadUrlValidityMinutes).
        /// </summary>
        /// <param name="id">ID of the attachment.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Pre-signed download URL string.</returns>
        [HttpGet("occurrences/{id:guid}/download-url")]
        [ProducesResponseType(typeof(string), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetOccurrenceDownloadUrl([FromRoute] Guid id,
                                                                   CancellationToken cancellationToken)
        {
            var result = await _mediator.Send(new GetRecurringAttachmentDownloadUrlQuery(id), cancellationToken);
            return result.ToActionResult();
        }
    }
}
