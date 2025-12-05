using FluentAssertions;
using NotesApp.Api.IntegrationTests.Infrastructure.Hosting;
using NotesApp.Application.Tasks.Models;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http.Json;
using System.Text;

namespace NotesApp.Api.IntegrationTests.Tasks
{
    public sealed class TaskReminderEndpointsTests : IClassFixture<NotesAppApiFactory>
    {
        private readonly NotesAppApiFactory _factory;

        public TaskReminderEndpointsTests(NotesAppApiFactory factory)
        {
            _factory = factory;
        }

        [Fact]
        public async Task Acknowledge_reminder_for_task_with_reminder_returns_no_content()
        {
            // Arrange
            var client = _factory.CreateClientAsUser(Guid.NewGuid());
            var date = new DateOnly(2025, 11, 10);
            var reminderAt = DateTime.SpecifyKind(DateTime.UtcNow.AddHours(1), DateTimeKind.Utc);

            var createPayload = new
            {
                Date = date,
                Title = "Task with reminder",
                Description = "Desc",
                StartTime = (TimeOnly?)null,
                EndTime = (TimeOnly?)null,
                Location = (string?)null,
                TravelTime = (TimeSpan?)null,
                ReminderAtUtc = reminderAt
            };

            var createResponse = await client.PostAsJsonAsync("/api/tasks", createPayload);
            createResponse.StatusCode.Should().Be(HttpStatusCode.Created);

            var taskDetail = await createResponse.Content.ReadFromJsonAsync<TaskDetailDto>();
            taskDetail.Should().NotBeNull();

            var request = new AcknowledgeTaskReminderRequestDto(
                DeviceId: Guid.NewGuid(),
                AcknowledgedAtUtc: DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc));

            // Act
            var response = await client.PostAsJsonAsync(
                $"/api/tasks/{taskDetail!.TaskId}/reminder/acknowledge",
                request);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }
    }
}
