using FluentAssertions;
using NotesApp.Api.IntegrationTests.Infrastructure.Auth;
using NotesApp.Api.IntegrationTests.Infrastructure.Hosting;
using NotesApp.Application.Tasks;
using NotesApp.Application.Tasks.Models;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http.Json;
using System.Text;

namespace NotesApp.Api.IntegrationTests.Tasks
{
    /// <summary>
    /// Integration tests for the core task endpoints:
    /// - Create
    /// - Get by id
    /// - Get tasks for a day
    /// - Get tasks for a range
    /// - Get overview for a range
    /// </summary>
    public sealed class TasksEndpointsTests : IClassFixture<NotesAppApiFactory>
    {
        private readonly NotesAppApiFactory _factory;
        private readonly HttpClient _client;

        public TasksEndpointsTests(NotesAppApiFactory factory)
        {
            _factory = factory;
            var userId = Guid.NewGuid();
            _client = _factory.CreateClientAsUser(userId);
        }

        [Fact]
        public async Task Create__GetById__And_GetTasksForDay_roundtrip_succeeds()
        {
            // Arrange
            var date = new DateOnly(2025, 11, 10);

            var createPayload = new
            {
                Date = date,
                Title = "Morning deep work block",
                Description = "Focus on NotesApp backend refactor",
                StartTime = new TimeOnly(9, 0),
                EndTime = new TimeOnly(11, 0),
                Location = "Office",
                TravelTime = TimeSpan.FromMinutes(15),
                ReminderAtUtc = DateTime.UtcNow.AddHours(1)
            };

            // Act 1: Create the task
            var createResponse = await _client.PostAsJsonAsync("/api/tasks", createPayload);

            // Assert 1: Creation succeeded and returns TaskDetailDto (currently 200 OK)
            createResponse.StatusCode.Should().Be(HttpStatusCode.Created);

            var created = await createResponse.Content.ReadFromJsonAsync<TaskDetailDto>();
            created.Should().NotBeNull();
            created!.TaskId.Should().NotBeEmpty();
            created.Title.Should().Be(createPayload.Title);
            created.Date.Should().Be(date);
            created.IsCompleted.Should().BeFalse();
            created.Location.Should().Be(createPayload.Location);
            created.TravelTime.Should().Be(createPayload.TravelTime);

            created.ReminderAtUtc.Should().NotBeNull();
            created.ReminderAtUtc!.Value.Should()
                .BeCloseTo(createPayload.ReminderAtUtc, TimeSpan.FromSeconds(5));

            var taskId = created.TaskId;

            // Act 2: Get by id
            var getByIdResponse = await _client.GetAsync($"/api/tasks/{taskId}");

            // Assert 2: Detail endpoint returns the same task
            getByIdResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            var detail = await getByIdResponse.Content.ReadFromJsonAsync<TaskDetailDto>();

            detail.Should().NotBeNull();
            detail!.TaskId.Should().Be(taskId);
            detail.Title.Should().Be(createPayload.Title);
            detail.Date.Should().Be(date);

            // Act 3: Get day summaries for that date
            var dayResponse = await _client.GetAsync($"/api/tasks/day?date={date:yyyy-MM-dd}");

            // Assert 3: Day summaries include our task as a TaskSummaryDto
            dayResponse.StatusCode.Should().Be(HttpStatusCode.OK);

            var daySummaries =
                await dayResponse.Content.ReadFromJsonAsync<IReadOnlyList<TaskSummaryDto>>();

            daySummaries.Should().NotBeNull();
            daySummaries!.Should().NotBeEmpty();

            var summary = daySummaries.Single(s => s.TaskId == taskId);

            summary.Title.Should().Be(createPayload.Title);
            summary.Date.Should().Be(date);
            summary.IsCompleted.Should().BeFalse();
            summary.Location.Should().Be(createPayload.Location);
            summary.TravelTime.Should().Be(createPayload.TravelTime);
        }

        [Fact]
        public async Task Get_nonexistent_task_detail_returns_not_found()
        {
            // Arrange
            var nonExistentTaskId = Guid.NewGuid();

            // Act
            var response = await _client.GetAsync($"api/tasks/{nonExistentTaskId}");

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }


