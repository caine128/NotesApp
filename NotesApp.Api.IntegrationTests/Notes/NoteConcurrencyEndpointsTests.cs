using FluentAssertions;
using NotesApp.Api.IntegrationTests.Infrastructure.Hosting;
using NotesApp.Api.IntegrationTests.Infrastructure.Http;
using NotesApp.Application.Notes.Models;
using System;
using System.Net;
using System.Net.Http.Json;

namespace NotesApp.Api.IntegrationTests.Notes
{
    /// <summary>
    /// Integration tests verifying that stale RowVersion values on PUT/DELETE for notes
    /// are rejected with 409 Conflict (web optimistic concurrency protection).
    /// </summary>
    public sealed class NoteConcurrencyEndpointsTests : IClassFixture<NotesAppApiFactory>
    {
        private readonly NotesAppApiFactory _factory;

        public NoteConcurrencyEndpointsTests(NotesAppApiFactory factory)
        {
            _factory = factory;
        }

        // -----------------------------------------------------------------------
        // UpdateNote (PUT)
        // -----------------------------------------------------------------------

        [Fact]
        public async Task UpdateNote_WithCorrectRowVersion_Succeeds()
        {
            // Arrange
            var client = _factory.CreateClientAsUser(Guid.NewGuid());
            var created = await CreateNoteAsync(client);

            var updatePayload = new
            {
                Date = created.Date,
                Title = "Updated title",
                RowVersion = created.RowVersion // fresh RowVersion
            };

            // Act
            var response = await client.PutAsJsonAsync($"/api/notes/{created.NoteId}", updatePayload);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        [Fact]
        public async Task UpdateNote_WithStaleRowVersion_Returns409()
        {
            // Arrange: create then update once to advance RowVersion
            var client = _factory.CreateClientAsUser(Guid.NewGuid());
            var created = await CreateNoteAsync(client);
            var staleRowVersion = created.RowVersion;

            // First update — consumes the RowVersion
            var firstUpdate = await client.PutAsJsonAsync($"/api/notes/{created.NoteId}", new
            {
                Date = created.Date,
                Title = "First update",
                RowVersion = staleRowVersion
            });
            firstUpdate.EnsureSuccessStatusCode();

            // Act: second update with the now-stale RowVersion
            var response = await client.PutAsJsonAsync($"/api/notes/{created.NoteId}", new
            {
                Date = created.Date,
                Title = "Second update (stale)",
                RowVersion = staleRowVersion
            });

            // Assert: 409 Conflict
            response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        }

        // -----------------------------------------------------------------------
        // DeleteNote (DELETE)
        // -----------------------------------------------------------------------

        [Fact]
        public async Task DeleteNote_WithCorrectRowVersion_Succeeds()
        {
            // Arrange
            var client = _factory.CreateClientAsUser(Guid.NewGuid());
            var created = await CreateNoteAsync(client);

            // Act
            var response = await client.DeleteAsJsonAsync(
                $"/api/notes/{created.NoteId}",
                new { RowVersion = created.RowVersion });

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        [Fact]
        public async Task DeleteNote_WithStaleRowVersion_Returns409()
        {
            // Arrange: create, update (advances RowVersion), then delete with original RowVersion
            var client = _factory.CreateClientAsUser(Guid.NewGuid());
            var created = await CreateNoteAsync(client);
            var staleRowVersion = created.RowVersion;

            // Update to advance the RowVersion
            var updateResponse = await client.PutAsJsonAsync($"/api/notes/{created.NoteId}", new
            {
                Date = created.Date,
                Title = "Updated title",
                RowVersion = staleRowVersion
            });
            updateResponse.EnsureSuccessStatusCode();

            // Act: delete with stale RowVersion
            var response = await client.DeleteAsJsonAsync(
                $"/api/notes/{created.NoteId}",
                new { RowVersion = staleRowVersion });

            // Assert: 409 Conflict
            response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        }

        // -----------------------------------------------------------------------
        // Helpers
        // -----------------------------------------------------------------------

        private static async Task<NoteDetailDto> CreateNoteAsync(HttpClient client)
        {
            var payload = new
            {
                Date = new DateOnly(2025, 11, 10),
                Title = "Concurrency test note"
            };

            var response = await client.PostAsJsonAsync("/api/notes", payload);
            response.EnsureSuccessStatusCode();

            var dto = await response.Content.ReadFromJsonAsync<NoteDetailDto>();
            dto.Should().NotBeNull();
            return dto!;
        }
    }
}
