using FluentResults.Extensions.AspNetCore;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NotesApp.Api.DeviceProvisioning;
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
        private readonly IDeviceDebugProvisioningService _debugDeviceProvisioningService;

        public SyncController(ISender mediator, IDeviceDebugProvisioningService debugDeviceProvisioningService)
        {
            _mediator = mediator;
            _debugDeviceProvisioningService = debugDeviceProvisioningService;
        }

        /// <summary>
        /// Sequence-based sync pull. Returns ordered <see cref="SyncPullDto.Changes"/> with
        /// <c>Sequence &gt; afterSequence</c>, paginated by <paramref name="limit"/>. Replaces the
        /// legacy <c>GET /api/sync/changes</c> timestamp pull.
        /// </summary>
        /// <param name="afterSequence">Resume cursor; pass 0 for "from the start of available history".</param>
        /// <param name="deviceId">Optional device id; advances the device's LastAckedSyncSequence on success.</param>
        /// <param name="limit">Optional page size; defaults to 500. Max 1000.</param>
        [HttpGet("pull")]
        [ProducesResponseType(typeof(SyncPullDto), StatusCodes.Status200OK)]
        public async Task<IActionResult> Pull([FromQuery] long afterSequence,
                                              [FromQuery] Guid? deviceId,
                                              [FromQuery] int? limit,
                                              CancellationToken cancellationToken)
        {
            // In Dev + X-Debug-User, this may auto-provision a device.
            var effectiveDeviceId = await _debugDeviceProvisioningService
                .EnsureDeviceIdAsync(deviceId, cancellationToken);

            var query = new GetSyncPullQuery(afterSequence, effectiveDeviceId, limit);
            var result = await _mediator.Send(query, cancellationToken);

            return result.ToActionResult();
        }

        /// <summary>
        /// Bootstrap snapshot for new or re-bootstrapping devices. Returns the current state of
        /// every non-deleted entity for the user, plus the <c>BootstrapSequence</c> the client
        /// should use as <c>afterSequence</c> for subsequent <c>GET /api/sync/pull</c> calls.
        /// </summary>
        [HttpGet("snapshot")]
        [ProducesResponseType(typeof(SyncSnapshotDto), StatusCodes.Status200OK)]
        public async Task<IActionResult> Snapshot([FromQuery] Guid? deviceId, CancellationToken cancellationToken)
        {
            var effectiveDeviceId = await _debugDeviceProvisioningService
                .EnsureDeviceIdAsync(deviceId, cancellationToken);

            var query = new GetSyncSnapshotQuery(effectiveDeviceId);
            var result = await _mediator.Send(query, cancellationToken);

            return result.ToActionResult();
        }

        /// <summary>
        /// Applies client-side changes (tasks, notes, and blocks) to the server for the current user.
        /// Conflicts (version mismatch, not found, etc.) are returned in the payload and do not
        /// cause the request to fail at HTTP level.
        /// </summary>
        [HttpPost("push")]
        [ProducesResponseType(typeof(SyncPushResultDto), StatusCodes.Status200OK)]
        public async Task<IActionResult> Push([FromBody] SyncPushCommandPayloadDto payload,
                                              CancellationToken cancellationToken)
        {
            // In Dev + X-Debug-User, this may auto-provision a device.
            var effectiveDeviceId = await _debugDeviceProvisioningService
                .EnsureDeviceIdAsync(payload.DeviceId, cancellationToken);

            var command = new SyncPushCommand
            {
                DeviceId = effectiveDeviceId ?? payload.DeviceId,
                ClientSyncTimestampUtc = payload.ClientSyncTimestampUtc,
                Tasks = payload.Tasks,
                Notes = payload.Notes,
                Blocks = payload.Blocks,
                Categories = payload.Categories,           // REFACTORED: added category push support
                Subtasks = payload.Subtasks,               // REFACTORED: added subtask push support
                Attachments = payload.Attachments,         // REFACTORED: added attachment push support
                // REFACTORED: added recurring-task push support for recurring-tasks feature
                RecurringRoots = payload.RecurringRoots,
                RecurringSeries = payload.RecurringSeries,
                RecurringSeriesSubtasks = payload.RecurringSeriesSubtasks,
                RecurringExceptions = payload.RecurringExceptions,
                RecurringAttachments = payload.RecurringAttachments,  // REFACTORED: added recurring-attachment push support
            };

            var result = await _mediator.Send(command, cancellationToken);

            return result.ToActionResult();
        }

        /// <summary>
        /// Resolves previously reported sync conflicts for tasks and notes.
        /// </summary>
        [HttpPost("resolve-conflicts")]
        [ProducesResponseType(typeof(ResolveSyncConflictsResultDto), StatusCodes.Status200OK)]
        public async Task<IActionResult> ResolveConflicts([FromBody] ResolveSyncConflictsRequestDto request,
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

