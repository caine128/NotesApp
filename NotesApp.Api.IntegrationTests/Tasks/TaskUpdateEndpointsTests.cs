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
    /// Integration tests for updating tasks via the API:
    /// - Happy path: create -> update -> get
    /// - Security: user isolation (cannot update others' tasks)
    /// </summary>
    public sealed class TaskUpdateEndpointsTests : IClassFixture<NotesAppApiFactory>
    {
        private readonly NotesAppApiFactory _factory;

        public TaskUpdateEndpointsTests(NotesAppApiFactory factory)
        {
            _factory = factory;
        }

        [Fact]
        public async Task UpdateTask_roundtrip_succeeds_for_same_user()
        {
            // Arrange
            var userId = Guid.NewGuid();
            var client = _factory.CreateClientAsUser(userId);

            var originalDate = new DateOnly(2025, 2, 21);
            var updatedDate = new DateOnly(2025, 2, 22);

            // 1) Create an initial task
            var createPayload = new
            {
                date = originalDate,
                title = "Original title",
                reminderAtUtc = (DateTime?)null
            };

            var createResponse = await client.PostAsJsonAsync("api/tasks", createPayload);
            createResponse.StatusCode.Should().Be(HttpStatusCode.OK);

            var createdTask = await createResponse.Content.ReadFromJsonAsync<TaskDto>();
            createdTask.Should().NotBeNull();
            createdTask!.Title.Should().Be("Original title");
            createdTask.Date.Should().Be(originalDate);
            createdTask.UserId.Should().NotBe(Guid.Empty);

            // 2) Update the task (title + date + reminder)
            var updatePayload = new
            {
                date = updatedDate,
                title = "Updated title",
                reminderAtUtc = DateTime.SpecifyKind(DateTime.UtcNow.AddHours(2), DateTimeKind.Utc)
            };

            var updateResponse = await client.PutAsJsonAsync(
                $"api/tasks/{createdTask.TaskId}",
                updatePayload);

            updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);

            var updatedTask = await updateResponse.Content.ReadFromJsonAsync<TaskDto>();
            updatedTask.Should().NotBeNull();
            updatedTask!.TaskId.Should().Be(createdTask.TaskId);
            updatedTask.UserId.Should().Be(createdTask.UserId);
            updatedTask.Title.Should().Be("Updated title");
            updatedTask.Date.Should().Be(updatedDate);
            updatedTask.ReminderAtUtc.Should().Be(updatePayload.reminderAtUtc);

            // 3) Verify via GET for the new date
            var getResponse = await client.GetAsync($"api/tasks/day?date={updatedDate:yyyy-MM-dd}");
            getResponse.StatusCode.Should().Be(HttpStatusCode.OK);

            var tasksForDay =
                await getResponse.Content.ReadFromJsonAsync<IReadOnlyList<TaskDto>>();

            tasksForDay.Should().NotBeNull();
            tasksForDay!.Should().Contain(t => t.TaskId == createdTask.TaskId);

            var distinctUserIds = tasksForDay.Select(t => t.UserId).Distinct().ToList();
            distinctUserIds.Should().HaveCount(1);
            distinctUserIds[0].Should().Be(createdTask.UserId);
        }

        [Fact]
        public async Task UpdateTask_fails_for_other_user()
        {
            // Arrange
            var ownerUserId = Guid.NewGuid();
            var attackerUserId = Guid.NewGuid();

            var ownerClient = _factory.CreateClientAsUser(ownerUserId);
            var attackerClient = _factory.CreateClientAsUser(attackerUserId);

            var date = new DateOnly(2025, 2, 23);

            // 1) Owner creates a task
            var createPayload = new
            {
                date = date,
                title = "Owner's task",
                reminderAtUtc = (DateTime?)null
            };

            var createResponse = await ownerClient.PostAsJsonAsync("api/tasks", createPayload);
            createResponse.EnsureSuccessStatusCode();

            var createdTask = await createResponse.Content.ReadFromJsonAsync<TaskDto>();
            createdTask.Should().NotBeNull();

            // 2) Attacker tries to update the owner's task
            var attackerUpdatePayload = new
            {
                date = date,
                title = "Hacked title",
                reminderAtUtc = (DateTime?)null
            };

            var updateResponse = await attackerClient.PutAsJsonAsync(
                $"api/tasks/{createdTask!.TaskId}",
                attackerUpdatePayload);

            // We expect a "not found" style response to avoid information leakage.
            // Depending on your FluentResults -> HTTP mapping this may be 404 or 403.
            // Here we assume Tasks.NotFound is mapped to 404 Not Found.
            updateResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);

            // 3) Owner still sees original title
            var getResponse = await ownerClient.GetAsync($"api/tasks/day?date={date:yyyy-MM-dd}");
            getResponse.EnsureSuccessStatusCode();

            var tasksForDay =
                await getResponse.Content.ReadFromJsonAsync<IReadOnlyList<TaskDto>>();

            tasksForDay.Should().NotBeNull();
            tasksForDay!.Should().Contain(t =>
                t.TaskId == createdTask.TaskId &&
                t.Title == "Owner's task");
        }
    }
}
