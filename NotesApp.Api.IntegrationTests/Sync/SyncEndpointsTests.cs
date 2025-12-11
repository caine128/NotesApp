using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using NotesApp.Api.DeviceProvisioning;
using NotesApp.Api.IntegrationTests.Infrastructure.Hosting;
using NotesApp.Application.Devices.Commands.RegisterDevice;
using NotesApp.Application.Devices.Models;
using NotesApp.Application.Notes.Models;
using NotesApp.Application.Sync.Models;
using NotesApp.Application.Tasks.Models;
using NotesApp.Domain.Users;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http.Json;
using System.Text;

namespace NotesApp.Api.IntegrationTests.Sync
{
    /// <summary>
    /// End-to-end tests for the sync pull endpoint:
    /// - GET /api/sync/changes
    /// </summary>
    public sealed class SyncEndpointsTests : IClassFixture<NotesAppApiFactory>
    {
        private readonly NotesAppApiFactory _factory;

        public SyncEndpointsTests(NotesAppApiFactory factory)
        {
            _factory = factory;
        }

        [Fact]
        public async Task Get_changes_initial_returns_empty_when_no_data()
        {
            // Arrange
            var client = _factory.CreateClientAsUser(Guid.NewGuid());

            // Act
            var response = await client.GetAsync("/api/sync/changes");

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.OK);

            var dto = await response.Content.ReadFromJsonAsync<SyncChangesDto>();
            dto.Should().NotBeNull();

            dto!.Tasks.Created.Should().BeEmpty();
            dto.Tasks.Updated.Should().BeEmpty();
            dto.Tasks.Deleted.Should().BeEmpty();

            dto.Notes.Created.Should().BeEmpty();
            dto.Notes.Updated.Should().BeEmpty();
            dto.Notes.Deleted.Should().BeEmpty();

            dto.ServerTimestampUtc.Should().NotBe(default);
        }

        [Fact]
        public async Task Get_changes_initial_returns_created_tasks_and_notes()
        {
            // Arrange
            var client = _factory.CreateClientAsUser(Guid.NewGuid());
            var date = new DateOnly(2025, 11, 10);

            // Create a task
            var createTaskPayload = new
            {
                Date = date,
                Title = "Sync test task",
                Description = "Created before initial sync",
                StartTime = (TimeOnly?)null,
                EndTime = (TimeOnly?)null,
                Location = "Office",
                TravelTime = (TimeSpan?)null,
                ReminderAtUtc = (DateTime?)null
            };

            var createTaskResponse = await client.PostAsJsonAsync("/api/tasks", createTaskPayload);
            createTaskResponse.StatusCode.Should().Be(HttpStatusCode.Created);

            var taskDto = await createTaskResponse.Content.ReadFromJsonAsync<TaskDetailDto>();
            taskDto.Should().NotBeNull();

            // Create a note
            var createNotePayload = new
            {
                Date = date,
                Title = "Sync test note",
                Content = "Note created before initial sync",
                Summary = "Summary",
                Tags = "sync,test"
            };

            var createNoteResponse = await client.PostAsJsonAsync("/api/notes", createNotePayload);
            createNoteResponse.StatusCode.Should().Be(HttpStatusCode.Created);

            var noteDto = await createNoteResponse.Content.ReadFromJsonAsync<NoteDetailDto>();
            noteDto.Should().NotBeNull();

            // Act: initial sync (since = null)
            var syncResponse = await client.GetAsync("/api/sync/changes");

            // Assert
            syncResponse.StatusCode.Should().Be(HttpStatusCode.OK);

            var syncDto = await syncResponse.Content.ReadFromJsonAsync<SyncChangesDto>();
            syncDto.Should().NotBeNull();

            syncDto!.Tasks.Created.Should().ContainSingle(t => t.Id == taskDto!.TaskId);
            syncDto.Tasks.Updated.Should().BeEmpty();
            syncDto.Tasks.Deleted.Should().BeEmpty();

            syncDto.Notes.Created.Should().ContainSingle(n => n.Id == noteDto!.NoteId);
            syncDto.Notes.Updated.Should().BeEmpty();
            syncDto.Notes.Deleted.Should().BeEmpty();
        }

