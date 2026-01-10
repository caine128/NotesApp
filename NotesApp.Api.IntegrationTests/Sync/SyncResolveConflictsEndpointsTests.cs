using FluentAssertions;
using NotesApp.Api.IntegrationTests.Infrastructure.Hosting;
using NotesApp.Application.Sync.Models;
using NotesApp.Application.Tasks.Models;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http.Json;
using System.Text;

namespace NotesApp.Api.IntegrationTests.Sync
{
    public sealed class SyncResolveConflictsEndpointsTests : IClassFixture<NotesAppApiFactory>
    {
        private readonly NotesAppApiFactory _factory;

        public SyncResolveConflictsEndpointsTests(NotesAppApiFactory factory)
        {
            _factory = factory;
        }

        [Fact]
        public async Task Resolve_conflicts_keep_server_for_task_returns_kept_server()
        {
            // Arrange: create task normally
            var client = _factory.CreateClientAsUser(Guid.NewGuid());
            var date = new DateOnly(2025, 11, 10);

            var createPayload = new
            {
                Date = date,
                Title = "Conflict task",
                Description = "Original",
                StartTime = (TimeOnly?)null,
                EndTime = (TimeOnly?)null,
                Location = (string?)null,
                TravelTime = (TimeSpan?)null,
                ReminderAtUtc = (DateTime?)null
            };

            var createResponse = await client.PostAsJsonAsync("/api/tasks", createPayload);
            createResponse.StatusCode.Should().Be(HttpStatusCode.Created);

            var taskDetail = await createResponse.Content.ReadFromJsonAsync<TaskDetailDto>();
            taskDetail.Should().NotBeNull();

            // For a freshly created task, we know Version = 1 on the server.
            // Build resolution payload choosing keep_server.
            var request = new ResolveSyncConflictsRequestDto
            {
                Resolutions = new[]
                {
                    new SyncConflictResolutionDto
                    {
                        EntityType = SyncEntityType.Task,
                        EntityId = taskDetail!.TaskId,
                        Choice = SyncResolutionChoice.KeepServer,
                        ExpectedVersion = 1,
                        TaskData = null
                    }
                }
            };

            // Act
            var response = await client.PostAsJsonAsync("/api/sync/resolve-conflicts", request);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.OK);

            var result = await response.Content.ReadFromJsonAsync<ResolveSyncConflictsResultDto>();
            result.Should().NotBeNull();

            var item = result!.Results.Should().ContainSingle(r =>
                    r.EntityType == SyncEntityType.Task &&
                    r.EntityId == taskDetail.TaskId &&
                    r.Status == SyncConflictResolutionStatus.KeptServer)
                .Subject;

            item.NewVersion.Should().NotBeNull();
            item.NewVersion.Should().Be(1);
        }

        [Fact]
        public async Task Resolve_conflicts_keep_client_updates_task_when_versions_match()
        {
            // Arrange: create task, then resolve with keep_client and new title
            var client = _factory.CreateClientAsUser(Guid.NewGuid());
            var date = new DateOnly(2025, 11, 10);

            var createPayload = new
            {
                Date = date,
                Title = "Conflict task",
                Description = "Original",
                StartTime = (TimeOnly?)null,
                EndTime = (TimeOnly?)null,
                Location = (string?)null,
                TravelTime = (TimeSpan?)null,
                ReminderAtUtc = (DateTime?)null
            };

            var createResponse = await client.PostAsJsonAsync("/api/tasks", createPayload);
            createResponse.StatusCode.Should().Be(HttpStatusCode.Created);

            var taskDetail = await createResponse.Content.ReadFromJsonAsync<TaskDetailDto>();
            taskDetail.Should().NotBeNull();

            var request = new ResolveSyncConflictsRequestDto
            {
                Resolutions = new[]
                {
                    new SyncConflictResolutionDto
                    {
                        EntityType = SyncEntityType.Task,
                        EntityId = taskDetail!.TaskId,
                        Choice = SyncResolutionChoice.KeepClient,
                        ExpectedVersion = 1, // initial version
                        TaskData = new TaskConflictResolutionDataDto
                        {
                            Date = taskDetail.Date,
                            Title = "Resolved title",
                            Description = taskDetail.Description,
                            StartTime = taskDetail.StartTime,
                            EndTime = taskDetail.EndTime,
                            Location = taskDetail.Location,
                            TravelTime = taskDetail.TravelTime,
                            ReminderAtUtc = taskDetail.ReminderAtUtc
                        }
                    }
                }
            };

            // Act
            var response = await client.PostAsJsonAsync("/api/sync/resolve-conflicts", request);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.OK);

            var result = await response.Content.ReadFromJsonAsync<ResolveSyncConflictsResultDto>();
            result.Should().NotBeNull();

            var item = result!.Results.Should().ContainSingle(r =>
                    r.EntityType == SyncEntityType.Task &&
                    r.EntityId == taskDetail.TaskId &&
                    r.Status == SyncConflictResolutionStatus.Updated)
                .Subject;

            item.NewVersion.Should().NotBeNull();
            item.NewVersion.Should().BeGreaterThan(1);

            // Verify via /api/tasks/{id}
            var detailResponse = await client.GetAsync($"/api/tasks/{taskDetail.TaskId}");
            detailResponse.StatusCode.Should().Be(HttpStatusCode.OK);

            var updatedDetail = await detailResponse.Content.ReadFromJsonAsync<TaskDetailDto>();
            updatedDetail.Should().NotBeNull();
            updatedDetail!.Title.Should().Be("Resolved title");
        }
    }
}
