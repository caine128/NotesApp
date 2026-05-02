using FluentValidation;
using NotesApp.Application.Blocks.Commands.CreateBlock;
using NotesApp.Application.Blocks.Commands.DeleteBlock;
using NotesApp.Application.Blocks.Commands.UpdateBlock;
using NotesApp.Application.Notes.Commands.CreateNote;
using NotesApp.Application.Notes.Commands.DeleteNote;
using NotesApp.Application.Notes.Commands.UpdateNote;
using NotesApp.Application.Sync.Models;
using NotesApp.Application.Tasks.Commands.CreateTask;
using NotesApp.Application.Tasks.Commands.DeleteTask;
using NotesApp.Application.Tasks.Commands.UpdateTask;
using NotesApp.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Application.Sync.Commands.SyncPush
{
    /// <summary>
    /// Validator for <see cref="SyncPushCommand"/>.
    ///
    /// Validates:
    /// - DeviceId must be non-empty
    /// - ClientSyncTimestampUtc must not be default
    /// - Per-collection limits for all sync-able entity families
    /// - Total item limit across all collections
    /// - Individual item validation for creates, updates, and deletes
    ///
    /// Strategy: Reuses existing Create*/Update*/Delete* command validators for field-level rules
    /// where they exist (Tasks, Notes, Blocks). Newer entity families (Categories, Subtasks,
    /// recurring entities, attachments) carry their own inline rules.
    /// </summary>
    public sealed class SyncPushCommandValidator : AbstractValidator<SyncPushCommand>
    {

        public SyncPushCommandValidator()
        {
            // ─────────────────────────────────────────────────────────────────
            // Top-level required fields
            // ─────────────────────────────────────────────────────────────────

            RuleFor(x => x.DeviceId)
                .NotEmpty()
                .WithMessage("DeviceId is required.");

            RuleFor(x => x.ClientSyncTimestampUtc)
                .Must(t => t != default)
                .WithMessage("ClientSyncTimestampUtc must be a valid UTC timestamp.");


            // ─────────────────────────────────────────────────────────────────
            // Per-collection size limits: Tasks
            // ─────────────────────────────────────────────────────────────────

            RuleFor(x => x.Tasks.Created)
                .Must(list => list.Count <= SyncLimits.PushMaxTasks)
                .WithMessage($"Tasks.Created cannot contain more than {SyncLimits.PushMaxTasks} items.");

            RuleFor(x => x.Tasks.Updated)
                .Must(list => list.Count <= SyncLimits.PushMaxTasks)
                .WithMessage($"Tasks.Updated cannot contain more than {SyncLimits.PushMaxTasks} items.");

            RuleFor(x => x.Tasks.Deleted)
                .Must(list => list.Count <= SyncLimits.PushMaxTasks)
                .WithMessage($"Tasks.Deleted cannot contain more than {SyncLimits.PushMaxTasks} items.");

            // ─────────────────────────────────────────────────────────────────
            // Per-collection size limits: Notes
            // ─────────────────────────────────────────────────────────────────

            RuleFor(x => x.Notes.Created)
                .Must(list => list.Count <= SyncLimits.PushMaxNotes)
                .WithMessage($"Notes.Created cannot contain more than {SyncLimits.PushMaxNotes} items.");

            RuleFor(x => x.Notes.Updated)
                .Must(list => list.Count <= SyncLimits.PushMaxNotes)
                .WithMessage($"Notes.Updated cannot contain more than {SyncLimits.PushMaxNotes} items.");

            RuleFor(x => x.Notes.Deleted)
                .Must(list => list.Count <= SyncLimits.PushMaxNotes)
                .WithMessage($"Notes.Deleted cannot contain more than {SyncLimits.PushMaxNotes} items.");

            // ─────────────────────────────────────────────────────────────────
            // Per-collection size limits: Blocks
            // ─────────────────────────────────────────────────────────────────

            RuleFor(x => x.Blocks.Created)
                .Must(list => list.Count <= SyncLimits.PushMaxBlocks)
                .WithMessage($"Blocks.Created cannot contain more than {SyncLimits.PushMaxBlocks} items.");

            RuleFor(x => x.Blocks.Updated)
                .Must(list => list.Count <= SyncLimits.PushMaxBlocks)
                .WithMessage($"Blocks.Updated cannot contain more than {SyncLimits.PushMaxBlocks} items.");

            RuleFor(x => x.Blocks.Deleted)
                .Must(list => list.Count <= SyncLimits.PushMaxBlocks)
                .WithMessage($"Blocks.Deleted cannot contain more than {SyncLimits.PushMaxBlocks} items.");

            // ─────────────────────────────────────────────────────────────────
            // REFACTORED: Per-collection size limits: Categories
            // ─────────────────────────────────────────────────────────────────

            RuleFor(x => x.Categories.Created)
                .Must(list => list.Count <= SyncLimits.PushMaxCategories)
                .WithMessage($"Categories.Created cannot contain more than {SyncLimits.PushMaxCategories} items.");

            RuleFor(x => x.Categories.Updated)
                .Must(list => list.Count <= SyncLimits.PushMaxCategories)
                .WithMessage($"Categories.Updated cannot contain more than {SyncLimits.PushMaxCategories} items.");

            RuleFor(x => x.Categories.Deleted)
                .Must(list => list.Count <= SyncLimits.PushMaxCategories)
                .WithMessage($"Categories.Deleted cannot contain more than {SyncLimits.PushMaxCategories} items.");

            // ─────────────────────────────────────────────────────────────────
            // Per-collection size limits: Subtasks
            // ─────────────────────────────────────────────────────────────────

            RuleFor(x => x.Subtasks.Created)
                .Must(list => list.Count <= SyncLimits.PushMaxSubtasks)
                .WithMessage($"Subtasks.Created cannot contain more than {SyncLimits.PushMaxSubtasks} items.");

            RuleFor(x => x.Subtasks.Updated)
                .Must(list => list.Count <= SyncLimits.PushMaxSubtasks)
                .WithMessage($"Subtasks.Updated cannot contain more than {SyncLimits.PushMaxSubtasks} items.");

            RuleFor(x => x.Subtasks.Deleted)
                .Must(list => list.Count <= SyncLimits.PushMaxSubtasks)
                .WithMessage($"Subtasks.Deleted cannot contain more than {SyncLimits.PushMaxSubtasks} items.");

            // ─────────────────────────────────────────────────────────────────
            // Per-collection size limits: Attachments
            // ─────────────────────────────────────────────────────────────────

            RuleFor(x => x.Attachments.Deleted)
                .Must(list => list.Count <= SyncLimits.PushMaxAttachmentDeletes)
                .WithMessage($"Attachments.Deleted cannot contain more than {SyncLimits.PushMaxAttachmentDeletes} items.");

            // ─────────────────────────────────────────────────────────────────
            // Per-collection size limits: RecurringRoots
            // ─────────────────────────────────────────────────────────────────

            RuleFor(x => x.RecurringRoots.Created)
                .Must(list => list.Count <= SyncLimits.PushMaxRecurringRoots)
                .WithMessage($"RecurringRoots.Created cannot contain more than {SyncLimits.PushMaxRecurringRoots} items.");

            RuleFor(x => x.RecurringRoots.Deleted)
                .Must(list => list.Count <= SyncLimits.PushMaxRecurringRoots)
                .WithMessage($"RecurringRoots.Deleted cannot contain more than {SyncLimits.PushMaxRecurringRoots} items.");

            // ─────────────────────────────────────────────────────────────────
            // Per-collection size limits: RecurringSeries
            // ─────────────────────────────────────────────────────────────────

            RuleFor(x => x.RecurringSeries.Created)
                .Must(list => list.Count <= SyncLimits.PushMaxRecurringSeries)
                .WithMessage($"RecurringSeries.Created cannot contain more than {SyncLimits.PushMaxRecurringSeries} items.");

            RuleFor(x => x.RecurringSeries.Updated)
                .Must(list => list.Count <= SyncLimits.PushMaxRecurringSeries)
                .WithMessage($"RecurringSeries.Updated cannot contain more than {SyncLimits.PushMaxRecurringSeries} items.");

            RuleFor(x => x.RecurringSeries.Deleted)
                .Must(list => list.Count <= SyncLimits.PushMaxRecurringSeries)
                .WithMessage($"RecurringSeries.Deleted cannot contain more than {SyncLimits.PushMaxRecurringSeries} items.");

            // ─────────────────────────────────────────────────────────────────
            // Per-collection size limits: RecurringSeriesSubtasks
            // ─────────────────────────────────────────────────────────────────

            RuleFor(x => x.RecurringSeriesSubtasks.Created)
                .Must(list => list.Count <= SyncLimits.PushMaxRecurringSeriesSubtasks)
                .WithMessage($"RecurringSeriesSubtasks.Created cannot contain more than {SyncLimits.PushMaxRecurringSeriesSubtasks} items.");

            RuleFor(x => x.RecurringSeriesSubtasks.Updated)
                .Must(list => list.Count <= SyncLimits.PushMaxRecurringSeriesSubtasks)
                .WithMessage($"RecurringSeriesSubtasks.Updated cannot contain more than {SyncLimits.PushMaxRecurringSeriesSubtasks} items.");

            RuleFor(x => x.RecurringSeriesSubtasks.Deleted)
                .Must(list => list.Count <= SyncLimits.PushMaxRecurringSeriesSubtasks)
                .WithMessage($"RecurringSeriesSubtasks.Deleted cannot contain more than {SyncLimits.PushMaxRecurringSeriesSubtasks} items.");

            // ─────────────────────────────────────────────────────────────────
            // Per-collection size limits: RecurringExceptions
            // ─────────────────────────────────────────────────────────────────

            RuleFor(x => x.RecurringExceptions.Created)
                .Must(list => list.Count <= SyncLimits.PushMaxRecurringExceptions)
                .WithMessage($"RecurringExceptions.Created cannot contain more than {SyncLimits.PushMaxRecurringExceptions} items.");

            RuleFor(x => x.RecurringExceptions.Updated)
                .Must(list => list.Count <= SyncLimits.PushMaxRecurringExceptions)
                .WithMessage($"RecurringExceptions.Updated cannot contain more than {SyncLimits.PushMaxRecurringExceptions} items.");

            RuleFor(x => x.RecurringExceptions.Deleted)
                .Must(list => list.Count <= SyncLimits.PushMaxRecurringExceptions)
                .WithMessage($"RecurringExceptions.Deleted cannot contain more than {SyncLimits.PushMaxRecurringExceptions} items.");

            // ─────────────────────────────────────────────────────────────────
            // Per-collection size limits: RecurringAttachments
            // ─────────────────────────────────────────────────────────────────

            RuleFor(x => x.RecurringAttachments.Deleted)
                .Must(list => list.Count <= SyncLimits.PushMaxRecurringAttachmentDeletes)
                .WithMessage($"RecurringAttachments.Deleted cannot contain more than {SyncLimits.PushMaxRecurringAttachmentDeletes} items.");


            // ─────────────────────────────────────────────────────────────────
            // Total items limit (across ALL collections)
            // ─────────────────────────────────────────────────────────────────

            RuleFor(x => x)
                .Must(cmd =>
                {
                    var total =
                        cmd.Tasks.Created.Count +
                        cmd.Tasks.Updated.Count +
                        cmd.Tasks.Deleted.Count +
                        cmd.Notes.Created.Count +
                        cmd.Notes.Updated.Count +
                        cmd.Notes.Deleted.Count +
                        cmd.Blocks.Created.Count +
                        cmd.Blocks.Updated.Count +
                        cmd.Blocks.Deleted.Count +
                        cmd.Categories.Created.Count +
                        cmd.Categories.Updated.Count +
                        cmd.Categories.Deleted.Count +
                        cmd.Subtasks.Created.Count +
                        cmd.Subtasks.Updated.Count +
                        cmd.Subtasks.Deleted.Count +
                        cmd.Attachments.Deleted.Count +
                        cmd.RecurringRoots.Created.Count +
                        cmd.RecurringRoots.Deleted.Count +
                        cmd.RecurringSeries.Created.Count +
                        cmd.RecurringSeries.Updated.Count +
                        cmd.RecurringSeries.Deleted.Count +
                        cmd.RecurringSeriesSubtasks.Created.Count +
                        cmd.RecurringSeriesSubtasks.Updated.Count +
                        cmd.RecurringSeriesSubtasks.Deleted.Count +
                        cmd.RecurringExceptions.Created.Count +
                        cmd.RecurringExceptions.Updated.Count +
                        cmd.RecurringExceptions.Deleted.Count +
                        cmd.RecurringAttachments.Deleted.Count;

                    return total <= SyncLimits.PushMaxTotalItems;
                })
                .WithMessage($"Total number of pushed items must not exceed {SyncLimits.PushMaxTotalItems}.");

            // ─────────────────────────────────────────────────────────────────
            // Individual item validation: Tasks
            // ─────────────────────────────────────────────────────────────────

            RuleForEach(x => x.Tasks.Created)
                .SetValidator(new TaskCreatedPushItemValidator());

            RuleForEach(x => x.Tasks.Updated)
                .SetValidator(new TaskUpdatedPushItemValidator());

            RuleForEach(x => x.Tasks.Deleted)
                .SetValidator(new TaskDeletedPushItemValidator());

            // ─────────────────────────────────────────────────────────────────
            // Individual item validation: Notes
            // ─────────────────────────────────────────────────────────────────

            RuleForEach(x => x.Notes.Created)
                .SetValidator(new NoteCreatedPushItemValidator());

            RuleForEach(x => x.Notes.Updated)
                .SetValidator(new NoteUpdatedPushItemValidator());

            RuleForEach(x => x.Notes.Deleted)
                .SetValidator(new NoteDeletedPushItemValidator());

            // ─────────────────────────────────────────────────────────────────
            // Individual item validation: Blocks
            // ─────────────────────────────────────────────────────────────────

            RuleForEach(x => x.Blocks.Created)
                .SetValidator(new BlockCreatedPushItemValidator());

            RuleForEach(x => x.Blocks.Updated)
                .SetValidator(new BlockUpdatedPushItemValidator());

            RuleForEach(x => x.Blocks.Deleted)
                .SetValidator(new BlockDeletedPushItemValidator());

            // ─────────────────────────────────────────────────────────────────
            // REFACTORED: Individual item validation: Categories
            // ─────────────────────────────────────────────────────────────────

            RuleForEach(x => x.Categories.Created)
                .SetValidator(new CategoryCreatedPushItemValidator());

            RuleForEach(x => x.Categories.Updated)
                .SetValidator(new CategoryUpdatedPushItemValidator());

            RuleForEach(x => x.Categories.Deleted)
                .SetValidator(new CategoryDeletedPushItemValidator());

            // ─────────────────────────────────────────────────────────────────
            // Individual item validation: Subtasks
            // ─────────────────────────────────────────────────────────────────

            RuleForEach(x => x.Subtasks.Created)
                .SetValidator(new SubtaskCreatedPushItemValidator());

            RuleForEach(x => x.Subtasks.Updated)
                .SetValidator(new SubtaskUpdatedPushItemValidator());

            RuleForEach(x => x.Subtasks.Deleted)
                .SetValidator(new SubtaskDeletedPushItemValidator());

            // ─────────────────────────────────────────────────────────────────
            // Individual item validation: Attachments
            // ─────────────────────────────────────────────────────────────────

            RuleForEach(x => x.Attachments.Deleted)
                .SetValidator(new AttachmentDeletedPushItemValidator());

            // ─────────────────────────────────────────────────────────────────
            // Individual item validation: RecurringRoots
            // ─────────────────────────────────────────────────────────────────

            RuleForEach(x => x.RecurringRoots.Created)
                .SetValidator(new RecurringRootCreatedPushItemValidator());

            RuleForEach(x => x.RecurringRoots.Deleted)
                .SetValidator(new RecurringRootDeletedPushItemValidator());

            // ─────────────────────────────────────────────────────────────────
            // Individual item validation: RecurringSeries
            // ─────────────────────────────────────────────────────────────────

            RuleForEach(x => x.RecurringSeries.Created)
                .SetValidator(new RecurringSeriesCreatedPushItemValidator());

            RuleForEach(x => x.RecurringSeries.Updated)
                .SetValidator(new RecurringSeriesUpdatedPushItemValidator());

            RuleForEach(x => x.RecurringSeries.Deleted)
                .SetValidator(new RecurringSeriesDeletedPushItemValidator());

            // ─────────────────────────────────────────────────────────────────
            // Individual item validation: RecurringSeriesSubtasks
            // ─────────────────────────────────────────────────────────────────

            RuleForEach(x => x.RecurringSeriesSubtasks.Created)
                .SetValidator(new RecurringSeriesSubtaskCreatedPushItemValidator());

            RuleForEach(x => x.RecurringSeriesSubtasks.Updated)
                .SetValidator(new RecurringSeriesSubtaskUpdatedPushItemValidator());

            RuleForEach(x => x.RecurringSeriesSubtasks.Deleted)
                .SetValidator(new RecurringSeriesSubtaskDeletedPushItemValidator());

            // ─────────────────────────────────────────────────────────────────
            // Individual item validation: RecurringExceptions
            // ─────────────────────────────────────────────────────────────────

            RuleForEach(x => x.RecurringExceptions.Created)
                .SetValidator(new RecurringExceptionCreatedPushItemValidator());

            RuleForEach(x => x.RecurringExceptions.Updated)
                .SetValidator(new RecurringExceptionUpdatedPushItemValidator());

            RuleForEach(x => x.RecurringExceptions.Deleted)
                .SetValidator(new RecurringExceptionDeletedPushItemValidator());

            // ─────────────────────────────────────────────────────────────────
            // Individual item validation: RecurringAttachments
            // ─────────────────────────────────────────────────────────────────

            RuleForEach(x => x.RecurringAttachments.Deleted)
                .SetValidator(new RecurringAttachmentDeletedPushItemValidator());
        }


        // ════════════════════════════════════════════════════════════════════
        // TASK VALIDATORS
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Validates <see cref="TaskCreatedPushItemDto"/> by:
        /// 1. Checking sync-specific rules (ClientId required)
        /// 2. Delegating field validation to <see cref="CreateTaskCommandValidator"/>
        /// </summary>
        private sealed class TaskCreatedPushItemValidator : AbstractValidator<TaskCreatedPushItemDto>
        {
            public TaskCreatedPushItemValidator()
            {
                RuleFor(x => x.ClientId)
                    .NotEmpty()
                    .WithMessage("ClientId is required for created items.");

                // REFACTORED: payload-shape rule (B1) — recurring-series link fields must be
                // mutually consistent. CanonicalOccurrenceDate is set iff exactly one of
                // {RecurringSeriesId, RecurringSeriesClientId} is non-empty. At most one of the
                // two id fields may be set. Closes the silent-drop gap in the handler.
                RuleFor(x => x)
                    .Must(x => !(x.RecurringSeriesId.HasValue && x.RecurringSeriesId.Value != Guid.Empty
                                 && x.RecurringSeriesClientId.HasValue && x.RecurringSeriesClientId.Value != Guid.Empty))
                    .WithMessage("Only one of RecurringSeriesId or RecurringSeriesClientId may be set.");

                RuleFor(x => x)
                    .Must(x =>
                    {
                        var hasSeriesId = x.RecurringSeriesId.HasValue && x.RecurringSeriesId.Value != Guid.Empty;
                        var hasSeriesClientId = x.RecurringSeriesClientId.HasValue && x.RecurringSeriesClientId.Value != Guid.Empty;
                        var hasSeriesRef = hasSeriesId || hasSeriesClientId;
                        var hasCanonical = x.CanonicalOccurrenceDate.HasValue;
                        return hasSeriesRef == hasCanonical;
                    })
                    .WithMessage("CanonicalOccurrenceDate must be set together with a recurring series reference.");

                // Delegate field-level validation to existing command validator
                var innerValidator = new CreateTaskCommandValidator();

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

                    var result = innerValidator.Validate(command);

                    foreach (var error in result.Errors)
                    {
                        context.AddFailure(error.PropertyName, error.ErrorMessage);
                    }
                });
            }
        }


        /// <summary>
        /// Validates <see cref="TaskUpdatedPushItemDto"/> by:
        /// 1. Checking sync-specific rules (Id required, ExpectedVersion >= 1)
        /// 2. Delegating field validation to <see cref="UpdateTaskCommandValidator"/>
        /// </summary>
        private sealed class TaskUpdatedPushItemValidator : AbstractValidator<TaskUpdatedPushItemDto>
        {
             public TaskUpdatedPushItemValidator()
            {
                RuleFor(x => x.Id)
                    .NotEmpty()
                    .WithMessage("Id is required for updated items.");

                RuleFor(x => x.ExpectedVersion)
                    .GreaterThanOrEqualTo(1)
                    .WithMessage("ExpectedVersion must be at least 1.");

                var innerValidator = new UpdateTaskCommandValidator();

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

                    var result = innerValidator.Validate(command);

                    foreach (var error in result.Errors)
                    {
                        // Remap TaskId -> Id for consistent error reporting
                        var propertyName = error.PropertyName == "TaskId" ? "Id" : error.PropertyName;
                        context.AddFailure(propertyName, error.ErrorMessage);
                    }
                });
            }
        }

        /// <summary>
        /// Validates <see cref="TaskDeletedPushItemDto"/> by:
        /// 1. Delegating field validation to <see cref="DeleteTaskCommandValidator"/>
        /// 
        /// Note: The handler uses "delete wins" semantics, but we still validate
        /// to ensure consistency if DeleteTaskCommandValidator adds more rules later.
        /// </summary>
        private sealed class TaskDeletedPushItemValidator : AbstractValidator<TaskDeletedPushItemDto>
        {
            public TaskDeletedPushItemValidator()
            {
                var innerValidator = new DeleteTaskCommandValidator();

                RuleFor(x => x).Custom((dto, context) =>
                {
                    var command = new DeleteTaskCommand
                    {
                        TaskId = dto.Id
                    };

                    var result = innerValidator.Validate(command);

                    foreach (var error in result.Errors)
                    {
                        // Remap TaskId -> Id for consistent error reporting
                        var propertyName = error.PropertyName == "TaskId" ? "Id" : error.PropertyName;
                        context.AddFailure(propertyName, error.ErrorMessage);
                    }
                });
            }
        }



        // ════════════════════════════════════════════════════════════════════
        // NOTE VALIDATORS
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Validates <see cref="NoteCreatedPushItemDto"/> by:
        /// 1. Checking sync-specific rules (ClientId required)
        /// 2. Delegating field validation to <see cref="CreateNoteCommandValidator"/>
        /// </summary>
        private sealed class NoteCreatedPushItemValidator : AbstractValidator<NoteCreatedPushItemDto>
        {
            public NoteCreatedPushItemValidator()
            {
                RuleFor(x => x.ClientId)
                    .NotEmpty()
                    .WithMessage("ClientId is required for created items.");

                var innerValidator = new CreateNoteCommandValidator();

                RuleFor(x => x).Custom((dto, context) =>
                {
                    var command = new CreateNoteCommand
                    {
                        Date = dto.Date,
                        Title = dto.Title,
                        Summary = dto.Summary,
                        Tags = dto.Tags
                    };

                    var result = innerValidator.Validate(command);

                    foreach (var error in result.Errors)
                    {
                        context.AddFailure(error.PropertyName, error.ErrorMessage);
                    }
                });
            }
        }


        /// <summary>
        /// Validates <see cref="NoteUpdatedPushItemDto"/> by:
        /// 1. Checking sync-specific rules (Id required, ExpectedVersion >= 1)
        /// 2. Delegating field validation to <see cref="UpdateNoteCommandValidator"/>
        /// </summary>
        private sealed class NoteUpdatedPushItemValidator : AbstractValidator<NoteUpdatedPushItemDto>
        {
            public NoteUpdatedPushItemValidator()
            {
                RuleFor(x => x.Id)
                    .NotEmpty()
                    .WithMessage("Id is required for updated items.");

                RuleFor(x => x.ExpectedVersion)
                    .GreaterThanOrEqualTo(1)
                    .WithMessage("ExpectedVersion must be at least 1.");

                var innerValidator = new UpdateNoteCommandValidator();

                RuleFor(x => x).Custom((dto, context) =>
                {
                    var command = new UpdateNoteCommand
                    {
                        NoteId = dto.Id,
                        Date = dto.Date,
                        Title = dto.Title,
                        Summary = dto.Summary,
                        Tags = dto.Tags
                    };

                    var result = innerValidator.Validate(command);

                    foreach (var error in result.Errors)
                    {
                        // Remap NoteId -> Id for consistent error reporting
                        var propertyName = error.PropertyName == "NoteId" ? "Id" : error.PropertyName;
                        context.AddFailure(propertyName, error.ErrorMessage);
                    }
                });
            }
        }

        /// <summary>
        /// Validates <see cref="NoteDeletedPushItemDto"/> by:
        /// 1. Delegating field validation to <see cref="DeleteNoteCommandValidator"/>
        /// 
        /// Note: The handler uses "delete wins" semantics, but we still validate
        /// to ensure consistency if DeleteNoteCommandValidator adds more rules later.
        /// </summary>
        private sealed class NoteDeletedPushItemValidator : AbstractValidator<NoteDeletedPushItemDto>
        {
            public NoteDeletedPushItemValidator()
            {
                var innerValidator = new DeleteNoteCommandValidator();

                RuleFor(x => x).Custom((dto, context) =>
                {
                    var command = new DeleteNoteCommand { NoteId = dto.Id };

                    var result = innerValidator.Validate(command);

                    foreach (var error in result.Errors)
                    {
                        // Remap NoteId -> Id for consistent error reporting
                        var propertyName = error.PropertyName == "NoteId" ? "Id" : error.PropertyName;
                        context.AddFailure(propertyName, error.ErrorMessage);
                    }
                });
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // BLOCK VALIDATORS
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Validates <see cref="BlockCreatedPushItemDto"/> by:
        /// 1. Checking sync-specific rules (ClientId required, ParentId/ParentClientId)
        /// 2. Delegating field validation to <see cref="CreateBlockCommandValidator"/>
        /// </summary>
        private sealed class BlockCreatedPushItemValidator : AbstractValidator<BlockCreatedPushItemDto>
        {
            public BlockCreatedPushItemValidator()
            {
                // Sync-specific: client id must be provided for creates
                RuleFor(x => x.ClientId)
                    .NotEmpty()
                    .WithMessage("ClientId is required for created blocks.");

                // Sync-specific: must have exactly one of ParentId OR ParentClientId
                // (ParentClientId is used when parent is also being created in this sync).
                // REFACTORED (B4): tightened from OR to exactly-one to close the silent-pick gap
                // where both being set caused the handler to silently prefer ParentId.
                RuleFor(x => x)
                    .Must(x =>
                    {
                        var hasParentId = x.ParentId.HasValue && x.ParentId.Value != Guid.Empty;
                        var hasParentClientId = x.ParentClientId.HasValue && x.ParentClientId.Value != Guid.Empty;
                        return hasParentId ^ hasParentClientId;
                    })
                    .WithMessage("Exactly one of ParentId or ParentClientId must be provided.");

                // Delegate field-level validation to CreateBlockCommandValidator
                var innerValidator = new CreateBlockCommandValidator();

                RuleFor(x => x).Custom((dto, context) =>
                {
                    var command = new CreateBlockCommand
                    {
                        ParentId = dto.ParentId ?? Guid.Empty,
                        ParentType = dto.ParentType,
                        Type = dto.Type,
                        Position = dto.Position,
                        TextContent = dto.TextContent,
                        AssetClientId = dto.AssetClientId,
                        AssetFileName = dto.AssetFileName,
                        AssetContentType = dto.AssetContentType,
                        AssetSizeBytes = dto.AssetSizeBytes
                    };

                    var result = innerValidator.Validate(command);

                    foreach (var error in result.Errors)
                    {
                        // Skip ParentId validation from inner validator when using ParentClientId
                        // (sync allows ParentClientId for newly created parents)
                        if (error.PropertyName == "ParentId" &&
                            dto.ParentClientId.HasValue &&
                            dto.ParentClientId != Guid.Empty)
                        {
                            continue;
                        }

                        context.AddFailure(error.PropertyName, error.ErrorMessage);
                    }
                });
            }
        }

        /// <summary>
        /// Validates <see cref="BlockUpdatedPushItemDto"/> by:
        /// 1. Checking sync-specific rules (Id required, ExpectedVersion >= 1)
        /// 2. Delegating field validation to <see cref="UpdateBlockCommandValidator"/>
        /// </summary>
        private sealed class BlockUpdatedPushItemValidator : AbstractValidator<BlockUpdatedPushItemDto>
        {
            public BlockUpdatedPushItemValidator()
            {
                // Sync-specific rules
                RuleFor(x => x.Id)
                    .NotEmpty()
                    .WithMessage("Id is required for updated blocks.");

                RuleFor(x => x.ExpectedVersion)
                    .GreaterThanOrEqualTo(1)
                    .WithMessage("ExpectedVersion must be at least 1.");

                // Delegate field-level validation to UpdateBlockCommandValidator
                var innerValidator = new UpdateBlockCommandValidator();

                RuleFor(x => x).Custom((dto, context) =>
                {
                    var command = new UpdateBlockCommand
                    {
                        BlockId = dto.Id,
                        Position = dto.Position,
                        TextContent = dto.TextContent
                    };

                    var result = innerValidator.Validate(command);

                    foreach (var error in result.Errors)
                    {
                        // Remap BlockId -> Id for consistent error reporting
                        var propertyName = error.PropertyName == "BlockId" ? "Id" : error.PropertyName;
                        context.AddFailure(propertyName, error.ErrorMessage);
                    }
                });
            }
        }

        /// <summary>
        /// Validates <see cref="BlockDeletedPushItemDto"/> by:
        /// 1. Delegating field validation to <see cref="DeleteBlockCommandValidator"/>
        /// 
        /// Note: The handler uses "delete wins" semantics, but we still validate
        /// to ensure consistency if DeleteBlockCommandValidator adds more rules later.
        /// </summary>
        private sealed class BlockDeletedPushItemValidator : AbstractValidator<BlockDeletedPushItemDto>
        {
            public BlockDeletedPushItemValidator()
            {
                var innerValidator = new DeleteBlockCommandValidator();

                RuleFor(x => x).Custom((dto, context) =>
                {
                    var command = new DeleteBlockCommand
                    {
                        BlockId = dto.Id
                    };

                    var result = innerValidator.Validate(command);

                    foreach (var error in result.Errors)
                    {
                        // Remap BlockId -> Id for consistent error reporting
                        var propertyName = error.PropertyName == "BlockId" ? "Id" : error.PropertyName;
                        context.AddFailure(propertyName, error.ErrorMessage);
                    }
                });
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // REFACTORED: CATEGORY VALIDATORS (task categories feature)
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Validates <see cref="CategoryCreatedPushItemDto"/>:
        /// - ClientId must be non-empty
        /// - Name must be non-empty and at most <see cref="TaskCategory.MaxNameLength"/> characters
        /// </summary>
        private sealed class CategoryCreatedPushItemValidator : AbstractValidator<CategoryCreatedPushItemDto>
        {
            public CategoryCreatedPushItemValidator()
            {
                RuleFor(x => x.ClientId)
                    .NotEmpty()
                    .WithMessage("ClientId is required for created categories.");

                RuleFor(x => x.Name)
                    .NotEmpty()
                    .WithMessage("Category name is required.")
                    .MaximumLength(TaskCategory.MaxNameLength)
                    .WithMessage($"Category name cannot exceed {TaskCategory.MaxNameLength} characters.");
            }
        }

        /// <summary>
        /// Validates <see cref="CategoryUpdatedPushItemDto"/>:
        /// - Id must be non-empty
        /// - ExpectedVersion must be &gt;= 1
        /// - Name must be non-empty and at most <see cref="TaskCategory.MaxNameLength"/> characters
        /// </summary>
        private sealed class CategoryUpdatedPushItemValidator : AbstractValidator<CategoryUpdatedPushItemDto>
        {
            public CategoryUpdatedPushItemValidator()
            {
                RuleFor(x => x.Id)
                    .NotEmpty()
                    .WithMessage("Id is required for updated categories.");

                RuleFor(x => x.ExpectedVersion)
                    .GreaterThanOrEqualTo(1)
                    .WithMessage("ExpectedVersion must be at least 1.");

                RuleFor(x => x.Name)
                    .NotEmpty()
                    .WithMessage("Category name is required.")
                    .MaximumLength(TaskCategory.MaxNameLength)
                    .WithMessage($"Category name cannot exceed {TaskCategory.MaxNameLength} characters.");
            }
        }

        /// <summary>
        /// Validates <see cref="CategoryDeletedPushItemDto"/>:
        /// - Id must be non-empty
        /// </summary>
        private sealed class CategoryDeletedPushItemValidator : AbstractValidator<CategoryDeletedPushItemDto>
        {
            public CategoryDeletedPushItemValidator()
            {
                RuleFor(x => x.Id)
                    .NotEmpty()
                    .WithMessage("Id is required for deleted categories.");
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // SUBTASK VALIDATORS
        // ════════════════════════════════════════════════════════════════════

        private sealed class SubtaskCreatedPushItemValidator : AbstractValidator<SubtaskCreatedPushItemDto>
        {
            public SubtaskCreatedPushItemValidator()
            {
                RuleFor(x => x.ClientId)
                    .NotEmpty()
                    .WithMessage("ClientId is required for created subtasks.");

                // Sync-specific: must have exactly one of TaskId (existing task) OR TaskClientId
                // (created in same push).
                // REFACTORED (B5): tightened from OR to exactly-one to close the silent-pick gap
                // where both being set caused the handler to silently prefer TaskClientId.
                RuleFor(x => x)
                    .Must(x =>
                    {
                        var hasTaskId = x.TaskId.HasValue && x.TaskId.Value != Guid.Empty;
                        var hasTaskClientId = x.TaskClientId.HasValue && x.TaskClientId.Value != Guid.Empty;
                        return hasTaskId ^ hasTaskClientId;
                    })
                    .WithMessage("Exactly one of TaskId or TaskClientId must be provided.");

                RuleFor(x => x.Text)
                    .NotEmpty()
                    .WithMessage("Subtask text is required.")
                    .MaximumLength(Subtask.MaxTextLength)
                    .WithMessage($"Subtask text cannot exceed {Subtask.MaxTextLength} characters.");

                RuleFor(x => x.Position)
                    .NotEmpty()
                    .WithMessage("Position is required.")
                    .MaximumLength(Subtask.MaxPositionLength)
                    .WithMessage($"Position cannot exceed {Subtask.MaxPositionLength} characters.");
            }
        }

        private sealed class SubtaskUpdatedPushItemValidator : AbstractValidator<SubtaskUpdatedPushItemDto>
        {
            public SubtaskUpdatedPushItemValidator()
            {
                RuleFor(x => x.Id)
                    .NotEmpty()
                    .WithMessage("Id is required for updated subtasks.");

                RuleFor(x => x.ExpectedVersion)
                    .GreaterThanOrEqualTo(1)
                    .WithMessage("ExpectedVersion must be at least 1.");

                // Null means "no change"; non-null must be valid
                RuleFor(x => x.Text)
                    .NotEmpty()
                    .WithMessage("Subtask text cannot be empty.")
                    .MaximumLength(Subtask.MaxTextLength)
                    .WithMessage($"Subtask text cannot exceed {Subtask.MaxTextLength} characters.")
                    .When(x => x.Text is not null);

                RuleFor(x => x.Position)
                    .NotEmpty()
                    .WithMessage("Position cannot be empty.")
                    .MaximumLength(Subtask.MaxPositionLength)
                    .WithMessage($"Position cannot exceed {Subtask.MaxPositionLength} characters.")
                    .When(x => x.Position is not null);
            }
        }

        private sealed class SubtaskDeletedPushItemValidator : AbstractValidator<SubtaskDeletedPushItemDto>
        {
            public SubtaskDeletedPushItemValidator()
            {
                RuleFor(x => x.Id)
                    .NotEmpty()
                    .WithMessage("Id is required for deleted subtasks.");
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // ATTACHMENT VALIDATORS
        // ════════════════════════════════════════════════════════════════════

        private sealed class AttachmentDeletedPushItemValidator : AbstractValidator<AttachmentDeletedPushItemDto>
        {
            public AttachmentDeletedPushItemValidator()
            {
                RuleFor(x => x.Id)
                    .NotEmpty()
                    .WithMessage("Id is required for deleted attachments.");
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // RECURRING ROOT VALIDATORS
        // ════════════════════════════════════════════════════════════════════

        private sealed class RecurringRootCreatedPushItemValidator : AbstractValidator<RecurringRootCreatedPushItemDto>
        {
            public RecurringRootCreatedPushItemValidator()
            {
                RuleFor(x => x.ClientId)
                    .NotEmpty()
                    .WithMessage("ClientId is required for created recurring roots.");
            }
        }

        private sealed class RecurringRootDeletedPushItemValidator : AbstractValidator<RecurringRootDeletedPushItemDto>
        {
            public RecurringRootDeletedPushItemValidator()
            {
                RuleFor(x => x.Id)
                    .NotEmpty()
                    .WithMessage("Id is required for deleted recurring roots.");
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // RECURRING SERIES VALIDATORS
        // ════════════════════════════════════════════════════════════════════

        private sealed class RecurringSeriesCreatedPushItemValidator : AbstractValidator<RecurringSeriesCreatedPushItemDto>
        {
            public RecurringSeriesCreatedPushItemValidator()
            {
                RuleFor(x => x.ClientId)
                    .NotEmpty()
                    .WithMessage("ClientId is required for created recurring series.");

                // Sync-specific: must have either RootId (existing root) OR RootClientId (created in same push)
                RuleFor(x => x)
                    .Must(x => x.RootId.HasValue && x.RootId != Guid.Empty ||
                               x.RootClientId.HasValue && x.RootClientId != Guid.Empty)
                    .WithMessage("Either RootId or RootClientId must be provided.");

                RuleFor(x => x.RRuleString)
                    .NotEmpty()
                    .WithMessage("RRuleString is required.")
                    .MaximumLength(RecurringTaskSeries.MaxRRuleStringLength)
                    .WithMessage($"RRuleString cannot exceed {RecurringTaskSeries.MaxRRuleStringLength} characters.");

                RuleFor(x => x.StartsOnDate)
                    .Must(d => d != default)
                    .WithMessage("StartsOnDate must be a valid date.");

                RuleFor(x => x.Title)
                    .NotEmpty()
                    .WithMessage("Title is required.")
                    .MaximumLength(RecurringTaskSeries.MaxTitleLength)
                    .WithMessage($"Title cannot exceed {RecurringTaskSeries.MaxTitleLength} characters.");
            }
        }

        private sealed class RecurringSeriesUpdatedPushItemValidator : AbstractValidator<RecurringSeriesUpdatedPushItemDto>
        {
            public RecurringSeriesUpdatedPushItemValidator()
            {
                RuleFor(x => x.Id)
                    .NotEmpty()
                    .WithMessage("Id is required for updated recurring series.");

                RuleFor(x => x.ExpectedVersion)
                    .GreaterThanOrEqualTo(1)
                    .WithMessage("ExpectedVersion must be at least 1.");

                RuleFor(x => x.Title)
                    .NotEmpty()
                    .WithMessage("Title is required.")
                    .MaximumLength(RecurringTaskSeries.MaxTitleLength)
                    .WithMessage($"Title cannot exceed {RecurringTaskSeries.MaxTitleLength} characters.");
            }
        }

        private sealed class RecurringSeriesDeletedPushItemValidator : AbstractValidator<RecurringSeriesDeletedPushItemDto>
        {
            public RecurringSeriesDeletedPushItemValidator()
            {
                RuleFor(x => x.Id)
                    .NotEmpty()
                    .WithMessage("Id is required for deleted recurring series.");
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // RECURRING SERIES SUBTASK VALIDATORS
        // ════════════════════════════════════════════════════════════════════

        private sealed class RecurringSeriesSubtaskCreatedPushItemValidator : AbstractValidator<RecurringSubtaskCreatedPushItemDto>
        {
            public RecurringSeriesSubtaskCreatedPushItemValidator()
            {
                RuleFor(x => x.ClientId)
                    .NotEmpty()
                    .WithMessage("ClientId is required for created recurring subtasks.");

                // Sync-specific: must have exactly one of {SeriesId, SeriesClientId, ExceptionId}.
                // REFACTORED (B3): tightened from OR (any) to exactly-one to close the silent-pick
                // gap where the handler silently prefers ExceptionId, then SeriesClientId, then
                // SeriesId. The DB check constraint on the entity already enforces this; the
                // validator now rejects malformed payloads at intake instead.
                RuleFor(x => x)
                    .Must(x =>
                    {
                        var hasSeriesId = x.SeriesId.HasValue && x.SeriesId.Value != Guid.Empty;
                        var hasSeriesClientId = x.SeriesClientId.HasValue && x.SeriesClientId.Value != Guid.Empty;
                        var hasExceptionId = x.ExceptionId.HasValue && x.ExceptionId.Value != Guid.Empty;
                        var count = (hasSeriesId ? 1 : 0) + (hasSeriesClientId ? 1 : 0) + (hasExceptionId ? 1 : 0);
                        return count == 1;
                    })
                    .WithMessage("Exactly one of SeriesId, SeriesClientId, or ExceptionId must be provided.");

                RuleFor(x => x.Text)
                    .NotEmpty()
                    .WithMessage("Subtask text is required.")
                    .MaximumLength(RecurringTaskSubtask.MaxTextLength)
                    .WithMessage($"Subtask text cannot exceed {RecurringTaskSubtask.MaxTextLength} characters.");

                RuleFor(x => x.Position)
                    .NotEmpty()
                    .WithMessage("Position is required.")
                    .MaximumLength(RecurringTaskSubtask.MaxPositionLength)
                    .WithMessage($"Position cannot exceed {RecurringTaskSubtask.MaxPositionLength} characters.");
            }
        }

        private sealed class RecurringSeriesSubtaskUpdatedPushItemValidator : AbstractValidator<RecurringSubtaskUpdatedPushItemDto>
        {
            public RecurringSeriesSubtaskUpdatedPushItemValidator()
            {
                RuleFor(x => x.Id)
                    .NotEmpty()
                    .WithMessage("Id is required for updated recurring subtasks.");

                RuleFor(x => x.ExpectedVersion)
                    .GreaterThanOrEqualTo(1)
                    .WithMessage("ExpectedVersion must be at least 1.");

                // Null means "no change"; non-null must be valid
                RuleFor(x => x.Text)
                    .NotEmpty()
                    .WithMessage("Subtask text cannot be empty.")
                    .MaximumLength(RecurringTaskSubtask.MaxTextLength)
                    .WithMessage($"Subtask text cannot exceed {RecurringTaskSubtask.MaxTextLength} characters.")
                    .When(x => x.Text is not null);

                RuleFor(x => x.Position)
                    .NotEmpty()
                    .WithMessage("Position cannot be empty.")
                    .MaximumLength(RecurringTaskSubtask.MaxPositionLength)
                    .WithMessage($"Position cannot exceed {RecurringTaskSubtask.MaxPositionLength} characters.")
                    .When(x => x.Position is not null);
            }
        }

        private sealed class RecurringSeriesSubtaskDeletedPushItemValidator : AbstractValidator<RecurringSubtaskDeletedPushItemDto>
        {
            public RecurringSeriesSubtaskDeletedPushItemValidator()
            {
                RuleFor(x => x.Id)
                    .NotEmpty()
                    .WithMessage("Id is required for deleted recurring subtasks.");
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // RECURRING EXCEPTION VALIDATORS
        // ════════════════════════════════════════════════════════════════════

        private sealed class RecurringExceptionCreatedPushItemValidator : AbstractValidator<RecurringExceptionCreatedPushItemDto>
        {
            public RecurringExceptionCreatedPushItemValidator()
            {
                RuleFor(x => x.ClientId)
                    .NotEmpty()
                    .WithMessage("ClientId is required for created recurring exceptions.");

                RuleFor(x => x.SeriesId)
                    .NotEmpty()
                    .WithMessage("SeriesId is required for created recurring exceptions.");

                RuleFor(x => x.OccurrenceDate)
                    .Must(d => d != default)
                    .WithMessage("OccurrenceDate must be a valid date.");

                // REFACTORED (B2): payload-shape rule — when IsDeletion is true, all Override*
                // fields must be null. CreateDeletion ignores them in the handler, but the
                // validator should reject malformed payloads at intake.
                RuleFor(x => x)
                    .Must(x => !x.IsDeletion ||
                               (x.OverrideTitle is null
                                && x.OverrideDescription is null
                                && x.OverrideDate is null
                                && x.OverrideStartTime is null
                                && x.OverrideEndTime is null
                                && x.OverrideLocation is null
                                && x.OverrideTravelTime is null
                                && x.OverrideCategoryId is null
                                && x.OverridePriority is null
                                && x.OverrideMeetingLink is null
                                && x.OverrideReminderAtUtc is null))
                    .WithMessage("Override fields must be null when IsDeletion is true.");
            }
        }

        private sealed class RecurringExceptionUpdatedPushItemValidator : AbstractValidator<RecurringExceptionUpdatedPushItemDto>
        {
            public RecurringExceptionUpdatedPushItemValidator()
            {
                RuleFor(x => x.Id)
                    .NotEmpty()
                    .WithMessage("Id is required for updated recurring exceptions.");

                RuleFor(x => x.ExpectedVersion)
                    .GreaterThanOrEqualTo(1)
                    .WithMessage("ExpectedVersion must be at least 1.");
            }
        }

        private sealed class RecurringExceptionDeletedPushItemValidator : AbstractValidator<RecurringExceptionDeletedPushItemDto>
        {
            public RecurringExceptionDeletedPushItemValidator()
            {
                RuleFor(x => x.Id)
                    .NotEmpty()
                    .WithMessage("Id is required for deleted recurring exceptions.");
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // RECURRING ATTACHMENT VALIDATORS
        // ════════════════════════════════════════════════════════════════════

        private sealed class RecurringAttachmentDeletedPushItemValidator : AbstractValidator<RecurringAttachmentDeletedPushItemDto>
        {
            public RecurringAttachmentDeletedPushItemValidator()
            {
                RuleFor(x => x.Id)
                    .NotEmpty()
                    .WithMessage("Id is required for deleted recurring attachments.");
            }
        }
    }
}