        [Fact]
        public async Task Get_tasks_for_invalid_range_returns_bad_request()
        {
            // Arrange: EndExclusive <= Start should be rejected by validator
            var start = new DateOnly(2025, 11, 5);
            var endExclusive = start; // invalid: equal

            // Act
            var response = await _client.GetAsync(
                $"api/tasks/range?start={start:yyyy-MM-dd}&endExclusive={endExclusive:yyyy-MM-dd}");

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }


        [Fact]
        public async Task Get_tasks_for_day_with_default_date_returns_bad_request()
        {
            // Validator enforces Date != default(DateOnly).
            // We simulate default by passing the minimum date value explicitly.
            var invalidDate = new DateOnly(1, 1, 1); // default(DateOnly)

            // Act
            var response = await _client.GetAsync(
                $"api/tasks/day?date={invalidDate:yyyy-MM-dd}");

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }


        [Fact]
        public async Task Tasks_for_day_are_isolated_between_different_users()
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
                Title = "User1 task",
                Description = (string?)null,
                StartTime = (TimeOnly?)null,
                EndTime = (TimeOnly?)null,
                Location = (string?)null,
                TravelTime = (TimeSpan?)null,
                ReminderAtUtc = (DateTime?)null
            };

            var user2Payload = new
            {
                Date = date,
                Title = "User2 task",
                Description = (string?)null,
                StartTime = (TimeOnly?)null,
                EndTime = (TimeOnly?)null,
                Location = (string?)null,
                TravelTime = (TimeSpan?)null,
                ReminderAtUtc = (DateTime?)null
            };

            // Act: Each user creates one task on the same date
            var user1CreateResponse = await user1Client.PostAsJsonAsync("/api/tasks", user1Payload);
            var user2CreateResponse = await user2Client.PostAsJsonAsync("/api/tasks", user2Payload);

            user1CreateResponse.EnsureSuccessStatusCode();
            user2CreateResponse.EnsureSuccessStatusCode();

            // Assert: Each user sees only their own task in /day endpoint
            var user1DayResponse = await user1Client.GetAsync($"/api/tasks/day?date={date:yyyy-MM-dd}");
            user1DayResponse.StatusCode.Should().Be(HttpStatusCode.OK);

            var user1Summaries =
                await user1DayResponse.Content.ReadFromJsonAsync<IReadOnlyList<TaskSummaryDto>>();

            user1Summaries.Should().NotBeNull();
            user1Summaries!.Should().ContainSingle(s => s.Title == "User1 task");
            user1Summaries.Should().NotContain(s => s.Title == "User2 task");

            var user2DayResponse = await user2Client.GetAsync($"/api/tasks/day?date={date:yyyy-MM-dd}");
            user2DayResponse.StatusCode.Should().Be(HttpStatusCode.OK);

            var user2Summaries =
                await user2DayResponse.Content.ReadFromJsonAsync<IReadOnlyList<TaskSummaryDto>>();

