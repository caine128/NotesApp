namespace NotesApp.Api.DeviceProvisioning
{
    /// <summary>
    /// Dev-only helper that can auto-provision devices for debug users.
    /// In production, the implementation is a no-op and simply returns the original device id.
    /// </summary>
    public interface IDeviceDebugProvisioningService
    {
        /// <summary>
        /// Returns an effective device id for the current request.
        /// Implementations may return the original id unchanged, or auto-provision a device
        /// and return its id (e.g. in Development + X-Debug-User).
        /// </summary>
        Task<Guid?> EnsureDeviceIdAsync(Guid? deviceId, CancellationToken cancellationToken);
    }
}
