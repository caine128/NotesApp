using FluentAssertions;
using NotesApp.Api.IntegrationTests.Infrastructure.Hosting;
using NotesApp.Api.IntegrationTests.Infrastructure.Http;
using System.Net;
using System.Net.Http.Json;
using System.Text;

namespace NotesApp.Api.IntegrationTests.CrossCutting
{
    /// <summary>
    /// Parameterized tests asserting cross-cutting HTTP invariants over the API surface:
    /// - Mutating endpoints reject anonymous requests with 401.
    /// - Routes typed as Guid reject malformed values with 404 (route-constraint failure).
    /// - JSON-body endpoints reject text/plain payloads with 415 Unsupported Media Type.
    ///
    /// Per-endpoint ownership/404 semantics are exercised exhaustively in each
    /// feature's own *EndpointsTests file; this suite locks in the cross-cutting shape only.
    /// </summary>
    public sealed class AuthAndOwnershipTests : IClassFixture<NotesAppApiFactory>
    {
        private readonly NotesAppApiFactory _factory;

        public AuthAndOwnershipTests(NotesAppApiFactory factory)
        {
            _factory = factory;
        }

        public static IEnumerable<object[]> MutatingEndpoints()
        {
            // Method, Path
            yield return new object[] { "POST",   "/api/tasks" };
            yield return new object[] { "PUT",    $"/api/tasks/{Guid.NewGuid()}" };
            yield return new object[] { "DELETE", $"/api/tasks/{Guid.NewGuid()}" };

            yield return new object[] { "POST",   "/api/categories" };
            yield return new object[] { "PUT",    $"/api/categories/{Guid.NewGuid()}" };
            yield return new object[] { "DELETE", $"/api/categories/{Guid.NewGuid()}" };

            yield return new object[] { "POST",   $"/api/tasks/{Guid.NewGuid()}/subtasks" };
            yield return new object[] { "PUT",    $"/api/tasks/{Guid.NewGuid()}/subtasks/{Guid.NewGuid()}" };
            yield return new object[] { "DELETE", $"/api/tasks/{Guid.NewGuid()}/subtasks/{Guid.NewGuid()}" };

            yield return new object[] { "DELETE", $"/api/attachments/{Guid.NewGuid()}" };

            yield return new object[] { "PUT",    $"/api/tasks/{Guid.NewGuid()}/recurring" };
            yield return new object[] { "DELETE", $"/api/tasks/{Guid.NewGuid()}/recurring" };
            yield return new object[] { "PUT",    "/api/tasks/virtual-occurrences" };
            yield return new object[] { "DELETE", "/api/tasks/virtual-occurrences" };
            yield return new object[] { "PUT",    "/api/tasks/recurring-occurrences/subtasks" };

            yield return new object[] { "DELETE", $"/api/recurring-attachments/series/{Guid.NewGuid()}" };
        }

        // -----------------------------------------------------------------
        // Auth: anonymous mutating requests are rejected with 401.
        // -----------------------------------------------------------------

        [Theory]
        [MemberData(nameof(MutatingEndpoints))]
        public async Task Anonymous_request_to_mutating_endpoint_returns_401(string method, string path)
        {
            var anon = _factory.CreateClient();
            var request = new HttpRequestMessage(new HttpMethod(method), path);

            // Empty JSON body so 415-due-to-missing-content-type doesn't preempt the 401 challenge.
            if (method != "DELETE")
                request.Content = JsonContent.Create(new { });

            var response = await anon.SendAsync(request);

            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        // -----------------------------------------------------------------
        // Route-constraint: malformed GUID in a Guid-typed segment yields 404
        // (ASP.NET routing fails to match the route).
        // -----------------------------------------------------------------

        [Theory]
        [InlineData("GET",    "/api/tasks/not-a-guid")]
        [InlineData("PUT",    "/api/tasks/not-a-guid")]
        [InlineData("DELETE", "/api/tasks/not-a-guid")]
        [InlineData("PUT",    "/api/categories/not-a-guid")]
        [InlineData("DELETE", "/api/categories/not-a-guid")]
        [InlineData("DELETE", "/api/attachments/not-a-guid")]
        public async Task Malformed_guid_route_value_returns_404(string method, string path)
        {
            var client = _factory.CreateClientAsUser(Guid.NewGuid());
            var request = new HttpRequestMessage(new HttpMethod(method), path);

            if (method is "PUT" or "POST")
                request.Content = JsonContent.Create(new { });

            var response = await client.SendAsync(request);

            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }

        // -----------------------------------------------------------------
        // Content negotiation: JSON-body endpoints reject text/plain with 415.
        // -----------------------------------------------------------------

        [Theory]
        [InlineData("POST", "/api/tasks")]
        [InlineData("POST", "/api/categories")]
        public async Task Json_body_endpoint_with_text_plain_body_returns_415(string method, string path)
        {
            var client = _factory.CreateClientAsUser(Guid.NewGuid());

            var request = new HttpRequestMessage(new HttpMethod(method), path)
            {
                Content = new StringContent("not json", Encoding.UTF8, "text/plain")
            };

            var response = await client.SendAsync(request);

            response.StatusCode.Should().Be(HttpStatusCode.UnsupportedMediaType);
        }
    }
}
