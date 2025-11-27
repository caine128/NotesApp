using FluentAssertions;
using NotesApp.Api.IntegrationTests.Infrastructure.Hosting;
using NotesApp.Application.Calendar;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http.Json;
using System.Text;

namespace NotesApp.Api.IntegrationTests.Calendar
{
    /// <summary>
    /// Integration tests for the Calendar API endpoints.
    /// 
    /// These tests use the real application startup + TestAuthHandler,
    /// and validate that tasks and notes are aggregated correctly per day.
    /// </summary>
    public sealed class CalendarEndpointsTests : IClassFixture<NotesAppApiFactory>
    {
        private readonly NotesAppApiFactory _factory;

        public CalendarEndpointsTests(NotesAppApiFactory factory)
        {
            _factory = factory;
        }

        [Fact]
        public async Task Month_overview_includes_tasks_and_notes_with_correct_counts()
        {
            // Arrange
            var userId = Guid.NewGuid();
            var client = _factory.CreateClientAsUser(userId);

            var year = 2025;
            var month = 2;

            var dayWithBoth = new DateOnly(year, month, 10);
            var dayWithOnlyTasks = new DateOnly(year, month, 11);

            // Create 2 tasks (1 completed) and 1 note on dayWithBoth
            await client.PostAsJsonAsync("api/tasks", new
            {
                date = dayWithBoth,
                title = "Task 1",
                reminderAtUtc = (DateTime?)null
            });

            var task2Response = await client.PostAsJsonAsync("api/tasks", new
            {
                date = dayWithBoth,
                title = "Task 2",
                reminderAtUtc = (DateTime?)null
            });

            task2Response.EnsureSuccessStatusCode();
            var task2 = await task2Response.Content.ReadFromJsonAsync<NotesApp.Application.Tasks.TaskDto>();
            task2.Should().NotBeNull();

            // Mark Task 2 as completed
            var completeResponse = await client.PatchAsJsonAsync(
                $"api/tasks/{task2!.TaskId}/completion",
                new { isCompleted = true });

            completeResponse.EnsureSuccessStatusCode();

            // Create 1 note on dayWithBoth
            await client.PostAsJsonAsync("api/notes", new
            {
                date = dayWithBoth,
                title = "Note 1",
                content = "Content"
            });

            // Create 1 pending task on dayWithOnlyTasks
            await client.PostAsJsonAsync("api/tasks", new
            {
                date = dayWithOnlyTasks,
                title = "Task Only Day",
                reminderAtUtc = (DateTime?)null
            });

            // Act
            var overviewResponse = await client.GetAsync(
                $"api/calendar/month-overview?year={year}&month={month}");

            // Assert HTTP
            overviewResponse.StatusCode.Should().Be(HttpStatusCode.OK);

            var overview =
                await overviewResponse.Content.ReadFromJsonAsync<CalendarDayOverviewDto[]>();

            overview.Should().NotBeNull();
            overview!.Length.Should().Be(DateTime.DaysInMonth(year, month));

            // Find entries for our two days
            var bothDay = overview.Single(d => d.Date == dayWithBoth);
            var tasksOnlyDay = overview.Single(d => d.Date == dayWithOnlyTasks);

            // Day with both tasks and note: 2 tasks (1 completed, 1 pending), 1 note
            bothDay.TotalTasks.Should().Be(2);
            bothDay.CompletedTasks.Should().Be(1);
            bothDay.PendingTasks.Should().Be(1);
            bothDay.NoteCount.Should().Be(1);

            // Day with only tasks: 1 task (pending), 0 notes
            tasksOnlyDay.TotalTasks.Should().Be(1);
            tasksOnlyDay.CompletedTasks.Should().Be(0);
            tasksOnlyDay.PendingTasks.Should().Be(1);
            tasksOnlyDay.NoteCount.Should().Be(0);
        }

        [Fact]
        public async Task Month_overview_isolated_between_different_users()
        {
            // Arrange
            var userA = Guid.NewGuid();
            var userB = Guid.NewGuid();

            var clientA = _factory.CreateClientAsUser(userA);
            var clientB = _factory.CreateClientAsUser(userB);

            var year = 2025;
            var month = 3;
            var date = new DateOnly(year, month, 5);

            // User A creates a task and a note
            await clientA.PostAsJsonAsync("api/tasks", new
            {
                date,
                title = "User A Task",
                reminderAtUtc = (DateTime?)null
            });

            await clientA.PostAsJsonAsync("api/notes", new
            {
                date,
                title = "User A Note",
                content = "Owned by A"
            });

            // Act: User B asks for overview of same month
            var responseB = await clientB.GetAsync(
                $"api/calendar/month-overview?year={year}&month={month}");

            responseB.StatusCode.Should().Be(HttpStatusCode.OK);

            var overviewB =
                await responseB.Content.ReadFromJsonAsync<CalendarDayOverviewDto[]>();

            overviewB.Should().NotBeNull();
            overviewB!.Length.Should().Be(DateTime.DaysInMonth(year, month));

            var dayForB = overviewB.Single(d => d.Date == date);

            // Assert: user B must not see user A's data (all zeros)
            dayForB.TotalTasks.Should().Be(0);
            dayForB.CompletedTasks.Should().Be(0);
            dayForB.PendingTasks.Should().Be(0);
            dayForB.NoteCount.Should().Be(0);
        }
    }
}
