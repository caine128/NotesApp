using FluentAssertions;
using NotesApp.Api.IntegrationTests.Infrastructure.Hosting;
using NotesApp.Application.Tasks;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http.Json;
using System.Text;

namespace NotesApp.Api.IntegrationTests.Tasks
{
    /// <summary>
    /// Integration tests for the month overview endpoint:
    /// - Verifies per-day aggregation for a single user.
    /// - Verifies isolation between different users.
    ///
    /// These tests:
    /// - Start the real NotesApp.Api application in a test host.
    /// - Authenticate using the TestAuthHandler (no real Entra calls).
    /// - Exercise controllers, MediatR, EF Core, and the database together.
    /// </summary>
    public sealed class TaskMonthOverviewEndpointsTests : IClassFixture<NotesAppApiFactory>
    {
        private readonly NotesAppApiFactory _factory;

        public TaskMonthOverviewEndpointsTests(NotesAppApiFactory factory)
        {
            _factory = factory;
        }

        [Fact]
        public async Task Month_overview_aggregates_tasks_per_day_for_same_user()
        {
            // Arrange: simulate a specific "current user" via header
            var userId = Guid.NewGuid();
            var client = _factory.CreateClientAsUser(userId);

            const int year = 2025;
            const int month = 2;

            var date1 = new DateOnly(year, month, 20);
            var date2 = new DateOnly(year, month, 21);

            // Create 2 tasks on date1: one with reminder, one without
            var createPayload1 = new
            {
                date = date1,
                title = "Task 1 on date1",
                reminderAtUtc = (DateTime?)null
            };

            var createPayload2 = new
            {
                date = date1,
                title = "Task 2 on date1 (with reminder)",
                reminderAtUtc = DateTime.SpecifyKind(DateTime.UtcNow.AddHours(1), DateTimeKind.Utc)
            };

            var createPayload3 = new
            {
                date = date2,
                title = "Task 1 on date2",
                reminderAtUtc = (DateTime?)null
            };

            // Act 1: create tasks
            var resp1 = await client.PostAsJsonAsync("api/tasks", createPayload1);
            resp1.EnsureSuccessStatusCode();

            var resp2 = await client.PostAsJsonAsync("api/tasks", createPayload2);
            resp2.EnsureSuccessStatusCode();

            var resp3 = await client.PostAsJsonAsync("api/tasks", createPayload3);
            resp3.EnsureSuccessStatusCode();

            // Act 2: request month overview for that user
            var overviewResponse =
                await client.GetAsync($"api/tasks/month-overview?year={year}&month={month}");

            overviewResponse.StatusCode.Should().Be(HttpStatusCode.OK);

            var overview =
                await overviewResponse.Content.ReadFromJsonAsync<IReadOnlyList<DayTasksOverviewDto>>();

            overview.Should().NotBeNull();

            // Assert: there should be entries for both dates with correct aggregates
            var day1 = overview!.SingleOrDefault(o => o.Date == date1);
            var day2 = overview.SingleOrDefault(o => o.Date == date2);

            day1.Should().NotBeNull();
            day2.Should().NotBeNull();

            day1!.TotalTasks.Should().Be(2);
            day1.CompletedTasks.Should().Be(0);       // No completion toggling yet in these tests
            day1.HasAnyReminder.Should().BeTrue();    // One task has a reminder

            day2!.TotalTasks.Should().Be(1);
            day2.CompletedTasks.Should().Be(0);
            day2.HasAnyReminder.Should().BeFalse();
        }

        [Fact]
        public async Task Month_overview_is_isolated_between_different_fake_users()
        {
            // Arrange two different fake users
            var userA = Guid.NewGuid();
            var userB = Guid.NewGuid();

            var clientA = _factory.CreateClientAsUser(userA);
            var clientB = _factory.CreateClientAsUser(userB);

            const int year = 2025;
            const int month = 2;
            var date = new DateOnly(year, month, 22);

            var createPayload = new
            {
                date = date,
                title = "User A's task for month overview",
                reminderAtUtc = (DateTime?)null
            };

            // Act: user A creates a task
            var createResponse = await clientA.PostAsJsonAsync("api/tasks", createPayload);
            createResponse.EnsureSuccessStatusCode();

            // Act: user A gets month overview
            var overviewResponseForA =
                await clientA.GetAsync($"api/tasks/month-overview?year={year}&month={month}");

            overviewResponseForA.StatusCode.Should().Be(HttpStatusCode.OK);

            var overviewForA =
                await overviewResponseForA.Content.ReadFromJsonAsync<IReadOnlyList<DayTasksOverviewDto>>();

            overviewForA.Should().NotBeNull();
            overviewForA!.Should().Contain(o => o.Date == date && o.TotalTasks == 1);

            // Act: user B gets month overview for the same month
            var overviewResponseForB =
                await clientB.GetAsync($"api/tasks/month-overview?year={year}&month={month}");

            overviewResponseForB.StatusCode.Should().Be(HttpStatusCode.OK);

            var overviewForB =
                await overviewResponseForB.Content.ReadFromJsonAsync<IReadOnlyList<DayTasksOverviewDto>>();

            // Assert: user B should see no tasks, because tasks are user-scoped
            overviewForB.Should().NotBeNull();
            overviewForB!.Should().BeEmpty();
        }
    }
}
