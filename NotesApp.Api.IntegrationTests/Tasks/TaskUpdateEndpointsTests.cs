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
    /// Integration tests for updating tasks via PUT /api/tasks/{id}.
    /// </summary>
    public sealed class TaskUpdateEndpointsTests : IClassFixture<NotesAppApiFactory>
    {
        private readonly NotesAppApiFactory _factory;

        public TaskUpdateEndpointsTests(NotesAppApiFactory factory)
        {
            _factory = factory;
        }

        [Fact]
        public async Task Update_existing_task_updates_all_mutable_fields()
        {
            // Arrange: create client + initial task
            var client = _factory.CreateClientAsDefaultUser();

            var originalDate = new DateOnly(2025, 11, 10);

            var createPayload = new
            {
                Date = originalDate,
                Title = "Original title",
                Description = "Original description",
                StartTime = new TimeOnly(9, 0),
                EndTime = new TimeOnly(10, 0),
                Location = "Old location",
                TravelTime = TimeSpan.FromMinutes(10),
                ReminderAtUtc = DateTime.UtcNow.AddHours(1)
            };

            var createResponse = await client.PostAsJsonAsync("/api/tasks", createPayload);
            createResponse.EnsureSuccessStatusCode();

            var created = await createResponse.Content.ReadFromJsonAsync<TaskDetailDto>();
            created.Should().NotBeNull();

            var taskId = created!.TaskId;

            // Prepare update payload
            var newDate = originalDate.AddDays(1);

            var updatePayload = new
            {
                Date = newDate,
                Title = "Updated title",
                Description = "Updated description",
                StartTime = new TimeOnly(14, 0),
                EndTime = new TimeOnly(16, 0),
                Location = "New location",
                TravelTime = TimeSpan.FromMinutes(25),
                ReminderAtUtc = DateTime.UtcNow.AddHours(2)
            };

            // Act: PUT /api/tasks/{id}
            var updateResponse = await client.PutAsJsonAsync($"/api/tasks/{taskId}", updatePayload);

            // Assert: 200 + updated TaskDetailDto
            updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);

            var updated = await updateResponse.Content.ReadFromJsonAsync<TaskDetailDto>();
            updated.Should().NotBeNull();
            updated!.TaskId.Should().Be(taskId);
            updated.Title.Should().Be(updatePayload.Title);
            updated.Description.Should().Be(updatePayload.Description);
            updated.Date.Should().Be(newDate);
            updated.StartTime.Should().Be(updatePayload.StartTime);
            updated.EndTime.Should().Be(updatePayload.EndTime);
            updated.Location.Should().Be(updatePayload.Location);
            updated.TravelTime.Should().Be(updatePayload.TravelTime);

            updated.ReminderAtUtc.Should().NotBeNull();
            updated.ReminderAtUtc!.Value.Should()
                .BeCloseTo(updatePayload.ReminderAtUtc, TimeSpan.FromSeconds(5));

            // Also verify /day summaries reflect the new date & title
            var dayResponse = await client.GetAsync($"/api/tasks/day?date={newDate:yyyy-MM-dd}");
            dayResponse.StatusCode.Should().Be(HttpStatusCode.OK);

            var summaries =
                await dayResponse.Content.ReadFromJsonAsync<IReadOnlyList<TaskSummaryDto>>();

            summaries.Should().NotBeNull();
            summaries!.Should().ContainSingle(s => s.TaskId == taskId);
        }

        [Fact]
        public async Task Updating_nonexistent_task_returns_not_found()
        {
            // Arrange
            var client = _factory.CreateClientAsDefaultUser();
            var nonExistentTaskId = Guid.NewGuid();

            var date = new DateOnly(2025, 11, 10);

            var updatePayload = new
            {
                Date = date,
                Title = "Updated title",
                Description = "Some updated description",
                StartTime = (TimeOnly?)null,
                EndTime = (TimeOnly?)null,
                Location = (string?)null,
                TravelTime = (TimeSpan?)null,
                ReminderAtUtc = (DateTime?)null
            };

            // Act
            var response = await client.PutAsJsonAsync(
                $"/api/tasks/{nonExistentTaskId}",
                updatePayload);

            // Assert: controller should map "not found" to 404
            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }


        [Fact]
        public async Task Update_with_empty_title_returns_bad_request()
        {
            // Arrange
            var client = _factory.CreateClientAsDefaultUser();
            var date = new DateOnly(2025, 11, 10);

            var createPayload = new
            {
                Date = date,
                Title = "Valid title",
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

            // Act: try to update with empty title -> should fail validation
            var invalidUpdatePayload = new
            {
                Date = date,
                Title = "   ", // invalid
                Description = (string?)null,
                StartTime = (TimeOnly?)null,
                EndTime = (TimeOnly?)null,
                Location = (string?)null,
                TravelTime = (TimeSpan?)null,
                ReminderAtUtc = (DateTime?)null
            };

            var updateResponse = await client.PutAsJsonAsync($"/api/tasks/{taskId}", invalidUpdatePayload);

            // Assert
            updateResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }

        [Fact]
        public async Task Cannot_update_task_belonging_to_another_user()
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

            // Owner creates the task
            var createResponse = await ownerClient.PostAsJsonAsync("/api/tasks", createPayload);
            createResponse.EnsureSuccessStatusCode();

            var created = await createResponse.Content.ReadFromJsonAsync<TaskDetailDto>();
            created.Should().NotBeNull();

            var taskId = created!.TaskId;

            // Act: attacker tries to update someone else's task
            var attackerUpdatePayload = new
            {
                Date = date,
                Title = "Attacker update",
                Description = "Should not be allowed",
                StartTime = (TimeOnly?)null,
                EndTime = (TimeOnly?)null,
                Location = (string?)null,
                TravelTime = (TimeSpan?)null,
                ReminderAtUtc = (DateTime?)null
            };

            var attackerUpdateResponse =
                await attackerClient.PutAsJsonAsync($"/api/tasks/{taskId}", attackerUpdatePayload);

            // Assert: controller maps "not owned" to 404 NotFound
            attackerUpdateResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }
    }
}
