using FluentAssertions;
using NotesApp.Api.IntegrationTests.Infrastructure.Hosting;
using NotesApp.Application.Notes;
using NotesApp.Application.Notes.Models;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http.Json;
using System.Text;

namespace NotesApp.Api.IntegrationTests.Notes
{
    /// <summary>
    /// End-to-end tests for Notes endpoints:
    /// - Create / Get detail
    /// - Get summaries for a day
    /// - Get summaries for a date range
    /// - Get overview for a date range
    /// - Update notes
    /// - Delete notes (soft delete + user isolation)
    /// </summary>
    public sealed class NotesEndpointsTests : IClassFixture<NotesAppApiFactory>
    {
        private readonly NotesAppApiFactory _factory;
        private readonly HttpClient _defaultClient;

        public NotesEndpointsTests(NotesAppApiFactory factory)
        {
            _factory = factory;
            _defaultClient = _factory.CreateClientAsDefaultUser();
        }

        #region Create + Get detail + Day summaries

        [Fact]
        public async Task Create__GetById__And_GetNotesForDay_roundtrip_succeeds()
        {
            // Arrange
            var date = new DateOnly(2025, 11, 10);

            var createPayload = new
            {
                Date = date,
                Title = "Daily log",
                Content = "Worked on NotesApp integration tests.",
                Summary = "Short summary",
                Tags = "work,notes"
            };

            // Act 1: Create the note
            var createResponse = await _defaultClient.PostAsJsonAsync("/api/notes", createPayload);

            // Assert 1: Creation succeeded and returns NoteDetailDto (currently 200 OK)
            createResponse.StatusCode.Should().Be(HttpStatusCode.Created);

            var created = await createResponse.Content.ReadFromJsonAsync<NoteDetailDto>();
            created.Should().NotBeNull();
            created!.NoteId.Should().NotBeEmpty();
            created.Title.Should().Be(createPayload.Title);
            created.Content.Should().Be(createPayload.Content);
            created.Date.Should().Be(date);
            created.Summary.Should().Be(createPayload.Summary);
            created.Tags.Should().Be(createPayload.Tags);
            created.CreatedAtUtc.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
            created.UpdatedAtUtc.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));

            var noteId = created.NoteId;

            // Act 2: Get by id
            var getByIdResponse = await _defaultClient.GetAsync($"/api/notes/{noteId}");

            // Assert 2: 200 OK + same detail
            getByIdResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            var detail = await getByIdResponse.Content.ReadFromJsonAsync<NoteDetailDto>();

            detail.Should().NotBeNull();
            detail!.NoteId.Should().Be(noteId);
            detail.Title.Should().Be(createPayload.Title);
            detail.Content.Should().Be(createPayload.Content);
            detail.Date.Should().Be(date);
            detail.Summary.Should().Be(createPayload.Summary);
            detail.Tags.Should().Be(createPayload.Tags);

            // Act 3: Get summaries for the day
            var dayResponse = await _defaultClient.GetAsync($"/api/notes/day?date={date:yyyy-MM-dd}");

            // Assert 3: 200 OK + NoteSummaryDto list containing our note
            dayResponse.StatusCode.Should().Be(HttpStatusCode.OK);

            var daySummaries =
                await dayResponse.Content.ReadFromJsonAsync<IReadOnlyList<NoteSummaryDto>>();

            daySummaries.Should().NotBeNull();
            daySummaries!.Should().NotBeEmpty();

