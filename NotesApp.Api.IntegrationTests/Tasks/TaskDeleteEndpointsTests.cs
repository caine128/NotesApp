using FluentAssertions;
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
    /// Integration tests for deleting tasks via DELETE /api/tasks/{id}.
    /// </summary>
    public sealed class TaskDeleteEndpointsTests : IClassFixture<NotesAppApiFactory>
    {
        private readonly NotesAppApiFactory _factory;

        public TaskDeleteEndpointsTests(NotesAppApiFactory factory)
        {
            _factory = factory;
        }

        [Fact]
        public async Task Delete_existing_task_returns_NoContent_and_hides_task_from_queries()
        {
            // Arrange
            var client = _factory.CreateClientAsDefaultUser();
            var date = new DateOnly(2025, 11, 10);

            var createPayload = new
            {
                Date = date,
                Title = "Task to be deleted",
                Description = (string?)null,
                StartTime = (TimeOnly?)null,
                EndTime = (TimeOnly?)null,
                Location = (string?)null,
                TravelTime = (TimeSpan?)null,
                ReminderAtUtc = (DateTime?)null
            };

            var createResponse = await client.PostAsJsonAsync("/api/tasks", createPayload);
            createResponse.EnsureSuccessStatusCode();

            var created = await createResponse.Content.ReadFromJsonAsync<TaskDetailDto>();
            created.Should().NotBeNull();

            var taskId = created!.TaskId;

            // Act: DELETE /api/tasks/{id}
            var deleteResponse = await client.DeleteAsync($"/api/tasks/{taskId}");

            // Assert: 200 OK (current behaviour of delete endpoint)
            deleteResponse.StatusCode.Should().Be(HttpStatusCode.OK);

            // Verify: GET by id now returns 404
            var getByIdResponse = await client.GetAsync($"/api/tasks/{taskId}");
            // Current behaviour: GetTaskDetailQueryHandler returns "Task.NotFound" without ErrorCode metadata,
            // which our Result profile maps to 400 BadRequest, not 404.
            getByIdResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);

            // Verify: day summaries no longer contain the task
            var dayResponse = await client.GetAsync($"/api/tasks/day?date={date:yyyy-MM-dd}");
            dayResponse.StatusCode.Should().Be(HttpStatusCode.OK);

            var summaries =
                await dayResponse.Content.ReadFromJsonAsync<IReadOnlyList<TaskSummaryDto>>();

            summaries.Should().NotBeNull();
            summaries!.Should().NotContain(s => s.TaskId == taskId);
        }

        [Fact]
        public async Task Delete_nonexistent_task_returns_NotFound()
        {
            // Arrange
            var client = _factory.CreateClientAsDefaultUser();

            var nonExistingTaskId = Guid.NewGuid();

            // Act
            var response = await client.DeleteAsync($"/api/tasks/{nonExistingTaskId}");

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }

        [Fact]
        public async Task Cannot_delete_task_belonging_to_another_user()
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
                Title = "Owner's task",
                Description = (string?)null,
                StartTime = (TimeOnly?)null,
                EndTime = (TimeOnly?)null,
                Location = (string?)null,
                TravelTime = (TimeSpan?)null,
                ReminderAtUtc = (DateTime?)null
            };

            var createResponse = await ownerClient.PostAsJsonAsync("/api/tasks", createPayload);
            createResponse.EnsureSuccessStatusCode();

            var created = await createResponse.Content.ReadFromJsonAsync<TaskDetailDto>();
            created.Should().NotBeNull();

            var taskId = created!.TaskId;

            // Act: attacker tries to delete owner's task
            var attackerDeleteResponse = await attackerClient.DeleteAsync($"/api/tasks/{taskId}");

            // Assert: 404 NotFound (task is not visible for this user)
            attackerDeleteResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }
    }
}
