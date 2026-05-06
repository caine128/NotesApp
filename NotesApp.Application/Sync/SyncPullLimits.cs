namespace NotesApp.Application.Sync
{
    /// <summary>
    /// Pagination constants for the sequence-based sync pull endpoint.
    /// Distinct from <c>SyncLimits</c>, which holds push-related constants.
    /// </summary>
    internal static class SyncPullLimits
    {
        public const int MinPullLimit = 1;
        public const int DefaultPullLimit = 500;
        public const int MaxPullLimit = 1000;
    }
}
