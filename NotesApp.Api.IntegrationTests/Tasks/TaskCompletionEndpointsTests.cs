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
    /// Integration tests for the PATCH /api/tasks/{taskId}/completion endpoint.
    ///
    /// These tests:
    /// - Use the real API host with TestAuthHandler (no real Entra calls).
    /// - Verify that only the owner can change completion state.
    /// - Verify that IsCompleted is reflected in the day view.
    /// </summary>
    public sealed class TaskCompletionEndpointsTests : IClassFixture<NotesAppApiFactory>
    {
        private readonly NotesAppApiFactory _factory;

        public TaskCompletionEndpointsTests(NotesAppApiFactory factory)
        {
            _factory = factory;
        }

        [Fact]
        public async Task Set_completion_roundtrip_succeeds_for_owner()
        {
            // Arrange: user A and a specific date
            var userId = Guid.NewGuid();
            var client = _factory.CreateClientAsUser(userId);

            var date = new DateOnly(2025, 3, 10);

            var createPayload = new
            {
                date,
                title = "Completion test task",
                // Explicitly null reminder to keep test simple
                reminderAtUtc = (DateTime?)null
            };

            // 1) Create the task
            var createResponse = await client.PostAsJsonAsync("api/tasks", createPayload);
            createResponse.EnsureSuccessStatusCode();

            var createdTask =
                await createResponse.Content.ReadFromJsonAsync<TaskDto>();

            createdTask.Should().NotBeNull();
            createdTask!.IsCompleted.Should().BeFalse("new tasks should start as not completed");

            var taskId = createdTask.TaskId;

            // 2) Set completion to true via PATCH
            var completionPayload = new { isCompleted = true };

            using var patchRequest = new HttpRequestMessage(
                HttpMethod.Patch,
                $"api/tasks/{taskId}/completion")
            {
                Content = JsonContent.Create(completionPayload)
            };

            var completionResponse = await client.SendAsync(patchRequest);
            completionResponse.StatusCode.Should().Be(HttpStatusCode.OK);

            var updatedTask =
                await completionResponse.Content.ReadFromJsonAsync<TaskDto>();

            updatedTask.Should().NotBeNull();
            updatedTask!.TaskId.Should().Be(taskId);
            updatedTask.IsCompleted.Should().BeTrue("after PATCH IsCompleted=true, the task should be completed");

            // 3) Verify via GET day that the task appears as completed
            var getResponse =
                await client.GetAsync($"api/tasks/day?date={date:yyyy-MM-dd}");

            getResponse.EnsureSuccessStatusCode();

            var tasksForDay =
                await getResponse.Content.ReadFromJsonAsync<IReadOnlyList<TaskDto>>();

            tasksForDay.Should().NotBeNull();
            tasksForDay!
                .Should()
                .Contain(t => t.TaskId == taskId && t.IsCompleted);
        }

        [Fact]
        public async Task Set_completion_fails_for_other_user()
        {
            // Arrange: owner and another user
            var ownerId = Guid.NewGuid();
            var otherUserId = Guid.NewGuid();

            var clientOwner = _factory.CreateClientAsUser(ownerId);
            var clientOther = _factory.CreateClientAsUser(otherUserId);

            var date = new DateOnly(2025, 3, 11);

            var createPayload = new
            {
                date,
                title = "Other user completion test",
                reminderAtUtc = (DateTime?)null
            };

            // Owner creates the task
            var createResponse = await clientOwner.PostAsJsonAsync("api/tasks", createPayload);
            createResponse.EnsureSuccessStatusCode();

            var createdTask =
                await createResponse.Content.ReadFromJsonAsync<TaskDto>();

            createdTask.Should().NotBeNull();
            var taskId = createdTask!.TaskId;

            // Act: other user tries to set completion
            var completionPayload = new { isCompleted = true };

            using var patchRequest = new HttpRequestMessage(
                HttpMethod.Patch,
                $"api/tasks/{taskId}/completion")
            {
                Content = JsonContent.Create(completionPayload)
            };

            var completionResponse = await clientOther.SendAsync(patchRequest);

            // Assert: we respond with NotFound (consistent with Update/Delete behavior)
            completionResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }
    }
}
