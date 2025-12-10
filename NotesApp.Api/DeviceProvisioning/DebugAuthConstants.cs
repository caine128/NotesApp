namespace NotesApp.Api.DeviceProvisioning
{
    public static class DebugAuthConstants
    {
        /// <summary>
        /// Header used to indicate a debug user in Development.
        /// Must match the header inspected by DebugAuthenticationHandler and Program.cs.
        /// </summary>
        public const string DebugUserHeaderName = "X-Debug-User";
    }
}
