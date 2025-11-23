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
    /// Integration tests for deleting tasks via the API:
    /// - Happy path: owner deletes their own task.
    /// - Security: another user cannot delete someone else's task.
    /// </summary>
    public sealed class TaskDeleteEndpointsTests : IClassFixture<NotesAppApiFactory>
    {
        private readonly NotesAppApiFactory _factory;

        public TaskDeleteEndpointsTests(NotesAppApiFactory factory)
        {
            _factory = factory;
        }

        [Fact]
        public async Task DeleteTask_succeeds_for_owner_and_task_disappears_from_day_view()
        {
            // Arrange
            var userId = Guid.NewGuid();
            var client = _factory.CreateClientAsUser(userId);

            var date = new DateOnly(2025, 2, 25);

            // 1) Create a task for that day
            var createPayload = new
            {
                date = date,
                title = "Task to be deleted",
                reminderAtUtc = (DateTime?)null
            };

            var createResponse = await client.PostAsJsonAsync("api/tasks", createPayload);
            createResponse.StatusCode.Should().Be(HttpStatusCode.OK);

            var createdTask = await createResponse.Content.ReadFromJsonAsync<TaskDto>();
            createdTask.Should().NotBeNull();
            createdTask!.TaskId.Should().NotBe(Guid.Empty);
            createdTask.UserId.Should().NotBe(Guid.Empty);

            // 2) Delete the task
            var deleteResponse = await client.DeleteAsync($"api/tasks/{createdTask.TaskId}");
            // FluentResults.Extensions.AspNetCore maps a successful Result to 200 OK by default.
            // 200 and 204 are both valid for DELETE, but we align with the library.
            deleteResponse.StatusCode.Should().Be(HttpStatusCode.OK);

            // 3) Verify: GET for that day should not include the task anymore
            var getResponse = await client.GetAsync($"api/tasks/day?date={date:yyyy-MM-dd}");
            getResponse.StatusCode.Should().Be(HttpStatusCode.OK);

            var tasksForDay =
                await getResponse.Content.ReadFromJsonAsync<IReadOnlyList<TaskDto>>();

            tasksForDay.Should().NotBeNull();
            tasksForDay!.Should().NotContain(t => t.TaskId == createdTask.TaskId);
        }

        [Fact]
        public async Task DeleteTask_fails_with_not_found_for_other_user()
        {
            // Arrange
            var ownerUserId = Guid.NewGuid();
            var attackerUserId = Guid.NewGuid();

            var ownerClient = _factory.CreateClientAsUser(ownerUserId);
            var attackerClient = _factory.CreateClientAsUser(attackerUserId);

            var date = new DateOnly(2025, 2, 26);

            // 1) Owner creates a task
            var createPayload = new
            {
                date = date,
                title = "Owner-only task",
                reminderAtUtc = (DateTime?)null
            };

            var createResponse = await ownerClient.PostAsJsonAsync("api/tasks", createPayload);
            createResponse.EnsureSuccessStatusCode();

            var createdTask = await createResponse.Content.ReadFromJsonAsync<TaskDto>();
            createdTask.Should().NotBeNull();

            // 2) Attacker tries to delete owner's task
            var deleteResponse = await attackerClient.DeleteAsync($"api/tasks/{createdTask!.TaskId}");

            // According to our ResultEndpointProfile, Tasks.NotFound -> 404
            deleteResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);

            // 3) Owner still sees the task in their day view
            var getResponse = await ownerClient.GetAsync($"api/tasks/day?date={date:yyyy-MM-dd}");
            getResponse.EnsureSuccessStatusCode();

            var tasksForDay =
                await getResponse.Content.ReadFromJsonAsync<IReadOnlyList<TaskDto>>();

            tasksForDay.Should().NotBeNull();
            tasksForDay!.Should().Contain(t =>
                t.TaskId == createdTask.TaskId &&
                t.Title == "Owner-only task");
        }
    }
}
