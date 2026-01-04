using FluentResults.Extensions.AspNetCore;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NotesApp.Application.Assets.Commands.UploadAsset;

namespace NotesApp.Api.Controllers
{
    /// <summary>
    /// Handles asset (file/image) upload and download operations.
    /// </summary>
    [Route("api/[controller]")]
    [ApiController]
    [Authorize(Policy = "ApiScope")]
    [Authorize]
    public class AssetsController : ControllerBase
    {
        private readonly ISender _mediator;

        public AssetsController(ISender mediator)
        {
            _mediator = mediator;
        }


        /// <summary>
        /// Uploads a file asset for a block.
        /// The block must already exist with UploadStatus.Pending and the AssetClientId must match.
        /// </summary>
        /// <param name="blockId">ID of the block this asset belongs to.</param>
        /// <param name="assetClientId">Client-generated asset identifier (must match block's AssetClientId).</param>
        /// <param name="file">The file to upload.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Upload result with asset ID and download URL.</returns>
        [HttpPost("{blockId:guid}")]
        [ProducesResponseType(typeof(UploadAssetResultDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [RequestSizeLimit(52_428_800)] // 50 MB
        public async Task<IActionResult> Upload([FromRoute] Guid blockId,
                                                [FromQuery] string assetClientId,
                                                IFormFile file,
                                                CancellationToken cancellationToken)
        {
            if (file is null || file.Length == 0)
            {
                return BadRequest(new { Error = "No file provided or file is empty." });
            }

            if (string.IsNullOrWhiteSpace(assetClientId))
            {
                return BadRequest(new { Error = "AssetClientId is required." });
            }

            using var stream = file.OpenReadStream();

            var command = new UploadAssetCommand
            {
                BlockId = blockId,
                AssetClientId = assetClientId,
                Content = stream,
                FileName = file.FileName,
                ContentType = file.ContentType ?? "application/octet-stream",
                SizeBytes = file.Length
            };

            var result = await _mediator.Send(command, cancellationToken);

            return result.ToActionResult();
        }


        /// <summary>
        /// Alternative endpoint for uploading assets using a streaming approach.
        /// Useful for larger files or when the client needs more control over the upload.
        /// </summary>
        /// <param name="blockId">ID of the block this asset belongs to.</param>
        /// <param name="assetClientId">Client-generated asset identifier.</param>
        /// <param name="fileName">Original filename.</param>
        /// <param name="contentType">MIME type of the content.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Upload result with asset ID and download URL.</returns>
        [HttpPost("{blockId:guid}/stream")]
        [ProducesResponseType(typeof(UploadAssetResultDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [RequestSizeLimit(52_428_800)] // 50 MB
        public async Task<IActionResult> UploadStream([FromRoute] Guid blockId,
                                                      [FromQuery] string assetClientId,
                                                      [FromQuery] string fileName,
                                                      [FromQuery] string? contentType,
                                                      CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(assetClientId))
            {
                return BadRequest(new { Error = "AssetClientId is required." });
            }

            if (string.IsNullOrWhiteSpace(fileName))
            {
                return BadRequest(new { Error = "FileName is required." });
            }

            if (Request.ContentLength is null || Request.ContentLength == 0)
            {
                return BadRequest(new { Error = "No content provided." });
            }

            var command = new UploadAssetCommand
            {
                BlockId = blockId,
                AssetClientId = assetClientId,
                Content = Request.Body,
                FileName = fileName,
                ContentType = contentType ?? Request.ContentType ?? "application/octet-stream",
                SizeBytes = Request.ContentLength.Value
            };

            var result = await _mediator.Send(command, cancellationToken);

            return result.ToActionResult();
        }
    }
}