        [Fact]
        public async Task Get_changes_incremental_returns_only_changes_since_timestamp()
        {
            // Arrange
            var client = _factory.CreateClientAsUser(Guid.NewGuid());
            var date = new DateOnly(2025, 11, 10);

            // Create an initial task and note, then perform an initial sync
            var initialTaskPayload = new
            {
                Date = date,
                Title = "Initial task",
                Description = "Created before first sync",
                StartTime = (TimeOnly?)null,
                EndTime = (TimeOnly?)null,
                Location = "Office",
                TravelTime = (TimeSpan?)null,
                ReminderAtUtc = (DateTime?)null
            };

            var initialTaskResponse = await client.PostAsJsonAsync("/api/tasks", initialTaskPayload);
            initialTaskResponse.StatusCode.Should().Be(HttpStatusCode.Created);

            var initialNotePayload = new
            {
                Date = date,
                Title = "Initial note",
                Content = "Created before first sync",
                Summary = "Summary",
                Tags = "sync,test"
            };

            var initialNoteResponse = await client.PostAsJsonAsync("/api/notes", initialNotePayload);
            initialNoteResponse.StatusCode.Should().Be(HttpStatusCode.Created);

            var initialSyncResponse = await client.GetAsync("/api/sync/changes");
            initialSyncResponse.StatusCode.Should().Be(HttpStatusCode.OK);

            var initialSyncDto = await initialSyncResponse.Content.ReadFromJsonAsync<SyncChangesDto>();
            initialSyncDto.Should().NotBeNull();

            var since = initialSyncDto!.ServerTimestampUtc;

            // Create a new task and a new note after the initial sync
            var newTaskPayload = new
            {
                Date = date,
                Title = "New task after sync",
                Description = "Should appear as created in incremental sync",
                StartTime = (TimeOnly?)null,
                EndTime = (TimeOnly?)null,
                Location = "Office",
                TravelTime = (TimeSpan?)null,
                ReminderAtUtc = (DateTime?)null
            };

            var newTaskResponse = await client.PostAsJsonAsync("/api/tasks", newTaskPayload);
            newTaskResponse.StatusCode.Should().Be(HttpStatusCode.Created);
            var newTaskDto = await newTaskResponse.Content.ReadFromJsonAsync<TaskDetailDto>();
            newTaskDto.Should().NotBeNull();

            var newNotePayload = new
            {
                Date = date,
                Title = "New note after sync",
                Content = "Should appear as created in incremental sync",
                Summary = "Summary",
                Tags = "sync,test"
            };

            var newNoteResponse = await client.PostAsJsonAsync("/api/notes", newNotePayload);
            newNoteResponse.StatusCode.Should().Be(HttpStatusCode.Created);
            var newNoteDto = await newNoteResponse.Content.ReadFromJsonAsync<NoteDetailDto>();
            newNoteDto.Should().NotBeNull();

            // Act: incremental sync with since = previous server timestamp (ISO 8601)
            var sinceParam = Uri.EscapeDataString(since.ToString("O"));
            var incrementalResponse = await client.GetAsync($"/api/sync/changes?sinceUtc={sinceParam}");
            incrementalResponse.StatusCode.Should().Be(HttpStatusCode.OK);

            var incrementalDto = await incrementalResponse.Content.ReadFromJsonAsync<SyncChangesDto>();
            incrementalDto.Should().NotBeNull();

            // Assert: only the new task and note are in the "created" buckets
            incrementalDto!.Tasks.Created.Should().ContainSingle(t => t.Id == newTaskDto!.TaskId);
            incrementalDto.Tasks.Updated.Should().BeEmpty();
            incrementalDto.Tasks.Deleted.Should().BeEmpty();

            incrementalDto.Notes.Created.Should().ContainSingle(n => n.Id == newNoteDto!.NoteId);
            incrementalDto.Notes.Updated.Should().BeEmpty();
            incrementalDto.Notes.Deleted.Should().BeEmpty();
        }

