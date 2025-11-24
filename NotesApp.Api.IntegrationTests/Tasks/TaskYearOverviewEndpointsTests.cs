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
    /// Integration tests for GET /api/tasks/year-overview.
    /// </summary>
    public sealed class TaskYearOverviewEndpointsTests : IClassFixture<NotesAppApiFactory>
    {
        private readonly NotesAppApiFactory _factory;

        public TaskYearOverviewEndpointsTests(NotesAppApiFactory factory)
        {
            _factory = factory;
        }

        [Fact]
        public async Task Year_overview_aggregates_tasks_per_month_for_same_user()
        {
            // Arrange
            var userId = Guid.NewGuid();
            var client = _factory.CreateClientAsUser(userId);

            const int year = 2025;

            var dateFeb = new DateOnly(year, 2, 15);
            var dateMar = new DateOnly(year, 3, 10);

            var payloadFeb1 = new { date = dateFeb, title = "Feb task 1", reminderAtUtc = (DateTime?)null };
            var payloadFeb2 = new { date = dateFeb, title = "Feb task 2", reminderAtUtc = (DateTime?)null };
            var payloadMar1 = new { date = dateMar, title = "Mar task 1", reminderAtUtc = (DateTime?)null };

            // Create tasks: 2 in February, 1 in March
            var resp1 = await client.PostAsJsonAsync("api/tasks", payloadFeb1);
            resp1.EnsureSuccessStatusCode();

            var resp2 = await client.PostAsJsonAsync("api/tasks", payloadFeb2);
            resp2.EnsureSuccessStatusCode();

            var resp3 = await client.PostAsJsonAsync("api/tasks", payloadMar1);
            resp3.EnsureSuccessStatusCode();

            // Act: get year overview
            var overviewResponse =
                await client.GetAsync($"api/tasks/year-overview?year={year}");

            overviewResponse.StatusCode.Should().Be(HttpStatusCode.OK);

            var overview =
                await overviewResponse.Content.ReadFromJsonAsync<IReadOnlyList<MonthTasksOverviewDto>>();

            overview.Should().NotBeNull();

            // Assert: we should see entries for February (2 tasks) and March (1 task)
            overview!.Should().Contain(o =>
                o.Year == year &&
                o.Month == 2 &&
                o.TotalTasks == 2 &&
                o.CompletedTasks == 0 &&
                o.PendingTasks == 2);

            overview.Should().Contain(o =>
                o.Year == year &&
                o.Month == 3 &&
                o.TotalTasks == 1 &&
                o.CompletedTasks == 0 &&
                o.PendingTasks == 1);
        }

        [Fact]
        public async Task Year_overview_is_isolated_between_different_users()
        {
            // Arrange
            var userA = Guid.NewGuid();
            var userB = Guid.NewGuid();

            var clientA = _factory.CreateClientAsUser(userA);
            var clientB = _factory.CreateClientAsUser(userB);

            const int year = 2025;
            var date = new DateOnly(year, 4, 5);

            var payload = new { date = date, title = "User A year-overview task", reminderAtUtc = (DateTime?)null };

            // User A creates a task
            var createResponse = await clientA.PostAsJsonAsync("api/tasks", payload);
            createResponse.EnsureSuccessStatusCode();

            // User A gets year overview
            var overviewResponseA =
                await clientA.GetAsync($"api/tasks/year-overview?year={year}");

            overviewResponseA.StatusCode.Should().Be(HttpStatusCode.OK);

            var overviewA =
                await overviewResponseA.Content.ReadFromJsonAsync<IReadOnlyList<MonthTasksOverviewDto>>();

            overviewA.Should().NotBeNull();
            overviewA!.Should().Contain(o => o.Year == year && o.Month == 4 && o.TotalTasks == 1);

            // User B gets year overview for same year
            var overviewResponseB =
                await clientB.GetAsync($"api/tasks/year-overview?year={year}");

            overviewResponseB.StatusCode.Should().Be(HttpStatusCode.OK);

            var overviewB =
                await overviewResponseB.Content.ReadFromJsonAsync<IReadOnlyList<MonthTasksOverviewDto>>();

            overviewB.Should().NotBeNull();
            overviewB!.Should().BeEmpty();
        }
    }
}
