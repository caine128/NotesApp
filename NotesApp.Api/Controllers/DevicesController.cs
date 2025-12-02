using FluentResults.Extensions.AspNetCore;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NotesApp.Application.Devices.Commands.RegisterDevice;
using NotesApp.Application.Devices.Commands.UnregisterDevice;
using NotesApp.Application.Devices.Models;
using NotesApp.Application.Devices.Queries.GetUserDevices;

namespace NotesApp.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Policy = "ApiScope")]
    [Authorize]
    public sealed class DevicesController : ControllerBase
    {
        private readonly IMediator _mediator;

        public DevicesController(IMediator mediator)
        {
            _mediator = mediator;
        }

        /// <summary>
        /// Registers or updates a device for the current user.
        /// </summary>
        [HttpPost]
        [ProducesResponseType(typeof(UserDeviceDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<ActionResult<UserDeviceDto>> RegisterDevice([FromBody] RegisterDeviceCommand command,
                                                                      CancellationToken cancellationToken)
        {
            var result = await _mediator.Send(command, cancellationToken);

            if (result.IsFailed)
            {
                return result.ToActionResult();
            }

            // Registration is idempotent, so 200 OK is fine.
            return Ok(result.Value);
        }

        /// <summary>
        /// Deactivates a device belonging to the current user.
        /// </summary>
        [HttpDelete("{deviceId:guid}")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> UnregisterDevice(Guid deviceId,
                                                          CancellationToken cancellationToken)
        {
            var command = new UnregisterDeviceCommand { DeviceId = deviceId };
            var result = await _mediator.Send(command, cancellationToken);

            if (result.IsFailed)
            {
                // translate "Device.NotFound" → 404 via profile/handler, otherwise 400.
                return result.ToActionResult();
            }

            return NoContent();
        }

        /// <summary>
        /// Returns all active devices for the current user.
        /// </summary>
        [HttpGet]
        [ProducesResponseType(typeof(IReadOnlyList<UserDeviceDto>), StatusCodes.Status200OK)]
        public async Task<ActionResult<IReadOnlyList<UserDeviceDto>>> GetDevices(CancellationToken cancellationToken)
        {
            var query = new GetUserDevicesQuery();
            var result = await _mediator.Send(query, cancellationToken);

            if (result.IsFailed)
            {
                return result.ToActionResult();
            }

            return Ok(result.Value);
        }
    }
}