        [Fact]
        public async Task Push_with_created_task_and_note_persists_and_returns_mapping()
        {
            // Arrange
            var client = _factory.CreateClientAsUser(Guid.NewGuid());
            // NEW: register a real device for this user
            var deviceId = await RegisterTestDeviceAsync(client);
            var now = DateTime.UtcNow;

            var payload = new SyncPushCommandPayloadDto
            {
                DeviceId = deviceId,                    // use the registered device
                ClientSyncTimestampUtc = now,
                Tasks = new SyncPushTasksDto
                {
                    Created = new[]
            {
                new TaskCreatedPushItemDto
                {
                    ClientId = Guid.NewGuid(),
                    Date = new DateOnly(2025, 11, 10),
                    Title = "Pushed task",
                    Description = "Created via sync push"
                }
            }
                },
                Notes = new SyncPushNotesDto
                {
                    Created = new[]
            {
                new NoteCreatedPushItemDto
                {
                    ClientId = Guid.NewGuid(),
                    Date = new DateOnly(2025, 11, 10),
                    Title = "Pushed note",
                    Content = "Created via sync push"
                }
            }
                }
            };

            // Act
            var response = await client.PostAsJsonAsync("/api/sync/push", payload);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.OK);

            var result = await response.Content.ReadFromJsonAsync<SyncPushResultDto>();
            result.Should().NotBeNull();

            result!.Tasks.Created.Should().HaveCount(1);
            result.Tasks.Created[0].Status.Should().Be("created");
            result.Tasks.Created[0].ServerId.Should().NotBeEmpty();
            result.Tasks.Created[0].Version.Should().BeGreaterThanOrEqualTo(1);

            result.Notes.Created.Should().HaveCount(1);
            result.Notes.Created[0].Status.Should().Be("created");
            result.Notes.Created[0].ServerId.Should().NotBeEmpty();
            result.Notes.Created[0].Version.Should().BeGreaterThanOrEqualTo(1);

            result.Conflicts.Should().BeEmpty();

            // Optional: verify that the created entities are visible via existing endpoints.

            var dateParam = Uri.EscapeDataString("2025-11-10");

            var tasksDayResponse = await client.GetAsync($"/api/tasks/day?date={dateParam}");
            tasksDayResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            var tasksForDay = await tasksDayResponse.Content.ReadFromJsonAsync<TaskSummaryDto[]>();
            tasksForDay.Should().NotBeNull();
            tasksForDay!.Should().Contain(t => t.Title == "Pushed task");

