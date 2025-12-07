using FluentAssertions;
using NotesApp.Application.Sync.Commands.SyncPush;
using NotesApp.Application.Sync.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Application.Tests.Sync
{
    public sealed class SyncPushCommandValidatorTests
    {
        private readonly SyncPushCommandValidator _validator = new();

        [Fact]
        public void Valid_command_with_small_payload_passes_validation()
        {
            // Arrange
            var command = new SyncPushCommand
            {
                DeviceId = Guid.NewGuid(),
                ClientSyncTimestampUtc = DateTime.UtcNow,
                Tasks = new SyncPushTasksDto
                {
                    Created = new[]
                    {
                        new TaskCreatedPushItemDto
                        {
                            ClientId = Guid.NewGuid(),
                            Date = new DateOnly(2025, 1, 2),
                            Title = "Task",
                            Description = "Desc"
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
                            Date = new DateOnly(2025, 1, 2),
                            Title = "Note",
                            Content = "Content"
                        }
                    }
                }
            };

            // Act
            var result = _validator.Validate(command);

            // Assert
            result.IsValid.Should().BeTrue();
        }

        [Fact]
        public void Too_many_created_tasks_fails_validation()
        {
            // Arrange: more than the MaxItemsPerEntity = 500
            var manyTasks = Enumerable.Range(0, 501)
                .Select(i => new TaskCreatedPushItemDto
                {
                    ClientId = Guid.NewGuid(),
                    Date = new DateOnly(2025, 1, 2),
                    Title = $"Task {i}",
                    Description = "Desc"
                })
                .ToArray();

            var command = new SyncPushCommand
            {
                DeviceId = Guid.NewGuid(),
                ClientSyncTimestampUtc = DateTime.UtcNow,
                Tasks = new SyncPushTasksDto
                {
                    Created = manyTasks
                }
            };

            // Act
            var result = _validator.Validate(command);

            // Assert
            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e =>
                e.ErrorMessage.Contains("Tasks.Created cannot contain more than"));
        }

        [Fact]
        public void Too_many_total_items_fails_validation()
        {
            // Arrange: each list is under per-entity limit (500) but
            // total across all lists exceeds PushMaxTotalItems (2000).
            const int itemsPerList = 400; // 6 * 400 = 2400 > 2000

            var taskCreated = Enumerable.Range(0, itemsPerList)
        .Select(i => new TaskCreatedPushItemDto
        {
            ClientId = Guid.NewGuid(),
            Date = new DateOnly(2025, 1, 2),
            Title = $"Task Created {i}",
            Description = "Desc"
        })
        .ToArray();

            var taskUpdated = Enumerable.Range(0, itemsPerList)
                .Select(i => new TaskUpdatedPushItemDto
                {
                    Id = Guid.NewGuid(),
                    Date = new DateOnly(2025, 1, 2),
                    Title = $"Task Updated {i}",
                    Description = "Desc",
                    ExpectedVersion = 1
                })
                .ToArray();

            var taskDeleted = Enumerable.Range(0, itemsPerList)
                .Select(i => new TaskDeletedPushItemDto
                {
                    Id = Guid.NewGuid(),
                    ExpectedVersion = 1
                })
                .ToArray();

            var noteCreated = Enumerable.Range(0, itemsPerList)
                .Select(i => new NoteCreatedPushItemDto
                {
                    ClientId = Guid.NewGuid(),
                    Date = new DateOnly(2025, 1, 2),
                    Title = $"Note Created {i}",
                    Content = "Content"
                })
                .ToArray();

            var noteUpdated = Enumerable.Range(0, itemsPerList)
                .Select(i => new NoteUpdatedPushItemDto
                {
                    Id = Guid.NewGuid(),
                    Date = new DateOnly(2025, 1, 2),
                    Title = $"Note Updated {i}",
                    Content = "Content",
                    ExpectedVersion = 1
                })
                .ToArray();

            var noteDeleted = Enumerable.Range(0, itemsPerList)
                .Select(i => new NoteDeletedPushItemDto
                {
                    Id = Guid.NewGuid(),
                    ExpectedVersion = 1
                })
                .ToArray();

            var command = new SyncPushCommand
            {
                DeviceId = Guid.NewGuid(),
                ClientSyncTimestampUtc = DateTime.UtcNow,
                Tasks = new SyncPushTasksDto
                {
                    Created = taskCreated,
                    Updated = taskUpdated,
                    Deleted = taskDeleted
                },
                Notes = new SyncPushNotesDto
                {
                    Created = noteCreated,
                    Updated = noteUpdated,
                    Deleted = noteDeleted
                }
            };

            // Act
            var result = _validator.Validate(command);

            // Assert
            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e =>
                e.ErrorMessage.Contains("Total number of pushed items must not exceed"));
        }
    }
}
