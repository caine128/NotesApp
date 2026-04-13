using System.Net.Http.Json;

namespace NotesApp.Api.IntegrationTests.Infrastructure.Http
{
    /// <summary>
    /// Extension helpers for HttpClient used in integration tests.
    /// </summary>
    internal static class HttpClientExtensions
    {
        /// <summary>
        /// Sends an HTTP DELETE request with a JSON body.
        /// Required for endpoints that bind RowVersion from the request body.
        /// </summary>
        public static Task<HttpResponseMessage> DeleteAsJsonAsync<T>(
            this HttpClient client,
            string requestUri,
            T value,
            CancellationToken cancellationToken = default)
        {
            var request = new HttpRequestMessage(HttpMethod.Delete, requestUri)
            {
                Content = JsonContent.Create(value)
            };
            return client.SendAsync(request, cancellationToken);
        }

        /// <summary>
        /// A placeholder 8-byte RowVersion for use in tests that target non-existent resources
        /// (where the handler returns 404 before the concurrency check runs).
        /// </summary>
        public static readonly byte[] PlaceholderRowVersion = [1, 0, 0, 0, 0, 0, 0, 0];
    }
}
