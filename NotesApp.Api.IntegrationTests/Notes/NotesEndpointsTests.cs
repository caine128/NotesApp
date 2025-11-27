using FluentAssertions;
using NotesApp.Api.IntegrationTests.Infrastructure.Hosting;
using NotesApp.Application.Notes;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http.Json;
using System.Text;

namespace NotesApp.Api.IntegrationTests.Notes
{
    /// <summary>
    /// Integration tests for the Notes API endpoints.
    ///
    /// These tests:
    /// - Start the real NotesApp.Api application in a test host.
    /// - Authenticate using the TestAuthHandler (no real Entra calls).
    /// - Exercise controllers, MediatR, EF Core, and the database together.
    /// </summary>
    public sealed class NotesEndpointsTests : IClassFixture<NotesAppApiFactory>
    {
        private readonly NotesAppApiFactory _factory;

        public NotesEndpointsTests(NotesAppApiFactory factory)
        {
            _factory = factory;
        }

        [Fact]
        public async Task Create_and_get_notes_for_day_roundtrip_succeeds()
        {
            // Arrange: simulate a specific "current user" via header
            var userId = Guid.NewGuid();
            var client = _factory.CreateClientAsUser(userId);

            var date = new DateOnly(2025, 2, 20);

            var createPayload = new
            {
                date = date,
                title = "Integration test note",
                content = "This is an integration test note."
            };

            // Act 1: create the note
            var createResponse = await client.PostAsJsonAsync("api/notes", createPayload);

            // Assert 1: creation succeeded and returned a NoteDto
            createResponse.StatusCode.Should().Be(HttpStatusCode.OK);

            var createdNote = await createResponse.Content.ReadFromJsonAsync<NoteDto>();
            createdNote.Should().NotBeNull();

            createdNote!.Title.Should().Be("Integration test note");
            createdNote.Content.Should().Be("This is an integration test note.");
            createdNote.Date.Should().Be(date);

            // UserId is the *internal* user id created by CurrentUserService/User.Create.
            // It should be a valid, non-empty GUID, but not necessarily equal to the external "sub".
            createdNote.UserId.Should().NotBe(Guid.Empty);

            // Act 2: get notes for that day
            var getResponse = await client.GetAsync($"api/notes/day?date={date:yyyy-MM-dd}");

            // Assert 2: request succeeded and our note is in the list
            getResponse.StatusCode.Should().Be(HttpStatusCode.OK);

            var notesForDay = await getResponse.Content.ReadFromJsonAsync<IReadOnlyList<NoteDto>>();

            notesForDay.Should().NotBeNull();
            notesForDay!.Should().Contain(n => n.NoteId == createdNote.NoteId);

            // All notes returned for this user/day should share the same internal UserId:
            var distinctUserIds = notesForDay.Select(n => n.UserId).Distinct().ToList();
            distinctUserIds.Should().HaveCount(1);
            distinctUserIds[0].Should().Be(createdNote.UserId);
        }

        [Fact]
        public async Task Notes_are_isolated_between_different_fake_users()
        {
            // Arrange: two different "external" users
            var userA = Guid.NewGuid();
            var userB = Guid.NewGuid();

            var clientA = _factory.CreateClientAsUser(userA);
            var clientB = _factory.CreateClientAsUser(userB);

            var date = new DateOnly(2025, 2, 21);

            var createPayload = new
            {
                date = date,
                title = "User A's note",
                content = "Note owned by user A"
            };

            // Act: user A creates a note
            var createResponse = await clientA.PostAsJsonAsync("api/notes", createPayload);
            createResponse.EnsureSuccessStatusCode();

            // Sanity: user A can see their note
            var getResponseForA = await clientA.GetAsync($"api/notes/day?date={date:yyyy-MM-dd}");
            getResponseForA.EnsureSuccessStatusCode();

            var notesForUserA =
                await getResponseForA.Content.ReadFromJsonAsync<IReadOnlyList<NoteDto>>();

            notesForUserA.Should().NotBeNull();
            notesForUserA!.Should().NotBeEmpty();

            // Act: user B asks for notes on the same day
            var getResponseForB = await clientB.GetAsync($"api/notes/day?date={date:yyyy-MM-dd}");
            getResponseForB.EnsureSuccessStatusCode();

            var notesForUserB =
                await getResponseForB.Content.ReadFromJsonAsync<IReadOnlyList<NoteDto>>();

            // Assert: user B should not see user A's notes
            notesForUserB.Should().NotBeNull();
            notesForUserB!.Should().BeEmpty();
        }
    }
}
