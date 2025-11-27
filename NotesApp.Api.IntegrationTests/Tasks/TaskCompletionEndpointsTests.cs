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
    /// Integration tests for task completion via PATCH /api/tasks/{id}/completion.
    /// </summary>
    public sealed class TaskCompletionEndpointsTests : IClassFixture<NotesAppApiFactory>
    {
        private readonly NotesAppApiFactory _factory;

        public TaskCompletionEndpointsTests(NotesAppApiFactory factory)
        {
            _factory = factory;
        }

        [Fact]
        public async Task Can_mark_task_completed_and_it_reflects_in_detail_and_day_summary()
        {
            // Arrange
            var client = _factory.CreateClientAsDefaultUser();
            var date = new DateOnly(2025, 11, 10);

            var createPayload = new
            {
                Date = date,
                Title = "Toggle completion task",
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

            // Act 1: Mark as completed
            var completionPayload = new { IsCompleted = true };

            var completionResponse =
                await client.PatchAsJsonAsync($"/api/tasks/{taskId}/completion", completionPayload);

            completionResponse.StatusCode.Should().Be(HttpStatusCode.OK);

            var completedDetail = await completionResponse.Content.ReadFromJsonAsync<TaskDetailDto>();
            completedDetail.Should().NotBeNull();
            completedDetail!.TaskId.Should().Be(taskId);
            completedDetail.IsCompleted.Should().BeTrue();

            // Verify day summary reflects completion
            var dayResponse = await client.GetAsync($"/api/tasks/day?date={date:yyyy-MM-dd}");
            dayResponse.StatusCode.Should().Be(HttpStatusCode.OK);

            var summaries =
                await dayResponse.Content.ReadFromJsonAsync<IReadOnlyList<TaskSummaryDto>>();

            summaries.Should().NotBeNull();
            summaries!.Should().ContainSingle(s => s.TaskId == taskId);

            summaries.Single(s => s.TaskId == taskId).IsCompleted.Should().BeTrue();
        }

        [Fact]
        public async Task Can_mark_task_back_to_pending_and_operation_is_idempotent()
        {
            // Arrange
            var client = _factory.CreateClientAsDefaultUser();
            var date = new DateOnly(2025, 11, 10);

            var createPayload = new
            {
                Date = date,
                Title = "Idempotent completion toggling",
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

            // First set completed
            var completePayload = new { IsCompleted = true };
            var completeResponse =
                await client.PatchAsJsonAsync($"/api/tasks/{taskId}/completion", completePayload);

            completeResponse.StatusCode.Should().Be(HttpStatusCode.OK);

            // Act: mark pending twice
            var pendingPayload = new { IsCompleted = false };

            var pendingResponse1 =
                await client.PatchAsJsonAsync($"/api/tasks/{taskId}/completion", pendingPayload);
            pendingResponse1.StatusCode.Should().Be(HttpStatusCode.OK);

            var pendingResponse2 =
                await client.PatchAsJsonAsync($"/api/tasks/{taskId}/completion", pendingPayload);
            pendingResponse2.StatusCode.Should().Be(HttpStatusCode.OK);

            var detail = await pendingResponse2.Content.ReadFromJsonAsync<TaskDetailDto>();
            detail.Should().NotBeNull();
            detail!.IsCompleted.Should().BeFalse();

            // Verify day summary is also pending
            var dayResponse = await client.GetAsync($"/api/tasks/day?date={date:yyyy-MM-dd}");
            dayResponse.StatusCode.Should().Be(HttpStatusCode.OK);

            var summaries =
                await dayResponse.Content.ReadFromJsonAsync<IReadOnlyList<TaskSummaryDto>>();

            summaries.Should().NotBeNull();
            summaries!.Should().ContainSingle(s => s.TaskId == taskId);

            summaries.Single(s => s.TaskId == taskId).IsCompleted.Should().BeFalse();
        }

        [Fact]
        public async Task Cannot_change_completion_of_task_belonging_to_another_user()
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
                Title = "Owner's completion task",
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

            // Act: attacker tries to set completion
            var completionPayload = new { IsCompleted = true };

            var attackerResponse =
                await attackerClient.PatchAsJsonAsync($"/api/tasks/{taskId}/completion", completionPayload);

            // Assert: 404 NotFound
            attackerResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }
    }
}
