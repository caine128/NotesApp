using FluentAssertions;
using NotesApp.Api.IntegrationTests.Infrastructure.Hosting;
using NotesApp.Application.Calendar;
using NotesApp.Application.Calendar.Models;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace NotesApp.Api.IntegrationTests.Calendar
{
    /// <summary>
    /// End-to-end tests for Calendar endpoints:
    /// - /api/calendar/summary/day
    /// - /api/calendar/summary/range
    /// - /api/calendar/overview/range
    ///
    /// These tests verify:
    /// - Aggregation of tasks + notes into a single calendar view.
    /// - Correct date filtering (single day vs range, inclusive/exclusive semantics).
    /// - Per-user isolation (no data leakage between users).
    /// - Behaviour on empty days and invalid ranges (validation).
    /// - Soft-deleted items not appearing in calendar results.
    /// </summary>
    [Collection("Api")]
    public sealed class CalendarEndpointsTests : IClassFixture<NotesAppApiFactory>
    {
        private readonly NotesAppApiFactory _factory;

        public CalendarEndpointsTests(NotesAppApiFactory factory)
        {
            _factory = factory;
        }

        [Fact]
        public async Task GetCalendarSummaryForDay_Returns_Tasks_And_Notes_For_That_Day()
        {
            // Arrange
            var userId = Guid.NewGuid();
            var client = _factory.CreateClientAsUser(userId);

            var targetDate = new DateOnly(2025, 1, 15);

            // Create tasks and notes on the target date
            await CreateTaskAsync(client, targetDate, "Task A");
            await CreateTaskAsync(client, targetDate, "Task B");
            await CreateNoteAsync(client, targetDate, "Note A");

            // Noise: items on another date should NOT appear
            await CreateTaskAsync(client, targetDate.AddDays(1), "Task on another day");
            await CreateNoteAsync(client, targetDate.AddDays(1), "Note on another day");

            // Act
            var response = await client.GetAsync(
                $"api/calendar/summary/day?date={targetDate:yyyy-MM-dd}");

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.OK);

            var summary =
                await response.Content.ReadFromJsonAsync<CalendarSummaryDto>();

            summary.Should().NotBeNull();
            summary!.Date.Should().Be(targetDate);

            summary.Tasks
                   .Select(t => t.Title)
                   .Should()
                   .BeEquivalentTo(new[] { "Task A", "Task B" });

            summary.Notes
                   .Select(n => n.Title)
                   .Should()
                   .BeEquivalentTo(new[] { "Note A" });
        }

        [Fact]
        public async Task GetCalendarSummaryForDay_Excludes_Other_Users_Data()
        {
            // Arrange
            var userA = Guid.NewGuid();
            var userB = Guid.NewGuid();

            var clientA = _factory.CreateClientAsUser(userA);
            var clientB = _factory.CreateClientAsUser(userB);

            var date = new DateOnly(2025, 2, 1);

            // Data for user A
            await CreateTaskAsync(clientA, date, "User A Task");
            await CreateNoteAsync(clientA, date, "User A Note");

            // Data for user B (should not leak into user A's calendar)
            await CreateTaskAsync(clientB, date, "User B Task");
            await CreateNoteAsync(clientB, date, "User B Note");

            // Act
            var response = await clientA.GetAsync(
                $"api/calendar/summary/day?date={date:yyyy-MM-dd}");

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.OK);

            var summary =
                await response.Content.ReadFromJsonAsync<CalendarSummaryDto>();

            summary.Should().NotBeNull();
            summary!.Date.Should().Be(date);

            summary.Tasks
                   .Select(t => t.Title)
                   .Should()
                   .BeEquivalentTo(new[] { "User A Task" });

            summary.Notes
                   .Select(n => n.Title)
                   .Should()
                   .BeEquivalentTo(new[] { "User A Note" });
        }

        [Fact]
        public async Task GetCalendarSummaryForRange_Returns_Continuous_Days_And_Honors_SoftDelete()
        {
            // Arrange
            var userId = Guid.NewGuid();
            var client = _factory.CreateClientAsUser(userId);

            var start = new DateOnly(2025, 3, 10);
            var endExclusive = start.AddDays(4); // 10, 11, 12, 13

            // Day 1: 2 tasks + 1 note
            var day1 = start;
            var day1Task1 = await CreateTaskAsync(client, day1, "D1 Task 1");
            var day1Task2 = await CreateTaskAsync(client, day1, "D1 Task 2");
            var day1Note1 = await CreateNoteAsync(client, day1, "D1 Note 1");

            // Day 2: 1 task (will be soft-deleted) + 1 note
            var day2 = start.AddDays(1);
            var day2TaskToDelete = await CreateTaskAsync(client, day2, "D2 Task To Delete");
            var day2Note1 = await CreateNoteAsync(client, day2, "D2 Note 1");

            // Day 3: no items

            // Day 4: only tasks
            var day4 = start.AddDays(3);
            await CreateTaskAsync(client, day4, "D4 Task 1");

            // Soft delete one task on day 2
            await client.DeleteAsync($"api/tasks/{day2TaskToDelete}");

            // Act
            var response = await client.GetAsync(
                $"api/calendar/summary/range?start={start:yyyy-MM-dd}&endExclusive={endExclusive:yyyy-MM-dd}");

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.OK);

            var summaries =
                await response.Content.ReadFromJsonAsync<IReadOnlyList<CalendarSummaryDto>>();

            summaries.Should().NotBeNull();
            summaries!.Count.Should().Be(4); // 10, 11, 12, 13

            // Day 1 checks
            var s1 = summaries.Single(s => s.Date == day1);
            s1.Tasks.Select(t => t.Title).Should().BeEquivalentTo("D1 Task 1", "D1 Task 2");
            s1.Notes.Select(n => n.Title).Should().BeEquivalentTo("D1 Note 1");

            // Day 2 checks: soft-deleted task must not appear
            var s2 = summaries.Single(s => s.Date == day2);
            s2.Tasks.Select(t => t.Title).Should().BeEmpty();
            s2.Notes.Select(n => n.Title).Should().BeEquivalentTo("D2 Note 1");

            // Day 3 checks: empty lists but day must be present
            var day3 = start.AddDays(2);
            var s3 = summaries.Single(s => s.Date == day3);
            s3.Tasks.Should().BeEmpty();
            s3.Notes.Should().BeEmpty();

            // Day 4 checks
            var s4 = summaries.Single(s => s.Date == day4);
            s4.Tasks.Select(t => t.Title).Should().BeEquivalentTo("D4 Task 1");
            s4.Notes.Should().BeEmpty();
        }

        [Fact]
        public async Task GetCalendarSummaryForRange_Returns_BadRequest_When_EndExclusive_Not_Greater_Than_Start()
        {
            // Arrange
            var userId = Guid.NewGuid();
            var client = _factory.CreateClientAsUser(userId);

            var start = new DateOnly(2025, 4, 1);
            var endExclusive = start; // invalid: EndExclusive must be > Start

            // Act
            var response = await client.GetAsync(
                $"api/calendar/summary/range?start={start:yyyy-MM-dd}&endExclusive={endExclusive:yyyy-MM-dd}");

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }

        [Fact]
        public async Task GetCalendarOverviewForRange_Returns_Titles_Per_Day_And_Respects_User_Boundaries()
        {
            // Arrange
            var userA = Guid.NewGuid();
            var userB = Guid.NewGuid();

            var clientA = _factory.CreateClientAsUser(userA);
            var clientB = _factory.CreateClientAsUser(userB);

            var start = new DateOnly(2025, 5, 1);
            var endExclusive = start.AddDays(3); // 1, 2, 3

            // User A data
            var aDay1 = start;
            var aDay2 = start.AddDays(1);

            await CreateTaskAsync(clientA, aDay1, "A D1 Task");
            await CreateNoteAsync(clientA, aDay1, "A D1 Note");

            await CreateTaskAsync(clientA, aDay2, "A D2 Task");

            // User B data in the same range (must not leak into user A's overview)
            await CreateTaskAsync(clientB, aDay1, "B D1 Task");
            await CreateNoteAsync(clientB, aDay2, "B D2 Note");

            // Act
            var response = await clientA.GetAsync(
                $"api/calendar/overview/range?start={start:yyyy-MM-dd}&endExclusive={endExclusive:yyyy-MM-dd}");

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.OK);

            var overview =
                await response.Content.ReadFromJsonAsync<IReadOnlyList<CalendarOverviewDto>>();

            overview.Should().NotBeNull();
            overview!.Count.Should().Be(3); // 1,2,3

            var o1 = overview.Single(o => o.Date == aDay1);
            o1.Tasks.Select(t => t.Title).Should().BeEquivalentTo("A D1 Task");
            o1.Notes.Select(n => n.Title).Should().BeEquivalentTo("A D1 Note");

            var o2 = overview.Single(o => o.Date == aDay2);
            o2.Tasks.Select(t => t.Title).Should().BeEquivalentTo("A D2 Task");
            o2.Notes.Should().BeEmpty();

            var o3 = overview.Single(o => o.Date == start.AddDays(2));
            o3.Tasks.Should().BeEmpty();
            o3.Notes.Should().BeEmpty();
        }

        #region Helper methods

        /// <summary>
        /// Creates a task via the Tasks API and returns its TaskId.
        /// The payload mirrors CreateTaskCommand properties we actually use in tests.
        /// </summary>
        private static async Task<Guid> CreateTaskAsync(
            HttpClient client,
            DateOnly date,
            string title)
        {
            var payload = new
            {
                date,
                title,
                description = (string?)null,
                startTime = (TimeOnly?)null,
                endTime = (TimeOnly?)null,
                location = (string?)null,
                travelTimeMinutes = (int?)null,
                reminderAtUtc = (DateTime?)null
            };

            var response = await client.PostAsJsonAsync("api/tasks", payload);
            response.EnsureSuccessStatusCode();

            // We keep this generic and just pull out the TaskId from the JSON,
            // so we don't depend on the exact DTO type exposed by the API.
            var json = await response.Content.ReadFromJsonAsync<JsonElement>();
            if (json.TryGetProperty("taskId", out var idProperty))
            {
                return idProperty.GetGuid();
            }

            // Fallback for older/newer naming if needed in the future
            if (json.TryGetProperty("id", out var altId))
            {
                return altId.GetGuid();
            }

            throw new InvalidOperationException("Could not find TaskId in task creation response.");
        }

        /// <summary>
        /// Creates a note via the Notes API and returns its NoteId.
        /// </summary>
        private static async Task<Guid> CreateNoteAsync(
            HttpClient client,
            DateOnly date,
            string title)
        {
            var payload = new
            {
                date,
                title,
                content = "Sample content for calendar tests",
                summary = (string?)null,
                tags = Array.Empty<string>()
            };

            var response = await client.PostAsJsonAsync("api/notes", payload);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadFromJsonAsync<JsonElement>();
            if (json.TryGetProperty("noteId", out var idProperty))
            {
                return idProperty.GetGuid();
            }

            if (json.TryGetProperty("id", out var altId))
            {
                return altId.GetGuid();
            }

            throw new InvalidOperationException("Could not find NoteId in note creation response.");
        }

        #endregion
    }
}
