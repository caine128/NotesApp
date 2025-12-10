using MediatR;
using NotesApp.Application.Devices.Commands.RegisterDevice;
using NotesApp.Domain.Users;

namespace NotesApp.Api.DeviceProvisioning
{
    internal sealed class DevDeviceDebugProvisioningService : IDeviceDebugProvisioningService
    {
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly ISender _mediator;
        private readonly ILogger<DevDeviceDebugProvisioningService> _logger;

        public DevDeviceDebugProvisioningService(IHttpContextAccessor httpContextAccessor,
                                                 ISender mediator,
                                                 ILogger<DevDeviceDebugProvisioningService> logger)
        {
            _httpContextAccessor = httpContextAccessor;
            _mediator = mediator;
            _logger = logger;
        }

        public async Task<Guid?> EnsureDeviceIdAsync(Guid? deviceId, CancellationToken cancellationToken)
        {
            // If client already provided a non-empty device id, respect it.
            if (deviceId.HasValue && deviceId.Value != Guid.Empty)
            {
                return deviceId;
            }

            var httpContext = _httpContextAccessor.HttpContext;
            if (httpContext is null)
            {
                return deviceId;
            }

            if (!httpContext.Request.Headers.TryGetValue(DebugAuthConstants.DebugUserHeaderName, out var headerValues))
            {
                // Not a debug request => do nothing
                return deviceId;
            }

            var debugUser = headerValues.ToString();
            if (string.IsNullOrWhiteSpace(debugUser))
            {
                return deviceId;
            }

            // Build a stable debug token so repeated calls reuse the same device.
            var token = $"debug:{debugUser}";
            if (token.Length > 512)
            {
                token = token.Substring(0, 512);
            }

            var command = new RegisterDeviceCommand
            {
                DeviceToken = token,
                Platform = DevicePlatform.Android, // arbitrary but valid
                DeviceName = $"Debug device ({debugUser})"
            };

            var result = await _mediator.Send(command, cancellationToken);

            if (result.IsFailed)
            {
                _logger.LogWarning(
                    "Dev auto device registration failed for X-Debug-User '{DebugUser}'. Errors: {Errors}",
                    debugUser,
                    result.Errors);

                // Let sync fail in the normal way (validation / Device.NotFound etc.)
                return deviceId;
            }

            return result.Value.Id;
        }
    }
}