            var notesDayResponse = await client.GetAsync($"/api/notes/day?date={dateParam}");
            notesDayResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            var notesForDay = await notesDayResponse.Content.ReadFromJsonAsync<NoteSummaryDto[]>();
            notesForDay.Should().NotBeNull();
            notesForDay!.Should().Contain(n => n.Title == "Pushed note");
        }

        [Fact]
        public async Task Push_task_update_with_mismatched_version_returns_conflict()
        {
            // Arrange
            var client = _factory.CreateClientAsUser(Guid.NewGuid());
            // NEW: register a real device for this user
            var deviceId = await RegisterTestDeviceAsync(client);

            var now = DateTime.UtcNow;
            var date = new DateOnly(2025, 11, 10);

            // 1. Create a task via /api/tasks
            var createTaskPayload = new
            {
                Date = date,
                Title = "Original task",
                Description = "Initial",
                StartTime = (TimeOnly?)null,
                EndTime = (TimeOnly?)null,
                Location = (string?)null,
                TravelTime = (TimeSpan?)null,
                ReminderAtUtc = (DateTime?)null
            };

            var createResponse = await client.PostAsJsonAsync("/api/tasks", createTaskPayload);
            createResponse.StatusCode.Should().Be(HttpStatusCode.Created);

            var createdTask = await createResponse.Content.ReadFromJsonAsync<TaskDetailDto>();
            createdTask.Should().NotBeNull();

            // 2. Prepare sync payload with mismatched version
            var pushPayload = new SyncPushCommandPayloadDto
            {
                DeviceId = deviceId,
                ClientSyncTimestampUtc = now,
                Tasks = new SyncPushTasksDto
                {
                    Updated = new[]
                    {
                        new TaskUpdatedPushItemDto
                        {
                            Id = createdTask!.TaskId,
                            ExpectedVersion = 999, // deliberately wrong
                            Date = createdTask.Date,
                            Title = "Updated title from client",
                            Description = createdTask.Description,
                            StartTime = createdTask.StartTime,
                            EndTime = createdTask.EndTime,
                            Location = createdTask.Location,
                            TravelTime = createdTask.TravelTime,
                            ReminderAtUtc = createdTask.ReminderAtUtc
                        }
                    }
                }
            };

            // Act
            var response = await client.PostAsJsonAsync("/api/sync/push", pushPayload);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.OK);

            var result = await response.Content.ReadFromJsonAsync<SyncPushResultDto>();
            result.Should().NotBeNull();

            result!.Tasks.Updated.Should().ContainSingle(u => u.Id == createdTask!.TaskId && u.Status == "conflict");
            result.Conflicts.Should().Contain(c =>
                c.EntityType == "task" &&
                c.EntityId == createdTask.TaskId &&
                c.ConflictType == "version_mismatch");
        }

        [Fact]
        public async Task Get_changes_respects_maxItemsPerEntity_and_truncates_results()
        {
            // Arrange
            var client = _factory.CreateClientAsUser(Guid.NewGuid());
            var date = new DateOnly(2025, 11, 10);

            // Create 3 tasks
            for (int i = 0; i < 3; i++)
            {
                var createTaskPayload = new
                {
                    Date = date,
                    Title = $"Task {i}",
                    Description = "For pagination test",
                    StartTime = (TimeOnly?)null,
                    EndTime = (TimeOnly?)null,
                    Location = "Office",
                    TravelTime = (TimeSpan?)null,
                    ReminderAtUtc = (DateTime?)null
                };

                var response = await client.PostAsJsonAsync("/api/tasks", createTaskPayload);
                response.StatusCode.Should().Be(HttpStatusCode.Created);
            }

            // Create 3 notes
            for (int i = 0; i < 3; i++)
            {
                var createNotePayload = new
                {
                    Date = date,
                    Title = $"Note {i}",
                    Content = "For pagination test",
                    Summary = "Summary",
                    Tags = "sync,test"
                };

                var response = await client.PostAsJsonAsync("/api/notes", createNotePayload);
                response.StatusCode.Should().Be(HttpStatusCode.Created);
            }

            // Act: initial sync but ask for maxItemsPerEntity = 2
            var responseSync = await client.GetAsync("/api/sync/changes?maxItemsPerEntity=2");
            responseSync.StatusCode.Should().Be(HttpStatusCode.OK);

            var dto = await responseSync.Content.ReadFromJsonAsync<SyncChangesDto>();
            dto.Should().NotBeNull();

            // Assert: at most 2 task changes and 2 note changes per entity type
            dto!.Tasks.Created.Should().HaveCount(2);
            dto.Tasks.Updated.Should().BeEmpty();
            dto.Tasks.Deleted.Should().BeEmpty();

            dto.Notes.Created.Should().HaveCount(2);
            dto.Notes.Updated.Should().BeEmpty();
            dto.Notes.Deleted.Should().BeEmpty();

            dto.HasMoreTasks.Should().BeTrue();
            dto.HasMoreNotes.Should().BeTrue();
        }


        [Fact]
        public async Task Dev_push_with_empty_DeviceId_auto_creates_device_and_succeeds()
        {
            // Arrange
            var userId = Guid.NewGuid();
            var client = _factory.CreateClientAsUser(userId);

            // IMPORTANT: use the dev-bypass path (X-Debug-User)
            client.DefaultRequestHeaders.Add(DebugAuthConstants.DebugUserHeaderName, userId.ToString());


            var now = DateTime.UtcNow;

            // NOTE: DeviceId = Guid.Empty -> "frontend didn't register device"
            var payload = new SyncPushCommandPayloadDto
            {
                DeviceId = Guid.Empty,
                ClientSyncTimestampUtc = now,
                Tasks = new SyncPushTasksDto
                {
                    Created = new[]
                    {
                new TaskCreatedPushItemDto
                {
                    ClientId = Guid.NewGuid(),
                    Date = new DateOnly(2025, 11, 10),
                    Title = "Pushed task via dev auto-device",
                    Description = "Created via sync push (dev auto-device)"
                }
            }
                },
                Notes = new SyncPushNotesDto
                {
                    Created = new[]
                    {
                new NoteCreatedPushItemDto
                {
                    ClientId = Guid.NewGuid(),
                    Date = new DateOnly(2025, 11, 10),
                    Title = "Pushed note via dev auto-device",
                    Content = "Created via sync push (dev auto-device)"
                }
            }
                }
            };

            // Act: call /api/sync/push without a real device id
            var response = await client.PostAsJsonAsync("/api/sync/push", payload);

            // Assert: HTTP 200 and normal sync push semantics
            response.StatusCode.Should().Be(HttpStatusCode.OK);

            var result = await response.Content.ReadFromJsonAsync<SyncPushResultDto>();
            result.Should().NotBeNull();

            result!.Tasks.Created.Should().HaveCount(1);
            result.Tasks.Created[0].Status.Should().Be("created");
            result.Tasks.Created[0].ServerId.Should().NotBeEmpty();
            result.Tasks.Created[0].Version.Should().BeGreaterThanOrEqualTo(1);

            result.Notes.Created.Should().HaveCount(1);
            result.Notes.Created[0].Status.Should().Be("created");
            result.Notes.Created[0].ServerId.Should().NotBeEmpty();
            result.Notes.Created[0].Version.Should().BeGreaterThanOrEqualTo(1);

            result.Conflicts.Should().BeEmpty();

            // And: backend should have auto-created a device for this user
            var devicesResponse = await client.GetAsync("/api/devices");
            devicesResponse.StatusCode.Should().Be(HttpStatusCode.OK);

            var devices = await devicesResponse.Content.ReadFromJsonAsync<UserDeviceDto[]>();
            devices.Should().NotBeNull();
            devices!.Should().HaveCount(1);
            devices[0].Id.Should().NotBe(Guid.Empty);
        }

        [Fact]
        public async Task Push_with_empty_DeviceId_without_debug_header_returns_bad_request()
        {
            // Arrange: normal authenticated user, NO X-Debug-User header
            var client = _factory.CreateClientAsUser(Guid.NewGuid());

            var payload = new SyncPushCommandPayloadDto
            {
                DeviceId = Guid.Empty, // invalid in normal flow
                ClientSyncTimestampUtc = DateTime.UtcNow,
                Tasks = new SyncPushTasksDto(),
                Notes = new SyncPushNotesDto()
            };

            // Act
            var response = await client.PostAsJsonAsync("/api/sync/push", payload);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

            var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
            problem.Should().NotBeNull();
            problem!.Errors.Should().ContainKey("DeviceId");
            problem.Errors["DeviceId"].Should().ContainSingle()
                .Which.Should().Be("DeviceId is required.");
        }


        private async Task<Guid> RegisterTestDeviceAsync(HttpClient client)
        {
            var command = new RegisterDeviceCommand
            {
                DeviceToken = "sync-test-token-" + Guid.NewGuid(),
                Platform = DevicePlatform.Android,
                DeviceName = "Sync integration test device"
            };

            var response = await client.PostAsJsonAsync("/api/devices", command);
            response.StatusCode.Should().Be(HttpStatusCode.OK);

            var dto = await response.Content.ReadFromJsonAsync<UserDeviceDto>();
            dto.Should().NotBeNull();

            return dto!.Id;
        }
    }
}
