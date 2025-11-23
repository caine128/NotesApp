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
    /// Integration tests for the Tasks API endpoints.
    ///
    /// These tests:
    /// - Start the real NotesApp.Api application in a test host.
    /// - Authenticate using the TestAuthHandler (no real Entra calls).
    /// - Exercise controllers, MediatR, EF Core, and the database together.
    /// </summary>
    public sealed class TasksEndpointsTests : IClassFixture<NotesAppApiFactory>
    {
        private readonly NotesAppApiFactory _factory;

        public TasksEndpointsTests(NotesAppApiFactory factory)
        {
            _factory = factory;
        }

        [Fact]
        public async Task Create_and_get_tasks_for_day_roundtrip_succeeds()
        {
            // Arrange: simulate a specific "current user" via header
            var userId = Guid.NewGuid();
            var client = _factory.CreateClientAsUser(userId);

            var date = new DateOnly(2025, 2, 20);

            var createPayload = new
            {
                date = date,
                title = "Integration test task",
                // Optionally, try with null or an actual value
                reminderAtUtc = DateTime.UtcNow.AddHours(1)
            };

            // Act 1: create the task
            var createResponse = await client.PostAsJsonAsync("api/tasks", createPayload);

            // Assert 1: creation succeeded and returned a TaskDto
            createResponse.StatusCode.Should().Be(HttpStatusCode.OK);

            var createdTask = await createResponse.Content.ReadFromJsonAsync<TaskDto>();
            createdTask.Should().NotBeNull();

            createdTask!.Title.Should().Be("Integration test task");

            // UserId is the *internal* user id created by CurrentUserService/User.Create.
            // It should be a valid, non-empty GUID, but not necessarily equal to the external "sub".
            createdTask.UserId.Should().NotBe(Guid.Empty);

            createdTask.Date.Should().Be(date);

            // Act 2: get tasks for that day
            var getResponse = await client.GetAsync($"api/tasks/day?date={date:yyyy-MM-dd}");

            // Assert 2: request succeeded and our task is in the list
            getResponse.StatusCode.Should().Be(HttpStatusCode.OK);

            var tasksForDay = await getResponse.Content.ReadFromJsonAsync<IReadOnlyList<TaskDto>>();

            tasksForDay.Should().NotBeNull();
            tasksForDay!.Should().Contain(t => t.TaskId == createdTask.TaskId);

            // All tasks returned for this user/day should share the same internal UserId:
            var distinctUserIds = tasksForDay.Select(t => t.UserId).Distinct().ToList();
            distinctUserIds.Should().HaveCount(1);
            distinctUserIds[0].Should().Be(createdTask.UserId);
        }

        [Fact]
        public async Task Tasks_are_isolated_between_different_fake_users()
        {
            // Arrange
            var userA = Guid.NewGuid();
            var userB = Guid.NewGuid();

            var clientA = _factory.CreateClientAsUser(userA);
            var clientB = _factory.CreateClientAsUser(userB);

            var date = new DateOnly(2025, 2, 21);

            var createPayload = new
            {
                date = date,
                title = "User A's task",
                reminderAtUtc = (DateTime?)null
            };

            // Act: user A creates a task
            var createResponse = await clientA.PostAsJsonAsync("api/tasks", createPayload);
            createResponse.EnsureSuccessStatusCode();

            // Act: user B asks for tasks on the same day
            var getResponseForB = await clientB.GetAsync($"api/tasks/day?date={date:yyyy-MM-dd}");
            getResponseForB.EnsureSuccessStatusCode();

            var tasksForUserB =
                await getResponseForB.Content.ReadFromJsonAsync<IReadOnlyList<TaskDto>>();

            // Assert: user B should not see user A's tasks
            tasksForUserB.Should().NotBeNull();
            tasksForUserB!.Should().BeEmpty();
        }

    }
}
