using FluentValidation;
using NotesApp.Application.Notes.Commands.CreateNote;
using NotesApp.Application.Notes.Commands.UpdateNote;
using NotesApp.Application.Sync.Models;
using NotesApp.Application.Tasks.Commands.CreateTask;
using NotesApp.Application.Tasks.Commands.UpdateTask;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Application.Sync.Commands.SyncPush
{
    /// <summary>
    /// Validator for <see cref="SyncPushCommand"/>.
    ///
    /// Responsibilities:
    /// - DeviceId must be non-empty
    /// - ClientSyncTimestampUtc must not be default
    /// - For created/updated items, reuse the existing Create*/Update* command validators
    ///   so we don't duplicate validation logic.
    /// - Plus push-specific rules:
    ///     - Created items must have a non-empty ClientId
    ///     - Updated items must have ExpectedVersion &gt;= 1
    /// </summary>
    public sealed class SyncPushCommandValidator : AbstractValidator<SyncPushCommand>
    {

        public SyncPushCommandValidator()
        {
            RuleFor(x => x.DeviceId)
                .NotEmpty()
                .WithMessage("DeviceId is required.");

            RuleFor(x => x.ClientSyncTimestampUtc)
                .Must(t => t != default)
                .WithMessage("ClientSyncTimestampUtc must be a valid UTC timestamp.");

            RuleFor(x => x.Tasks.Created)
                .Must(list => list.Count <= SyncLimits.PushMaxItemsPerEntity)
                .WithMessage($"Tasks.Created cannot contain more than {SyncLimits.PushMaxItemsPerEntity} items.");

            RuleFor(x => x.Tasks.Updated)
                .Must(list => list.Count <= SyncLimits.PushMaxItemsPerEntity)
                .WithMessage($"Tasks.Updated cannot contain more than {SyncLimits.PushMaxItemsPerEntity} items.");

            RuleFor(x => x.Tasks.Deleted)
                .Must(list => list.Count <= SyncLimits.PushMaxItemsPerEntity)
                .WithMessage($"Tasks.Deleted cannot contain more than {SyncLimits.PushMaxItemsPerEntity} items.");

            RuleFor(x => x.Notes.Created)
                .Must(list => list.Count <= SyncLimits.PushMaxItemsPerEntity)
                .WithMessage($"Notes.Created cannot contain more than {SyncLimits.PushMaxItemsPerEntity} items.");

            RuleFor(x => x.Notes.Updated)
                .Must(list => list.Count <= SyncLimits.PushMaxItemsPerEntity)
                .WithMessage($"Notes.Updated cannot contain more than {SyncLimits.PushMaxItemsPerEntity} items.");

            RuleFor(x => x.Notes.Deleted)
                .Must(list => list.Count <= SyncLimits.PushMaxItemsPerEntity)
                .WithMessage($"Notes.Deleted cannot contain more than {SyncLimits.PushMaxItemsPerEntity} items.");

            RuleFor(x => x)
                .Must(cmd =>
                {
                    var total =
                        cmd.Tasks.Created.Count +
                        cmd.Tasks.Updated.Count +
                        cmd.Tasks.Deleted.Count +
                        cmd.Notes.Created.Count +
                        cmd.Notes.Updated.Count +
                        cmd.Notes.Deleted.Count;

                    return total <= SyncLimits.PushMaxTotalItems;
                })
                .WithMessage($"Total number of pushed items must not exceed {SyncLimits.PushMaxTotalItems}.");

            RuleForEach(x => x.Tasks.Created)
                .SetValidator(new TaskCreatedPushItemValidator());

            RuleForEach(x => x.Tasks.Updated)
                .SetValidator(new TaskUpdatedPushItemValidator());

            RuleForEach(x => x.Notes.Created)
                .SetValidator(new NoteCreatedPushItemValidator());

            RuleForEach(x => x.Notes.Updated)
                .SetValidator(new NoteUpdatedPushItemValidator());
        }

        // --------------------------------------------------------------------
        // Task Created
        // --------------------------------------------------------------------
        private sealed class TaskCreatedPushItemValidator : AbstractValidator<TaskCreatedPushItemDto>
        {
            public TaskCreatedPushItemValidator()
            {
                // Sync-specific: client id must be provided for creates
                RuleFor(x => x.ClientId)
                    .NotEmpty();

                // Reuse full CreateTaskCommandValidator for field-level rules
                var inner = new CreateTaskCommandValidator();

                RuleFor(x => x).Custom((dto, context) =>
                {
                    var command = new CreateTaskCommand
                    {
                        Date = dto.Date,
                        Title = dto.Title,
                        Description = dto.Description,
                        StartTime = dto.StartTime,
                        EndTime = dto.EndTime,
                        Location = dto.Location,
                        TravelTime = dto.TravelTime,
                        ReminderAtUtc = dto.ReminderAtUtc
                    };

                    var result = inner.Validate(command);

                    foreach (var error in result.Errors)
                    {
                        // Propagate errors so they show up under the same property names
                        context.AddFailure(error.PropertyName, error.ErrorMessage);
                    }
                });
            }
        }

        // --------------------------------------------------------------------
        // Task Updated
        // --------------------------------------------------------------------
        private sealed class TaskUpdatedPushItemValidator : AbstractValidator<TaskUpdatedPushItemDto>
        {
            public TaskUpdatedPushItemValidator()
            {
                // Basic identity/version rules that only exist in sync
                RuleFor(x => x.Id)
                    .NotEmpty();

                RuleFor(x => x.ExpectedVersion)
                    .GreaterThanOrEqualTo(1);

                // Reuse full UpdateTaskCommandValidator for field-level rules
                var inner = new UpdateTaskCommandValidator();

                RuleFor(x => x).Custom((dto, context) =>
                {
                    var command = new UpdateTaskCommand
                    {
                        TaskId = dto.Id,
                        Date = dto.Date,
                        Title = dto.Title,
                        Description = dto.Description,
                        StartTime = dto.StartTime,
                        EndTime = dto.EndTime,
                        Location = dto.Location,
                        TravelTime = dto.TravelTime,
                        ReminderAtUtc = dto.ReminderAtUtc
                    };

                    var result = inner.Validate(command);

                    foreach (var error in result.Errors)
                    {
                        context.AddFailure(error.PropertyName, error.ErrorMessage);
                    }
                });
            }
        }

        // --------------------------------------------------------------------
        // Note Created
        // --------------------------------------------------------------------
        private sealed class NoteCreatedPushItemValidator : AbstractValidator<NoteCreatedPushItemDto>
        {
            public NoteCreatedPushItemValidator()
            {
                // Sync-specific: client id must be provided for creates
                RuleFor(x => x.ClientId)
                    .NotEmpty();

                // Reuse full CreateNoteCommandValidator for field-level rules
                var inner = new CreateNoteCommandValidator();

                RuleFor(x => x).Custom((dto, context) =>
                {
                    var command = new CreateNoteCommand
                    {
                        Date = dto.Date,
                        Title = dto.Title,
                        Content = dto.Content,
                        Summary = dto.Summary,
                        Tags = dto.Tags
                    };

                    var result = inner.Validate(command);

                    foreach (var error in result.Errors)
                    {
                        context.AddFailure(error.PropertyName, error.ErrorMessage);
                    }
                });
            }
        }

        // --------------------------------------------------------------------
        // Note Updated
        // --------------------------------------------------------------------
        private sealed class NoteUpdatedPushItemValidator : AbstractValidator<NoteUpdatedPushItemDto>
        {
            public NoteUpdatedPushItemValidator()
            {
                RuleFor(x => x.Id)
                    .NotEmpty();

                RuleFor(x => x.ExpectedVersion)
                    .GreaterThanOrEqualTo(1);

                var inner = new UpdateNoteCommandValidator();

                RuleFor(x => x).Custom((dto, context) =>
                {
                    var command = new UpdateNoteCommand
                    {
                        NoteId = dto.Id,
                        Date = dto.Date,
                        Title = dto.Title,
                        Content = dto.Content,
                        Summary = dto.Summary,
                        Tags = dto.Tags
                    };

                    var result = inner.Validate(command);

                    foreach (var error in result.Errors)
                    {
                        context.AddFailure(error.PropertyName, error.ErrorMessage);
                    }
                });
            }
        }
    }
}
