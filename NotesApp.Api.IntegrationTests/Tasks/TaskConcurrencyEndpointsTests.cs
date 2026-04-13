using FluentAssertions;
using NotesApp.Api.IntegrationTests.Infrastructure.Hosting;
using NotesApp.Api.IntegrationTests.Infrastructure.Http;
using NotesApp.Application.Tasks.Models;
using System;
using System.Net;
using System.Net.Http.Json;

namespace NotesApp.Api.IntegrationTests.Tasks
{
    /// <summary>
    /// Integration tests verifying that stale RowVersion values on PUT/PATCH/DELETE
    /// are rejected with 409 Conflict (web optimistic concurrency protection).
    /// </summary>
    public sealed class TaskConcurrencyEndpointsTests : IClassFixture<NotesAppApiFactory>
    {
        private readonly NotesAppApiFactory _factory;

        public TaskConcurrencyEndpointsTests(NotesAppApiFactory factory)
        {
            _factory = factory;
        }

        // -----------------------------------------------------------------------
        // UpdateTask (PUT)
        // -----------------------------------------------------------------------

        [Fact]
        public async Task UpdateTask_WithCorrectRowVersion_Succeeds()
        {
            // Arrange: create a task
            var client = _factory.CreateClientAsUser(Guid.NewGuid());
            var created = await CreateTaskAsync(client);

            var updatePayload = new
            {
                Date = created.Date,
                Title = "Updated title",
                RowVersion = created.RowVersion // fresh RowVersion
            };

            // Act
            var response = await client.PutAsJsonAsync($"/api/tasks/{created.TaskId}", updatePayload);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        [Fact]
        public async Task UpdateTask_WithStaleRowVersion_Returns409()
        {
            // Arrange: create and immediately update once (advances the RowVersion)
            var client = _factory.CreateClientAsUser(Guid.NewGuid());
            var created = await CreateTaskAsync(client);
            var staleRowVersion = created.RowVersion;

            // First update — consumes the RowVersion
            var firstUpdate = await client.PutAsJsonAsync($"/api/tasks/{created.TaskId}", new
            {
                Date = created.Date,
                Title = "First update",
                RowVersion = staleRowVersion
            });
            firstUpdate.EnsureSuccessStatusCode();

            // Act: second update with the now-stale RowVersion
            var response = await client.PutAsJsonAsync($"/api/tasks/{created.TaskId}", new
            {
                Date = created.Date,
                Title = "Second update (stale)",
                RowVersion = staleRowVersion
            });

            // Assert: 409 Conflict
            response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        }

        // -----------------------------------------------------------------------
        // DeleteTask (DELETE)
        // -----------------------------------------------------------------------

        [Fact]
        public async Task DeleteTask_WithCorrectRowVersion_Succeeds()
        {
            // Arrange
            var client = _factory.CreateClientAsUser(Guid.NewGuid());
            var created = await CreateTaskAsync(client);

            // Act
            var response = await client.DeleteAsJsonAsync(
                $"/api/tasks/{created.TaskId}",
                new { RowVersion = created.RowVersion });

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        [Fact]
        public async Task DeleteTask_WithStaleRowVersion_Returns409()
        {
            // Arrange: create, update (advances RowVersion), then try to delete with original RowVersion
            var client = _factory.CreateClientAsUser(Guid.NewGuid());
            var created = await CreateTaskAsync(client);
            var staleRowVersion = created.RowVersion;

            // Update to advance the RowVersion
            var updateResponse = await client.PutAsJsonAsync($"/api/tasks/{created.TaskId}", new
            {
                Date = created.Date,
                Title = "Updated title",
                RowVersion = staleRowVersion
            });
            updateResponse.EnsureSuccessStatusCode();

            // Act: delete with stale RowVersion
            var response = await client.DeleteAsJsonAsync(
                $"/api/tasks/{created.TaskId}",
                new { RowVersion = staleRowVersion });

            // Assert: 409 Conflict
            response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        }

        // -----------------------------------------------------------------------
        // SetTaskCompletion (PATCH)
        // -----------------------------------------------------------------------

        [Fact]
        public async Task SetTaskCompletion_WithStaleRowVersion_Returns409()
        {
            // Arrange: create, mark completed (advances RowVersion), then use original RowVersion
            var client = _factory.CreateClientAsUser(Guid.NewGuid());
            var created = await CreateTaskAsync(client);
            var staleRowVersion = created.RowVersion;

            // Mark completed — advances RowVersion
            var completeResponse = await client.PatchAsJsonAsync(
                $"/api/tasks/{created.TaskId}/completion",
                new { IsCompleted = true, RowVersion = staleRowVersion });
            completeResponse.EnsureSuccessStatusCode();

            // Act: try to mark pending with the stale RowVersion
            var response = await client.PatchAsJsonAsync(
                $"/api/tasks/{created.TaskId}/completion",
                new { IsCompleted = false, RowVersion = staleRowVersion });

            // Assert: 409 Conflict
            response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        }

        // -----------------------------------------------------------------------
        // Helpers
        // -----------------------------------------------------------------------

        private static async Task<TaskDetailDto> CreateTaskAsync(HttpClient client)
        {
            var payload = new
            {
                Date = new DateOnly(2025, 11, 10),
                Title = "Concurrency test task"
            };

            var response = await client.PostAsJsonAsync("/api/tasks", payload);
            response.EnsureSuccessStatusCode();

            var dto = await response.Content.ReadFromJsonAsync<TaskDetailDto>();
            dto.Should().NotBeNull();
            return dto!;
        }
    }
}
