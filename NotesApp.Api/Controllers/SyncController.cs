using FluentResults.Extensions.AspNetCore;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NotesApp.Application.Sync.Commands.ResolveConflicts;
using NotesApp.Application.Sync.Commands.SyncPush;
using NotesApp.Application.Sync.Models;
using NotesApp.Application.Sync.Queries;

namespace NotesApp.Api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize(Policy = "ApiScope")]
    [Authorize]
    public class SyncController : ControllerBase
    {
        private readonly ISender _mediator;

        public SyncController(ISender mediator)
        {
            _mediator = mediator;
        }

        /// <summary>
        /// Returns all changes (tasks and notes) for the current user since the given timestamp.
        /// If <paramref name="sinceUtc"/> is null, this performs an initial sync and returns all
        /// non-deleted entities for the user.
        /// </summary>
        /// <param name="sinceUtc">
        /// Optional timestamp (UTC). Only entities with UpdatedAtUtc greater than this value
        /// are returned. When omitted, this acts as an initial sync.
        /// </param>
        /// <param name="deviceId">
        /// Optional id of the requesting device. Currently not used on the server side,
        /// but reserved for future device-specific optimisations.
        /// </param>
        [HttpGet("changes")]
        [ProducesResponseType(typeof(SyncChangesDto), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetChanges([FromQuery] DateTime? sinceUtc,
                                                    [FromQuery] Guid? deviceId,
                                                    CancellationToken cancellationToken)
        {
            var query = new GetSyncChangesQuery(sinceUtc, deviceId);

            var result = await _mediator.Send(query, cancellationToken);

            // Uses NotesAppResultEndpointProfile behind the scenes
            return result.ToActionResult();
        }

        /// <summary>
        /// Applies client-side changes (tasks and notes) to the server for the current user.
        /// Conflicts (version mismatch, not found, etc.) are returned in the payload and do not
        /// cause the request to fail at HTTP level.
        /// </summary>
        [HttpPost("push")]
        [ProducesResponseType(typeof(SyncPushResultDto), StatusCodes.Status200OK)]
        public async Task<IActionResult> Push(
            [FromBody] SyncPushCommandPayloadDto payload,
            CancellationToken cancellationToken)
        {
            var command = new SyncPushCommand
            {
                DeviceId = payload.DeviceId,
                ClientSyncTimestampUtc = payload.ClientSyncTimestampUtc,
                Tasks = payload.Tasks,
                Notes = payload.Notes
            };

            var result = await _mediator.Send(command, cancellationToken);

            return result.ToActionResult();
        }

        /// <summary>
        /// Resolves previously reported sync conflicts for tasks and notes.
        /// </summary>
        [HttpPost("resolve-conflicts")]
        [ProducesResponseType(typeof(ResolveSyncConflictsResultDto), StatusCodes.Status200OK)]
        public async Task<IActionResult> ResolveConflicts(
            [FromBody] ResolveSyncConflictsRequestDto request,
            CancellationToken cancellationToken)
        {
            var command = new ResolveSyncConflictsCommand
            {
                Request = request
            };

            var result = await _mediator.Send(command, cancellationToken);

            return result.ToActionResult();
        }
    }
}

