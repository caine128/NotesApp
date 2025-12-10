namespace NotesApp.Api.DeviceProvisioning
{
    internal sealed class NoOpDeviceDebugProvisioningService : IDeviceDebugProvisioningService
    {
        public Task<Guid?> EnsureDeviceIdAsync(Guid? deviceId, CancellationToken cancellationToken)
        {
            // In non-dev environments we don't do any magic; just pass through.
            return Task.FromResult(deviceId);
        }
    }
}