            user2Summaries.Should().NotBeNull();
            user2Summaries!.Should().ContainSingle(s => s.Title == "User2 task");
            user2Summaries.Should().NotContain(s => s.Title == "User1 task");
        }

        [Fact]
        public async Task Get_tasks_for_day_returns_empty_list_when_no_tasks_exist()
        {
            // Arrange
            var dateWithoutTasks = new DateOnly(2030, 1, 1);

            // Act
            var response = await _client.GetAsync($"/api/tasks/day?date={dateWithoutTasks:yyyy-MM-dd}");

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.OK);

            var summaries =
                await response.Content.ReadFromJsonAsync<IReadOnlyList<TaskSummaryDto>>();

            summaries.Should().NotBeNull();
            summaries!.Should().BeEmpty();
        }

        [Fact]
        public async Task Get_tasks_for_range_returns_tasks_ordered_by_date_then_start_time()
        {
            // Arrange
            var client = _factory.CreateClientAsUser(Guid.NewGuid());

            var start = new DateOnly(2025, 11, 1);
            var endExclusive = start.AddDays(5);

            var date1 = start.AddDays(1); // 2nd
            var date2 = start.AddDays(3); // 4th

            // date2 morning
            await CreateSimpleTask(client, date2, "Task C", new TimeOnly(8, 0));
            // date1 afternoon
            await CreateSimpleTask(client, date1, "Task B", new TimeOnly(15, 0));
            // date1 morning
            await CreateSimpleTask(client, date1, "Task A", new TimeOnly(9, 0));

            // Act
            var response = await client.GetAsync($"/api/tasks/range?start={start:yyyy-MM-dd}&endExclusive={endExclusive:yyyy-MM-dd}");

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.OK);

            var summaries =
                await response.Content.ReadFromJsonAsync<IReadOnlyList<TaskSummaryDto>>();

            summaries.Should().NotBeNull();
            var list = summaries!.ToList();

            list.Should().HaveCount(3);

            // Ordered by Date, then StartTime
            list[0].Title.Should().Be("Task A"); // date1 09:00
            list[1].Title.Should().Be("Task B"); // date1 15:00
            list[2].Title.Should().Be("Task C"); // date2 08:00
        }

        [Fact]
        public async Task Get_task_overview_for_range_returns_lightweight_overview()
        {
            // Arrange
            var ownerId = Guid.NewGuid();
            var client = _factory.CreateClientAsUser(ownerId);

            var start = new DateOnly(2025, 11, 1);
            var endExclusive = start.AddDays(3);

            var date1 = start;
            var date2 = start.AddDays(1);

            await CreateSimpleTask(client, date1, "Task 1");
            await CreateSimpleTask(client, date2, "Task 2");

            // Act
            var response = await client.GetAsync($"/api/tasks/overview?start={start:yyyy-MM-dd}&endExclusive={endExclusive:yyyy-MM-dd}");

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.OK);

            var overview =
                await response.Content.ReadFromJsonAsync<IReadOnlyList<TaskOverviewDto>>();

            overview.Should().NotBeNull();
            overview!.Select(o => o.Title).Should().BeEquivalentTo(new[] { "Task 1", "Task 2" });
            overview.Select(o => o.Date).Should().BeEquivalentTo(new[] { date1, date2 });
        }


        [Fact]
        public async Task Get_tasks_for_day_without_auth_returns_unauthorized()
        {
            // Arrange: raw client without the test auth handler's Authorization header
            var unauthenticatedClient = _factory.CreateClient();

            var date = new DateOnly(2025, 11, 10);

            // Act
            var response = await unauthenticatedClient.GetAsync(
                $"/api/tasks/day?date={date:yyyy-MM-dd}");

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }


        [Fact]
        public async Task Get_tasks_for_day_with_required_scope_returns_ok()
        {
            // Arrange
            var client = _factory.CreateClient();

            var userId = Guid.NewGuid();
            client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeaderName, userId.ToString());
            client.DefaultRequestHeaders.Add(TestAuthHandler.ScopeHeaderName,
                "api://d1047ffd-a054-4a9f-aeb0-198996f0c0c6/notes.readwrite");

            var date = new DateOnly(2025, 11, 10);

            // Act
            var response = await client.GetAsync($"/api/tasks/day?date={date:yyyy-MM-dd}");

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.OK);
        }


        [Fact]
        public async Task Get_tasks_for_day_without_required_scope_returns_forbidden()
        {
            var client = _factory.CreateClient();

            var userId = Guid.NewGuid();
            client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeaderName, userId.ToString());
            // Notice: no X-Test-Scopes header, or you can send a wrong scope

            var date = new DateOnly(2025, 11, 10);

            var response = await client.GetAsync($"/api/tasks/day?date={date:yyyy-MM-dd}");

            response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        

        private static async Task<TaskDetailDto> CreateSimpleTask(HttpClient client,
                                                                  DateOnly date,
                                                                  string title,
                                                                  TimeOnly? startTime = null)
        {
            var payload = new
            {
                date,
                title,
                description = (string?)null,
                startTime,
                endTime = (TimeOnly?)null,
                location = (string?)null,
                travelTime = (TimeSpan?)null,
                reminderAtUtc = (DateTime?)null
            };

            var response = await client.PostAsJsonAsync("api/tasks", payload);

            // This ensures we see the real HTTP status in failures
            response.EnsureSuccessStatusCode();

            var dto = await response.Content.ReadFromJsonAsync<TaskDetailDto>();
            dto.Should().NotBeNull();

            return dto!;
        }
    }
}