            var summary = daySummaries.Single(s => s.NoteId == noteId);
            summary.Title.Should().Be(createPayload.Title);
            summary.Date.Should().Be(date);
        }

        [Fact]
        public async Task Create_note_with_empty_title_and_content_returns_bad_request()
        {
            // Arrange
            var payload = new
            {
                Date = new DateOnly(2025, 11, 10),
                Title = "   ",         // invalid (whitespace)
                Content = (string?)null,
                Summary = (string?)null,
                Tags = (string?)null
            };

            // Act
            var response = await _defaultClient.PostAsJsonAsync("/api/notes", payload);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }


        [Fact]
        public async Task Notes_for_day_are_isolated_between_different_users()
        {
            // Arrange
            var date = new DateOnly(2025, 11, 10);

            var user1Id = Guid.NewGuid();
            var user2Id = Guid.NewGuid();

            var user1Client = _factory.CreateClientAsUser(user1Id);
            var user2Client = _factory.CreateClientAsUser(user2Id);

            var user1Payload = new
            {
                Date = date,
                Title = "User1 note",
                Content = "Content 1",
                Summary = (string?)null,
                Tags = (string?)null
            };

            var user2Payload = new
            {
                Date = date,
                Title = "User2 note",
                Content = "Content 2",
                Summary = (string?)null,
                Tags = (string?)null
            };

            // Act: each user creates one note on the same date
            var user1CreateResponse = await user1Client.PostAsJsonAsync("/api/notes", user1Payload);
            var user2CreateResponse = await user2Client.PostAsJsonAsync("/api/notes", user2Payload);

            user1CreateResponse.EnsureSuccessStatusCode();
            user2CreateResponse.EnsureSuccessStatusCode();

            // Assert: day endpoint is user-isolated
            var user1DayResponse = await user1Client.GetAsync($"/api/notes/day?date={date:yyyy-MM-dd}");
            user1DayResponse.StatusCode.Should().Be(HttpStatusCode.OK);

            var user1Summaries =
                await user1DayResponse.Content.ReadFromJsonAsync<IReadOnlyList<NoteSummaryDto>>();

            user1Summaries.Should().NotBeNull();
            user1Summaries!.Should().ContainSingle(s => s.Title == "User1 note");
            user1Summaries.Should().NotContain(s => s.Title == "User2 note");

            var user2DayResponse = await user2Client.GetAsync($"/api/notes/day?date={date:yyyy-MM-dd}");
            user2DayResponse.StatusCode.Should().Be(HttpStatusCode.OK);

            var user2Summaries =
                await user2DayResponse.Content.ReadFromJsonAsync<IReadOnlyList<NoteSummaryDto>>();

            user2Summaries.Should().NotBeNull();
            user2Summaries!.Should().ContainSingle(s => s.Title == "User2 note");
            user2Summaries.Should().NotContain(s => s.Title == "User1 note");
        }

        [Fact]
        public async Task Get_notes_for_day_returns_empty_list_when_no_notes_exist()
        {
            // Arrange
            var emptyDate = new DateOnly(2030, 1, 1);

            // Act
            var response = await _defaultClient.GetAsync($"/api/notes/day?date={emptyDate:yyyy-MM-dd}");

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.OK);

            var summaries =
                await response.Content.ReadFromJsonAsync<IReadOnlyList<NoteSummaryDto>>();

            summaries.Should().NotBeNull();
            summaries!.Should().BeEmpty();
        }

        [Fact]
        public async Task Get_notes_for_day_with_default_date_returns_bad_request()
        {
            // Validator enforces Date != default(DateOnly)
            // We simulate default by passing the minimum date value.
            var invalidDate = new DateOnly(1, 1, 1); // default(DateOnly)

            // Act
            var response = await _defaultClient.GetAsync($"/api/notes/day?date={invalidDate:yyyy-MM-dd}");

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }

        #endregion

        #region Range summaries + Overview

        [Fact]
        public async Task Get_notes_for_range_returns_notes_ordered_by_date()
        {
            // Arrange
            var client = _factory.CreateClientAsUser(Guid.NewGuid());

            var start = new DateOnly(2025, 11, 1);
            var endExclusive = start.AddDays(5);

            var date1 = start.AddDays(1); // 2nd
            var date2 = start.AddDays(3); // 4th

            await CreateSimpleNote(client, date2, "Note C");
            await CreateSimpleNote(client, date1, "Note B");
            await CreateSimpleNote(client, date1, "Note A");

            // Act
            var response = await client.GetAsync($"/api/notes/range?start={start:yyyy-MM-dd}&endExclusive={endExclusive:yyyy-MM-dd}");

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.OK);

            var summaries =
                await response.Content.ReadFromJsonAsync<IReadOnlyList<NoteSummaryDto>>();

            summaries.Should().NotBeNull();
            var list = summaries!.ToList();
            list.Should().HaveCount(3);

            // Ensure results are ordered by date
            list.Select(n => n.Date).Should().BeInAscendingOrder();

            // Ensure we have the expected notes, regardless of intra-day ordering
            list.Select(n => n.Title).Should().BeEquivalentTo(new[] { "Note A", "Note B", "Note C" });
        }

        [Fact]
        public async Task Get_note_overview_for_range_returns_lightweight_overview()
        {
            // Arrange
            var client = _factory.CreateClientAsDefaultUser();

            var start = new DateOnly(2025, 11, 1);
            var endExclusive = start.AddDays(3);

            var date1 = start;
            var date2 = start.AddDays(1);

            await CreateSimpleNote(client, date1, "Note 1");
            await CreateSimpleNote(client, date2, "Note 2");

            // Act
            var response =
                await client.GetAsync($"/api/notes/overview?start={start:yyyy-MM-dd}&endExclusive={endExclusive:yyyy-MM-dd}");

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.OK);

            var overview =
                await response.Content.ReadFromJsonAsync<IReadOnlyList<NoteOverviewDto>>();

            overview.Should().NotBeNull();
            overview!.Select(o => o.Title).Should().BeEquivalentTo(new[] { "Note 1", "Note 2" });
            overview.Select(o => o.Date).Should().BeEquivalentTo(new[] { date1, date2 });
        }

        [Fact]
        public async Task Get_note_overview_for_range_respects_user_boundaries()
        {
            // Arrange
            var user1Id = Guid.NewGuid();
            var user2Id = Guid.NewGuid();

            var user1Client = _factory.CreateClientAsUser(user1Id);
            var user2Client = _factory.CreateClientAsUser(user2Id);

            var start = new DateOnly(2025, 11, 1);
            var date1 = start;                 // 1st – user 1
            var date2 = start.AddDays(1);      // 2nd – user 1
            var date3 = start.AddDays(2);      // 3rd – user 2
            var date4 = start.AddDays(3);      // 4th – user 2
            var endExclusive = start.AddDays(5);

            // User 1 data (should be visible to user 1)
            await CreateSimpleNote(user1Client, date1, "User1-Note-1");
            await CreateSimpleNote(user1Client, date2, "User1-Note-2");

            // User 2 data (must NOT leak into user 1's overview)
            await CreateSimpleNote(user2Client, date3, "User2-Note-1");
            await CreateSimpleNote(user2Client, date4, "User2-Note-2");

            // Act: user 1 requests overview for the whole range
            var response = await user1Client.GetAsync(
                $"/api/notes/overview?start={start:yyyy-MM-dd}&endExclusive={endExclusive:yyyy-MM-dd}");

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.OK);

            var overview =
                await response.Content.ReadFromJsonAsync<IReadOnlyList<NoteOverviewDto>>();

            overview.Should().NotBeNull();
            var list = overview!.ToList();

            // User 1 should only see their own dates
            list.Select(o => o.Date).Should().BeEquivalentTo(new[] { date1, date2 });

            // And the titles in overview should be composed only from user 1's notes
            list.Select(o => o.Title)
                .Should()
                .OnlyContain(title => title.Contains("User1", StringComparison.OrdinalIgnoreCase));
        }



        [Fact]
        public async Task Get_notes_for_invalid_range_returns_bad_request()
        {
            // Arrange: EndExclusive <= Start should be rejected by validator
            var start = new DateOnly(2025, 11, 5);
            var endExclusive = start; // invalid: equal

            // Act
            var response =
                await _defaultClient.GetAsync($"/api/notes/range?start={start:yyyy-MM-dd}&endExclusive={endExclusive:yyyy-MM-dd}");

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }

        #endregion

        #region Update

        [Fact]
        public async Task Update_existing_note_updates_all_mutable_fields()
        {
            // Arrange
            var client = _factory.CreateClientAsDefaultUser();
            var originalDate = new DateOnly(2025, 11, 10);

            var createPayload = new
            {
                Date = originalDate,
                Title = "Original title",
                Content = "Original content",
                Summary = "Original summary",
                Tags = "tag1"
            };

            var createResponse = await client.PostAsJsonAsync("/api/notes", createPayload);
            createResponse.EnsureSuccessStatusCode();

            var created = await createResponse.Content.ReadFromJsonAsync<NoteDetailDto>();
            created.Should().NotBeNull();

            var noteId = created!.NoteId;

            var newDate = originalDate.AddDays(1);

            var updatePayload = new
            {
                Date = newDate,
                Title = "Updated title",
                Content = "Updated content",
                Summary = "Updated summary",
                Tags = "tag2,tag3"
            };

            // Act
            var updateResponse = await client.PutAsJsonAsync($"/api/notes/{noteId}", updatePayload);

            // Assert
            updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);

            var updated = await updateResponse.Content.ReadFromJsonAsync<NoteDetailDto>();
            updated.Should().NotBeNull();

            updated!.NoteId.Should().Be(noteId);
            updated.Title.Should().Be(updatePayload.Title);
            updated.Content.Should().Be(updatePayload.Content);
            updated.Date.Should().Be(newDate);
            updated.Summary.Should().Be(updatePayload.Summary);
            updated.Tags.Should().Be(updatePayload.Tags);

            updated.UpdatedAtUtc.Should()
                .BeAfter(created.UpdatedAtUtc)
                .And
                .BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
        }

        [Fact]
        public async Task Update_with_empty_title_and_content_returns_bad_request()
        {
            // Arrange
            var client = _factory.CreateClientAsDefaultUser();
            var date = new DateOnly(2025, 11, 10);

            var createPayload = new
            {
                Date = date,
                Title = "Valid title",
                Content = "Some content",
                Summary = (string?)null,
                Tags = (string?)null
            };

            var createResponse = await client.PostAsJsonAsync("/api/notes", createPayload);
            createResponse.EnsureSuccessStatusCode();

            var created = await createResponse.Content.ReadFromJsonAsync<NoteDetailDto>();
            created.Should().NotBeNull();

            var noteId = created!.NoteId;

            // Title and Content both empty -> validator should fail
            var invalidUpdatePayload = new
            {
                Date = date,
                Title = "   ",
                Content = "   ",
                Summary = (string?)null,
                Tags = (string?)null
            };

            // Act
            var updateResponse = await client.PutAsJsonAsync($"/api/notes/{noteId}", invalidUpdatePayload);

            // Assert
            updateResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }

        [Fact]
        public async Task Cannot_update_note_belonging_to_another_user()
        {
            // Arrange
            var ownerId = Guid.NewGuid();
            var attackerId = Guid.NewGuid();

            var ownerClient = _factory.CreateClientAsUser(ownerId);
            var attackerClient = _factory.CreateClientAsUser(attackerId);

            var date = new DateOnly(2025, 11, 10);

            var createPayload = new
            {
                Date = date,
                Title = "Owner's note",
                Content = "Owner content",
                Summary = (string?)null,
                Tags = (string?)null
            };

            var createResponse = await ownerClient.PostAsJsonAsync("/api/notes", createPayload);
            createResponse.EnsureSuccessStatusCode();

            var created = await createResponse.Content.ReadFromJsonAsync<NoteDetailDto>();
            created.Should().NotBeNull();

            var noteId = created!.NoteId;

            var attackerUpdatePayload = new
            {
                Date = date,
                Title = "Attacker edit",
                Content = "Should not be allowed",
                Summary = (string?)null,
                Tags = (string?)null
            };

            // Act
            var attackerUpdateResponse =
                await attackerClient.PutAsJsonAsync($"/api/notes/{noteId}", attackerUpdatePayload);

            // Assert
            attackerUpdateResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }

        #endregion

        #region Delete

        [Fact]
        public async Task Delete_existing_note_returns_NoContent_and_hides_note_from_queries()
        {
            // Arrange
            var client = _factory.CreateClientAsDefaultUser();
            var date = new DateOnly(2025, 11, 10);

            var createPayload = new
            {
                Date = date,
                Title = "Note to delete",
                Content = "Will be deleted",
                Summary = (string?)null,
                Tags = (string?)null
            };

            var createResponse = await client.PostAsJsonAsync("/api/notes", createPayload);
            createResponse.EnsureSuccessStatusCode();

            var created = await createResponse.Content.ReadFromJsonAsync<NoteDetailDto>();
            created.Should().NotBeNull();

            var noteId = created!.NoteId;

            // Act: DELETE /api/notes/{id}
            var deleteResponse = await client.DeleteAsync($"/api/notes/{noteId}");

            // Assert: 204 NoContent
            deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

            // GET by id should no longer succeed → 404 NotFound
            var getByIdResponse = await client.GetAsync($"/api/notes/{noteId}");
            getByIdResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);

            // Day summaries should no longer contain the note
            var dayResponse = await client.GetAsync($"/api/notes/day?date={date:yyyy-MM-dd}");
            dayResponse.StatusCode.Should().Be(HttpStatusCode.OK);

            var summaries =
                await dayResponse.Content.ReadFromJsonAsync<IReadOnlyList<NoteSummaryDto>>();

            summaries.Should().NotBeNull();
            summaries!.Should().NotContain(s => s.NoteId == noteId);
        }

        [Fact]
        public async Task Delete_nonexistent_note_returns_NotFound()
        {
            // Arrange
            var client = _factory.CreateClientAsDefaultUser();
            var nonExistingNoteId = Guid.NewGuid();

            // Act
            var response = await client.DeleteAsync($"/api/notes/{nonExistingNoteId}");

            // Assert: DeleteNoteCommand uses "Notes.NotFound" metadata -> 404
            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }

        [Fact]
        public async Task Cannot_delete_note_belonging_to_another_user()
        {
            // Arrange
            var ownerId = Guid.NewGuid();
            var attackerId = Guid.NewGuid();

            var ownerClient = _factory.CreateClientAsUser(ownerId);
            var attackerClient = _factory.CreateClientAsUser(attackerId);

            var date = new DateOnly(2025, 11, 10);

            var createPayload = new
            {
                Date = date,
                Title = "Owner's note",
                Content = "Owner content",
                Summary = (string?)null,
                Tags = (string?)null
            };

            var createResponse = await ownerClient.PostAsJsonAsync("/api/notes", createPayload);
            createResponse.EnsureSuccessStatusCode();

            var created = await createResponse.Content.ReadFromJsonAsync<NoteDetailDto>();
            created.Should().NotBeNull();

            var noteId = created!.NoteId;

            // Act: attacker tries to delete
            var attackerResponse = await attackerClient.DeleteAsync($"/api/notes/{noteId}");

            // Assert
            attackerResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }


        #endregion

        #region Detail for non-existing note

        [Fact]
        public async Task Get_nonexistent_note_detail_returns_not_found()
        {
            // IMPORTANT: current GetNoteDetailQuery handler returns "Note.NotFound" (no metadata),
            // which the Result endpoint profile maps as a generic failure -> 400 (not 404).
            var client = _factory.CreateClientAsDefaultUser();
            var randomId = Guid.NewGuid();

            // Act
            var response = await client.GetAsync($"/api/notes/{randomId}");

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }

        [Fact]
        public async Task Get_notes_for_day_without_auth_returns_unauthorized()
        {
            // Arrange
            var unauthenticatedClient = _factory.CreateClient();
            var date = new DateOnly(2025, 11, 10);

            // Act
            var response = await unauthenticatedClient.GetAsync(
                $"/api/notes/day?date={date:yyyy-MM-dd}");

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }


        #endregion

        #region Helpers

        private static async Task<NoteDetailDto> CreateSimpleNote(
            HttpClient client,
            DateOnly date,
            string title)
        {
            var payload = new
            {
                Date = date,
                Title = title,
                Content = "content",
                Summary = (string?)null,
                Tags = (string?)null
            };

            var response = await client.PostAsJsonAsync("/api/notes", payload);
            response.EnsureSuccessStatusCode();

            var created = await response.Content.ReadFromJsonAsync<NoteDetailDto>();
            created.Should().NotBeNull();

            return created!;
        }

        #endregion
    }
}
