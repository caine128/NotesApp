using FluentAssertions;
using NotesApp.Application.Sync.Commands.SyncPush;
using NotesApp.Application.Sync.Models;
using NotesApp.Domain.Entities;
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
                            Title = "Note"
                            // CHANGED: Content removed - content is now in blocks
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

        // ────────────────────────────────────────────────────────────────────────
        // Per-collection size limit tests for newer entity families
        // ────────────────────────────────────────────────────────────────────────

        [Fact]
        public void Too_many_created_subtasks_fails_validation()
        {
            var command = new SyncPushCommand
            {
                DeviceId = Guid.NewGuid(),
                ClientSyncTimestampUtc = DateTime.UtcNow,
                Subtasks = new SyncPushSubtasksDto
                {
                    Created = Enumerable.Range(0, 501)
                        .Select(_ => new SubtaskCreatedPushItemDto
                        {
                            ClientId = Guid.NewGuid(),
                            TaskId = Guid.NewGuid(),
                            Text = "do something",
                            Position = "a0"
                        })
                        .ToArray()
                }
            };

            var result = _validator.Validate(command);

            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e =>
                e.ErrorMessage.Contains("Subtasks.Created cannot contain more than"));
        }

        [Fact]
        public void Too_many_deleted_attachments_fails_validation()
        {
            var command = new SyncPushCommand
            {
                DeviceId = Guid.NewGuid(),
                ClientSyncTimestampUtc = DateTime.UtcNow,
                Attachments = new SyncPushAttachmentsDto
                {
                    Deleted = Enumerable.Range(0, 501)
                        .Select(_ => new AttachmentDeletedPushItemDto { Id = Guid.NewGuid() })
                        .ToArray()
                }
            };

            var result = _validator.Validate(command);

            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e =>
                e.ErrorMessage.Contains("Attachments.Deleted cannot contain more than"));
        }

        [Fact]
        public void Too_many_created_recurring_roots_fails_validation()
        {
            var command = new SyncPushCommand
            {
                DeviceId = Guid.NewGuid(),
                ClientSyncTimestampUtc = DateTime.UtcNow,
                RecurringRoots = new SyncPushRecurringRootsDto
                {
                    Created = Enumerable.Range(0, 501)
                        .Select(_ => new RecurringRootCreatedPushItemDto { ClientId = Guid.NewGuid() })
                        .ToArray()
                }
            };

            var result = _validator.Validate(command);

            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e =>
                e.ErrorMessage.Contains("RecurringRoots.Created cannot contain more than"));
        }

        [Fact]
        public void Too_many_created_recurring_series_fails_validation()
        {
            var command = new SyncPushCommand
            {
                DeviceId = Guid.NewGuid(),
                ClientSyncTimestampUtc = DateTime.UtcNow,
                RecurringSeries = new SyncPushRecurringSeriesDto
                {
                    Created = Enumerable.Range(0, 501)
                        .Select(_ => new RecurringSeriesCreatedPushItemDto
                        {
                            ClientId = Guid.NewGuid(),
                            RootId = Guid.NewGuid(),
                            RRuleString = "FREQ=DAILY",
                            StartsOnDate = new DateOnly(2025, 1, 1),
                            Title = "Daily"
                        })
                        .ToArray()
                }
            };

            var result = _validator.Validate(command);

            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e =>
                e.ErrorMessage.Contains("RecurringSeries.Created cannot contain more than"));
        }

        [Fact]
        public void Too_many_created_recurring_exceptions_fails_validation()
        {
            var command = new SyncPushCommand
            {
                DeviceId = Guid.NewGuid(),
                ClientSyncTimestampUtc = DateTime.UtcNow,
                RecurringExceptions = new SyncPushRecurringExceptionsDto
                {
                    Created = Enumerable.Range(0, 501)
                        .Select(i => new RecurringExceptionCreatedPushItemDto
                        {
                            ClientId = Guid.NewGuid(),
                            SeriesId = Guid.NewGuid(),
                            OccurrenceDate = new DateOnly(2025, 1, 1).AddDays(i)
                        })
                        .ToArray()
                }
            };

            var result = _validator.Validate(command);

            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e =>
                e.ErrorMessage.Contains("RecurringExceptions.Created cannot contain more than"));
        }

        // ────────────────────────────────────────────────────────────────────────
        // Total items limit now covers all collections
        // ────────────────────────────────────────────────────────────────────────

        [Fact]
        public void Total_items_limit_triggered_by_subtasks_alone()
        {
            // 2001 subtasks across C/U/D exceeds PushMaxTotalItems (2000)
            // Each individual collection is under the 500 per-entity cap
            var command = new SyncPushCommand
            {
                DeviceId = Guid.NewGuid(),
                ClientSyncTimestampUtc = DateTime.UtcNow,
                Subtasks = new SyncPushSubtasksDto
                {
                    Created = Enumerable.Range(0, 400)
                        .Select(_ => new SubtaskCreatedPushItemDto
                        {
                            ClientId = Guid.NewGuid(),
                            TaskId = Guid.NewGuid(),
                            Text = "do something",
                            Position = "a0"
                        })
                        .ToArray(),
                    Updated = Enumerable.Range(0, 400)
                        .Select(_ => new SubtaskUpdatedPushItemDto
                        {
                            Id = Guid.NewGuid(),
                            ExpectedVersion = 1
                        })
                        .ToArray(),
                    Deleted = Enumerable.Range(0, 400)
                        .Select(_ => new SubtaskDeletedPushItemDto { Id = Guid.NewGuid() })
                        .ToArray()
                },
                RecurringRoots = new SyncPushRecurringRootsDto
                {
                    Created = Enumerable.Range(0, 400)
                        .Select(_ => new RecurringRootCreatedPushItemDto { ClientId = Guid.NewGuid() })
                        .ToArray(),
                    Deleted = Enumerable.Range(0, 401)
                        .Select(_ => new RecurringRootDeletedPushItemDto { Id = Guid.NewGuid() })
                        .ToArray()
                }
            };
            // 400+400+400+400+401 = 2001 > 2000

            var result = _validator.Validate(command);

            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e =>
                e.ErrorMessage.Contains("Total number of pushed items must not exceed"));
        }

        // ────────────────────────────────────────────────────────────────────────
        // Per-item validator tests: Subtasks
        // ────────────────────────────────────────────────────────────────────────

        [Fact]
        public void Subtask_created_missing_clientId_fails_validation()
        {
            var command = new SyncPushCommand
            {
                DeviceId = Guid.NewGuid(),
                ClientSyncTimestampUtc = DateTime.UtcNow,
                Subtasks = new SyncPushSubtasksDto
                {
                    Created = [new SubtaskCreatedPushItemDto
                    {
                        ClientId = Guid.Empty, // missing
                        TaskId = Guid.NewGuid(),
                        Text = "do something",
                        Position = "a0"
                    }]
                }
            };

            var result = _validator.Validate(command);

            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e =>
                e.ErrorMessage.Contains("ClientId is required for created subtasks"));
        }

        [Fact]
        public void Subtask_created_missing_task_reference_fails_validation()
        {
            var command = new SyncPushCommand
            {
                DeviceId = Guid.NewGuid(),
                ClientSyncTimestampUtc = DateTime.UtcNow,
                Subtasks = new SyncPushSubtasksDto
                {
                    Created = [new SubtaskCreatedPushItemDto
                    {
                        ClientId = Guid.NewGuid(),
                        TaskId = null,
                        TaskClientId = null, // neither provided
                        Text = "do something",
                        Position = "a0"
                    }]
                }
            };

            var result = _validator.Validate(command);

            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e =>
                e.ErrorMessage.Contains("Either TaskId or TaskClientId must be provided"));
        }

        [Fact]
        public void Subtask_created_empty_text_fails_validation()
        {
            var command = new SyncPushCommand
            {
                DeviceId = Guid.NewGuid(),
                ClientSyncTimestampUtc = DateTime.UtcNow,
                Subtasks = new SyncPushSubtasksDto
                {
                    Created = [new SubtaskCreatedPushItemDto
                    {
                        ClientId = Guid.NewGuid(),
                        TaskId = Guid.NewGuid(),
                        Text = "",
                        Position = "a0"
                    }]
                }
            };

            var result = _validator.Validate(command);

            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e =>
                e.ErrorMessage.Contains("Subtask text is required"));
        }

        [Fact]
        public void Subtask_created_text_exceeding_max_length_fails_validation()
        {
            var command = new SyncPushCommand
            {
                DeviceId = Guid.NewGuid(),
                ClientSyncTimestampUtc = DateTime.UtcNow,
                Subtasks = new SyncPushSubtasksDto
                {
                    Created = [new SubtaskCreatedPushItemDto
                    {
                        ClientId = Guid.NewGuid(),
                        TaskId = Guid.NewGuid(),
                        Text = new string('x', Subtask.MaxTextLength + 1),
                        Position = "a0"
                    }]
                }
            };

            var result = _validator.Validate(command);

            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e =>
                e.ErrorMessage.Contains($"cannot exceed {Subtask.MaxTextLength} characters"));
        }

        [Fact]
        public void Subtask_created_with_taskClientId_instead_of_taskId_passes_validation()
        {
            var command = new SyncPushCommand
            {
                DeviceId = Guid.NewGuid(),
                ClientSyncTimestampUtc = DateTime.UtcNow,
                Subtasks = new SyncPushSubtasksDto
                {
                    Created = [new SubtaskCreatedPushItemDto
                    {
                        ClientId = Guid.NewGuid(),
                        TaskId = null,
                        TaskClientId = Guid.NewGuid(), // reference to task created in same push
                        Text = "do something",
                        Position = "a0"
                    }]
                }
            };

            var result = _validator.Validate(command);

            result.IsValid.Should().BeTrue();
        }

        [Fact]
        public void Subtask_updated_missing_id_fails_validation()
        {
            var command = new SyncPushCommand
            {
                DeviceId = Guid.NewGuid(),
                ClientSyncTimestampUtc = DateTime.UtcNow,
                Subtasks = new SyncPushSubtasksDto
                {
                    Updated = [new SubtaskUpdatedPushItemDto
                    {
                        Id = Guid.Empty,
                        ExpectedVersion = 1
                    }]
                }
            };

            var result = _validator.Validate(command);

            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e =>
                e.ErrorMessage.Contains("Id is required for updated subtasks"));
        }

        [Fact]
        public void Subtask_updated_zero_version_fails_validation()
        {
            var command = new SyncPushCommand
            {
                DeviceId = Guid.NewGuid(),
                ClientSyncTimestampUtc = DateTime.UtcNow,
                Subtasks = new SyncPushSubtasksDto
                {
                    Updated = [new SubtaskUpdatedPushItemDto
                    {
                        Id = Guid.NewGuid(),
                        ExpectedVersion = 0
                    }]
                }
            };

            var result = _validator.Validate(command);

            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e =>
                e.ErrorMessage.Contains("ExpectedVersion must be at least 1"));
        }

        [Fact]
        public void Subtask_updated_null_text_passes_validation()
        {
            // Null means "no change" — should be accepted
            var command = new SyncPushCommand
            {
                DeviceId = Guid.NewGuid(),
                ClientSyncTimestampUtc = DateTime.UtcNow,
                Subtasks = new SyncPushSubtasksDto
                {
                    Updated = [new SubtaskUpdatedPushItemDto
                    {
                        Id = Guid.NewGuid(),
                        ExpectedVersion = 1,
                        Text = null
                    }]
                }
            };

            var result = _validator.Validate(command);

            result.IsValid.Should().BeTrue();
        }

        [Fact]
        public void Subtask_updated_empty_string_text_fails_validation()
        {
            // Empty string is not the same as null — it means "clear text", which the domain rejects
            var command = new SyncPushCommand
            {
                DeviceId = Guid.NewGuid(),
                ClientSyncTimestampUtc = DateTime.UtcNow,
                Subtasks = new SyncPushSubtasksDto
                {
                    Updated = [new SubtaskUpdatedPushItemDto
                    {
                        Id = Guid.NewGuid(),
                        ExpectedVersion = 1,
                        Text = ""
                    }]
                }
            };

            var result = _validator.Validate(command);

            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e =>
                e.ErrorMessage.Contains("Subtask text cannot be empty"));
        }

        // ────────────────────────────────────────────────────────────────────────
        // Per-item validator tests: RecurringSeries
        // ────────────────────────────────────────────────────────────────────────

        [Fact]
        public void RecurringSeries_created_missing_clientId_fails_validation()
        {
            var command = new SyncPushCommand
            {
                DeviceId = Guid.NewGuid(),
                ClientSyncTimestampUtc = DateTime.UtcNow,
                RecurringSeries = new SyncPushRecurringSeriesDto
                {
                    Created = [new RecurringSeriesCreatedPushItemDto
                    {
                        ClientId = Guid.Empty,
                        RootId = Guid.NewGuid(),
                        RRuleString = "FREQ=DAILY",
                        StartsOnDate = new DateOnly(2025, 1, 1),
                        Title = "Daily"
                    }]
                }
            };

            var result = _validator.Validate(command);

            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e =>
                e.ErrorMessage.Contains("ClientId is required for created recurring series"));
        }

        [Fact]
        public void RecurringSeries_created_missing_root_reference_fails_validation()
        {
            var command = new SyncPushCommand
            {
                DeviceId = Guid.NewGuid(),
                ClientSyncTimestampUtc = DateTime.UtcNow,
                RecurringSeries = new SyncPushRecurringSeriesDto
                {
                    Created = [new RecurringSeriesCreatedPushItemDto
                    {
                        ClientId = Guid.NewGuid(),
                        RootId = null,
                        RootClientId = null, // neither provided
                        RRuleString = "FREQ=DAILY",
                        StartsOnDate = new DateOnly(2025, 1, 1),
                        Title = "Daily"
                    }]
                }
            };

            var result = _validator.Validate(command);

            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e =>
                e.ErrorMessage.Contains("Either RootId or RootClientId must be provided"));
        }

        [Fact]
        public void RecurringSeries_created_empty_rrule_fails_validation()
        {
            var command = new SyncPushCommand
            {
                DeviceId = Guid.NewGuid(),
                ClientSyncTimestampUtc = DateTime.UtcNow,
                RecurringSeries = new SyncPushRecurringSeriesDto
                {
                    Created = [new RecurringSeriesCreatedPushItemDto
                    {
                        ClientId = Guid.NewGuid(),
                        RootId = Guid.NewGuid(),
                        RRuleString = "",
                        StartsOnDate = new DateOnly(2025, 1, 1),
                        Title = "Daily"
                    }]
                }
            };

            var result = _validator.Validate(command);

            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e =>
                e.ErrorMessage.Contains("RRuleString is required"));
        }

        [Fact]
        public void RecurringSeries_created_default_starts_on_date_fails_validation()
        {
            var command = new SyncPushCommand
            {
                DeviceId = Guid.NewGuid(),
                ClientSyncTimestampUtc = DateTime.UtcNow,
                RecurringSeries = new SyncPushRecurringSeriesDto
                {
                    Created = [new RecurringSeriesCreatedPushItemDto
                    {
                        ClientId = Guid.NewGuid(),
                        RootId = Guid.NewGuid(),
                        RRuleString = "FREQ=DAILY",
                        StartsOnDate = default,
                        Title = "Daily"
                    }]
                }
            };

            var result = _validator.Validate(command);

            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e =>
                e.ErrorMessage.Contains("StartsOnDate must be a valid date"));
        }

        [Fact]
        public void RecurringSeries_created_empty_title_fails_validation()
        {
            var command = new SyncPushCommand
            {
                DeviceId = Guid.NewGuid(),
                ClientSyncTimestampUtc = DateTime.UtcNow,
                RecurringSeries = new SyncPushRecurringSeriesDto
                {
                    Created = [new RecurringSeriesCreatedPushItemDto
                    {
                        ClientId = Guid.NewGuid(),
                        RootId = Guid.NewGuid(),
                        RRuleString = "FREQ=DAILY",
                        StartsOnDate = new DateOnly(2025, 1, 1),
                        Title = ""
                    }]
                }
            };

            var result = _validator.Validate(command);

            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e =>
                e.ErrorMessage.Contains("Title is required"));
        }

        [Fact]
        public void RecurringSeries_created_with_rootClientId_instead_of_rootId_passes_validation()
        {
            var command = new SyncPushCommand
            {
                DeviceId = Guid.NewGuid(),
                ClientSyncTimestampUtc = DateTime.UtcNow,
                RecurringSeries = new SyncPushRecurringSeriesDto
                {
                    Created = [new RecurringSeriesCreatedPushItemDto
                    {
                        ClientId = Guid.NewGuid(),
                        RootId = null,
                        RootClientId = Guid.NewGuid(), // reference to root created in same push
                        RRuleString = "FREQ=DAILY",
                        StartsOnDate = new DateOnly(2025, 1, 1),
                        Title = "Daily"
                    }]
                }
            };

            var result = _validator.Validate(command);

            result.IsValid.Should().BeTrue();
        }

        [Fact]
        public void RecurringSeries_updated_zero_version_fails_validation()
        {
            var command = new SyncPushCommand
            {
                DeviceId = Guid.NewGuid(),
                ClientSyncTimestampUtc = DateTime.UtcNow,
                RecurringSeries = new SyncPushRecurringSeriesDto
                {
                    Updated = [new RecurringSeriesUpdatedPushItemDto
                    {
                        Id = Guid.NewGuid(),
                        ExpectedVersion = 0,
                        Title = "Daily"
                    }]
                }
            };

            var result = _validator.Validate(command);

            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e =>
                e.ErrorMessage.Contains("ExpectedVersion must be at least 1"));
        }

        // ────────────────────────────────────────────────────────────────────────
        // Per-item validator tests: RecurringExceptions
        // ────────────────────────────────────────────────────────────────────────

        [Fact]
        public void RecurringException_created_missing_seriesId_fails_validation()
        {
            var command = new SyncPushCommand
            {
                DeviceId = Guid.NewGuid(),
                ClientSyncTimestampUtc = DateTime.UtcNow,
                RecurringExceptions = new SyncPushRecurringExceptionsDto
                {
                    Created = [new RecurringExceptionCreatedPushItemDto
                    {
                        ClientId = Guid.NewGuid(),
                        SeriesId = Guid.Empty,
                        OccurrenceDate = new DateOnly(2025, 1, 1)
                    }]
                }
            };

            var result = _validator.Validate(command);

            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e =>
                e.ErrorMessage.Contains("SeriesId is required"));
        }

        [Fact]
        public void RecurringException_created_default_occurrence_date_fails_validation()
        {
            var command = new SyncPushCommand
            {
                DeviceId = Guid.NewGuid(),
                ClientSyncTimestampUtc = DateTime.UtcNow,
                RecurringExceptions = new SyncPushRecurringExceptionsDto
                {
                    Created = [new RecurringExceptionCreatedPushItemDto
                    {
                        ClientId = Guid.NewGuid(),
                        SeriesId = Guid.NewGuid(),
                        OccurrenceDate = default
                    }]
                }
            };

            var result = _validator.Validate(command);

            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e =>
                e.ErrorMessage.Contains("OccurrenceDate must be a valid date"));
        }

        // ────────────────────────────────────────────────────────────────────────
        // Per-item validator tests: Attachments and RecurringAttachments
        // ────────────────────────────────────────────────────────────────────────

        [Fact]
        public void Attachment_deleted_empty_id_fails_validation()
        {
            var command = new SyncPushCommand
            {
                DeviceId = Guid.NewGuid(),
                ClientSyncTimestampUtc = DateTime.UtcNow,
                Attachments = new SyncPushAttachmentsDto
                {
                    Deleted = [new AttachmentDeletedPushItemDto { Id = Guid.Empty }]
                }
            };

            var result = _validator.Validate(command);

            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e =>
                e.ErrorMessage.Contains("Id is required for deleted attachments"));
        }

        [Fact]
        public void RecurringAttachment_deleted_empty_id_fails_validation()
        {
            var command = new SyncPushCommand
            {
                DeviceId = Guid.NewGuid(),
                ClientSyncTimestampUtc = DateTime.UtcNow,
                RecurringAttachments = new SyncPushRecurringAttachmentsDto
                {
                    Deleted = [new RecurringAttachmentDeletedPushItemDto { Id = Guid.Empty }]
                }
            };

            var result = _validator.Validate(command);

            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e =>
                e.ErrorMessage.Contains("Id is required for deleted recurring attachments"));
        }

        // ────────────────────────────────────────────────────────────────────────
        // Per-item validator tests: RecurringRoots
        // ────────────────────────────────────────────────────────────────────────

        [Fact]
        public void RecurringRoot_created_empty_clientId_fails_validation()
        {
            var command = new SyncPushCommand
            {
                DeviceId = Guid.NewGuid(),
                ClientSyncTimestampUtc = DateTime.UtcNow,
                RecurringRoots = new SyncPushRecurringRootsDto
                {
                    Created = [new RecurringRootCreatedPushItemDto { ClientId = Guid.Empty }]
                }
            };

            var result = _validator.Validate(command);

            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e =>
                e.ErrorMessage.Contains("ClientId is required for created recurring roots"));
        }

        [Fact]
        public void RecurringRoot_deleted_empty_id_fails_validation()
        {
            var command = new SyncPushCommand
            {
                DeviceId = Guid.NewGuid(),
                ClientSyncTimestampUtc = DateTime.UtcNow,
                RecurringRoots = new SyncPushRecurringRootsDto
                {
                    Deleted = [new RecurringRootDeletedPushItemDto { Id = Guid.Empty }]
                }
            };

            var result = _validator.Validate(command);

            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e =>
                e.ErrorMessage.Contains("Id is required for deleted recurring roots"));
        }

        // ────────────────────────────────────────────────────────────────────────
        // Per-item validator tests: RecurringSeriesSubtasks
        // ────────────────────────────────────────────────────────────────────────

        [Fact]
        public void RecurringSeriesSubtask_created_missing_clientId_fails_validation()
        {
            var command = new SyncPushCommand
            {
                DeviceId = Guid.NewGuid(),
                ClientSyncTimestampUtc = DateTime.UtcNow,
                RecurringSeriesSubtasks = new SyncPushRecurringSeriesSubtasksDto
                {
                    Created = [new RecurringSubtaskCreatedPushItemDto
                    {
                        ClientId = Guid.Empty,
                        SeriesId = Guid.NewGuid(),
                        Text = "do something",
                        Position = "a0"
                    }]
                }
            };

            var result = _validator.Validate(command);

            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e =>
                e.ErrorMessage.Contains("ClientId is required for created recurring subtasks"));
        }

        [Fact]
        public void RecurringSeriesSubtask_created_missing_all_parent_references_fails_validation()
        {
            // No SeriesId, SeriesClientId, or ExceptionId — all null/empty
            var command = new SyncPushCommand
            {
                DeviceId = Guid.NewGuid(),
                ClientSyncTimestampUtc = DateTime.UtcNow,
                RecurringSeriesSubtasks = new SyncPushRecurringSeriesSubtasksDto
                {
                    Created = [new RecurringSubtaskCreatedPushItemDto
                    {
                        ClientId = Guid.NewGuid(),
                        SeriesId = null,
                        SeriesClientId = null,
                        ExceptionId = null,
                        Text = "do something",
                        Position = "a0"
                    }]
                }
            };

            var result = _validator.Validate(command);

            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e =>
                e.ErrorMessage.Contains("Either SeriesId/SeriesClientId or ExceptionId must be provided"));
        }

        [Fact]
        public void RecurringSeriesSubtask_created_empty_text_fails_validation()
        {
            var command = new SyncPushCommand
            {
                DeviceId = Guid.NewGuid(),
                ClientSyncTimestampUtc = DateTime.UtcNow,
                RecurringSeriesSubtasks = new SyncPushRecurringSeriesSubtasksDto
                {
                    Created = [new RecurringSubtaskCreatedPushItemDto
                    {
                        ClientId = Guid.NewGuid(),
                        SeriesId = Guid.NewGuid(),
                        Text = "",
                        Position = "a0"
                    }]
                }
            };

            var result = _validator.Validate(command);

            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e =>
                e.ErrorMessage.Contains("Subtask text is required"));
        }

        [Fact]
        public void RecurringSeriesSubtask_created_with_exceptionId_instead_of_seriesId_passes_validation()
        {
            // ExceptionId is the alternative FK for exception-override subtasks
            var command = new SyncPushCommand
            {
                DeviceId = Guid.NewGuid(),
                ClientSyncTimestampUtc = DateTime.UtcNow,
                RecurringSeriesSubtasks = new SyncPushRecurringSeriesSubtasksDto
                {
                    Created = [new RecurringSubtaskCreatedPushItemDto
                    {
                        ClientId = Guid.NewGuid(),
                        SeriesId = null,
                        SeriesClientId = null,
                        ExceptionId = Guid.NewGuid(),
                        Text = "do something",
                        Position = "a0"
                    }]
                }
            };

            var result = _validator.Validate(command);

            result.IsValid.Should().BeTrue();
        }

        [Fact]
        public void RecurringSeriesSubtask_created_with_seriesClientId_instead_of_seriesId_passes_validation()
        {
            // SeriesClientId is the within-push reference to a series created in the same push
            var command = new SyncPushCommand
            {
                DeviceId = Guid.NewGuid(),
                ClientSyncTimestampUtc = DateTime.UtcNow,
                RecurringSeriesSubtasks = new SyncPushRecurringSeriesSubtasksDto
                {
                    Created = [new RecurringSubtaskCreatedPushItemDto
                    {
                        ClientId = Guid.NewGuid(),
                        SeriesId = null,
                        SeriesClientId = Guid.NewGuid(),
                        ExceptionId = null,
                        Text = "do something",
                        Position = "a0"
                    }]
                }
            };

            var result = _validator.Validate(command);

            result.IsValid.Should().BeTrue();
        }

        [Fact]
        public void RecurringSeriesSubtask_updated_zero_version_fails_validation()
        {
            var command = new SyncPushCommand
            {
                DeviceId = Guid.NewGuid(),
                ClientSyncTimestampUtc = DateTime.UtcNow,
                RecurringSeriesSubtasks = new SyncPushRecurringSeriesSubtasksDto
                {
                    Updated = [new RecurringSubtaskUpdatedPushItemDto
                    {
                        Id = Guid.NewGuid(),
                        ExpectedVersion = 0
                    }]
                }
            };

            var result = _validator.Validate(command);

            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e =>
                e.ErrorMessage.Contains("ExpectedVersion must be at least 1"));
        }

        [Fact]
        public void RecurringSeriesSubtask_updated_empty_string_text_fails_validation()
        {
            // Empty string is not the same as null — null means no change, empty string is invalid
            var command = new SyncPushCommand
            {
                DeviceId = Guid.NewGuid(),
                ClientSyncTimestampUtc = DateTime.UtcNow,
                RecurringSeriesSubtasks = new SyncPushRecurringSeriesSubtasksDto
                {
                    Updated = [new RecurringSubtaskUpdatedPushItemDto
                    {
                        Id = Guid.NewGuid(),
                        ExpectedVersion = 1,
                        Text = ""
                    }]
                }
            };

            var result = _validator.Validate(command);

            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e =>
                e.ErrorMessage.Contains("Subtask text cannot be empty"));
        }

        // ────────────────────────────────────────────────────────────────────────
        // Happy-path test: all new collections together pass validation
        // ────────────────────────────────────────────────────────────────────────

        [Fact]
        public void Valid_command_with_all_new_collections_passes_validation()
        {
            var rootClientId = Guid.NewGuid();
            var seriesClientId = Guid.NewGuid();
            var taskClientId = Guid.NewGuid();

            var command = new SyncPushCommand
            {
                DeviceId = Guid.NewGuid(),
                ClientSyncTimestampUtc = DateTime.UtcNow,
                RecurringRoots = new SyncPushRecurringRootsDto
                {
                    Created = [new RecurringRootCreatedPushItemDto { ClientId = rootClientId }],
                    Deleted = [new RecurringRootDeletedPushItemDto { Id = Guid.NewGuid() }]
                },
                RecurringSeries = new SyncPushRecurringSeriesDto
                {
                    Created = [new RecurringSeriesCreatedPushItemDto
                    {
                        ClientId = seriesClientId,
                        RootClientId = rootClientId,
                        RRuleString = "FREQ=WEEKLY;BYDAY=MO",
                        StartsOnDate = new DateOnly(2025, 6, 1),
                        Title = "Weekly standup"
                    }],
                    Updated = [new RecurringSeriesUpdatedPushItemDto
                    {
                        Id = Guid.NewGuid(),
                        ExpectedVersion = 1,
                        Title = "Updated standup"
                    }],
                    Deleted = [new RecurringSeriesDeletedPushItemDto { Id = Guid.NewGuid() }]
                },
                RecurringSeriesSubtasks = new SyncPushRecurringSeriesSubtasksDto
                {
                    Created = [new RecurringSubtaskCreatedPushItemDto
                    {
                        ClientId = Guid.NewGuid(),
                        SeriesClientId = seriesClientId,
                        Text = "prep agenda",
                        Position = "a0"
                    }],
                    Updated = [new RecurringSubtaskUpdatedPushItemDto
                    {
                        Id = Guid.NewGuid(),
                        ExpectedVersion = 2,
                        Text = "review action items"
                    }],
                    Deleted = [new RecurringSubtaskDeletedPushItemDto { Id = Guid.NewGuid() }]
                },
                RecurringExceptions = new SyncPushRecurringExceptionsDto
                {
                    Created = [new RecurringExceptionCreatedPushItemDto
                    {
                        ClientId = Guid.NewGuid(),
                        SeriesId = Guid.NewGuid(),
                        OccurrenceDate = new DateOnly(2025, 6, 9),
                        IsDeletion = true
                    }],
                    Updated = [new RecurringExceptionUpdatedPushItemDto
                    {
                        Id = Guid.NewGuid(),
                        ExpectedVersion = 1
                    }],
                    Deleted = [new RecurringExceptionDeletedPushItemDto { Id = Guid.NewGuid() }]
                },
                Subtasks = new SyncPushSubtasksDto
                {
                    Created = [new SubtaskCreatedPushItemDto
                    {
                        ClientId = Guid.NewGuid(),
                        TaskClientId = taskClientId,
                        Text = "review notes",
                        Position = "a0"
                    }],
                    Updated = [new SubtaskUpdatedPushItemDto
                    {
                        Id = Guid.NewGuid(),
                        ExpectedVersion = 1,
                        IsCompleted = true
                    }],
                    Deleted = [new SubtaskDeletedPushItemDto { Id = Guid.NewGuid() }]
                },
                Attachments = new SyncPushAttachmentsDto
                {
                    Deleted = [new AttachmentDeletedPushItemDto { Id = Guid.NewGuid() }]
                },
                RecurringAttachments = new SyncPushRecurringAttachmentsDto
                {
                    Deleted = [new RecurringAttachmentDeletedPushItemDto { Id = Guid.NewGuid() }]
                }
            };

            var result = _validator.Validate(command);

            result.IsValid.Should().BeTrue();
        }

        // ────────────────────────────────────────────────────────────────────────
        // Original total-items limit test (kept; still valid — 6 × 400 = 2400 > 2000)
        // ────────────────────────────────────────────────────────────────────────

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
                    Title = $"Note Created {i}"
                    // CHANGED: Content removed - content is now in blocks
                })
                .ToArray();

            var noteUpdated = Enumerable.Range(0, itemsPerList)
                .Select(i => new NoteUpdatedPushItemDto
                {
                    Id = Guid.NewGuid(),
                    Date = new DateOnly(2025, 1, 2),
                    Title = $"Note Updated {i}",
                    // CHANGED: Content removed - content is now in blocks
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
