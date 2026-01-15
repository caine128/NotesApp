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
    /// - Per-collection limits for Tasks, Notes, and Blocks
    /// - Total item limit across all collections
    /// - Individual item validation for creates, updates, and deletes
    /// 
    /// Strategy: Reuses existing Create*/Update*/Delete* command validators for field-level rules
    /// to maintain consistency between direct API calls and sync operations.
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
                .Must(list => list.Count <= SyncLimits.PushMaxItemsPerEntity)
                .WithMessage($"Tasks.Created cannot contain more than {SyncLimits.PushMaxItemsPerEntity} items.");

            RuleFor(x => x.Tasks.Updated)
                .Must(list => list.Count <= SyncLimits.PushMaxItemsPerEntity)
                .WithMessage($"Tasks.Updated cannot contain more than {SyncLimits.PushMaxItemsPerEntity} items.");

            RuleFor(x => x.Tasks.Deleted)
                .Must(list => list.Count <= SyncLimits.PushMaxItemsPerEntity)
                .WithMessage($"Tasks.Deleted cannot contain more than {SyncLimits.PushMaxItemsPerEntity} items.");

            // ─────────────────────────────────────────────────────────────────
            // Per-collection size limits: Notes
            // ─────────────────────────────────────────────────────────────────

            RuleFor(x => x.Notes.Created)
                .Must(list => list.Count <= SyncLimits.PushMaxItemsPerEntity)
                .WithMessage($"Notes.Created cannot contain more than {SyncLimits.PushMaxItemsPerEntity} items.");

            RuleFor(x => x.Notes.Updated)
                .Must(list => list.Count <= SyncLimits.PushMaxItemsPerEntity)
                .WithMessage($"Notes.Updated cannot contain more than {SyncLimits.PushMaxItemsPerEntity} items.");

            RuleFor(x => x.Notes.Deleted)
                .Must(list => list.Count <= SyncLimits.PushMaxItemsPerEntity)
                .WithMessage($"Notes.Deleted cannot contain more than {SyncLimits.PushMaxItemsPerEntity} items.");

            // ─────────────────────────────────────────────────────────────────
            // Per-collection size limits: Blocks
            // ─────────────────────────────────────────────────────────────────

            RuleFor(x => x.Blocks.Created)
                .Must(list => list.Count <= SyncLimits.PushMaxItemsPerEntity)
                .WithMessage($"Blocks.Created cannot contain more than {SyncLimits.PushMaxItemsPerEntity} items.");

            RuleFor(x => x.Blocks.Updated)
                .Must(list => list.Count <= SyncLimits.PushMaxItemsPerEntity)
                .WithMessage($"Blocks.Updated cannot contain more than {SyncLimits.PushMaxItemsPerEntity} items.");

            RuleFor(x => x.Blocks.Deleted)
                .Must(list => list.Count <= SyncLimits.PushMaxItemsPerEntity)
                .WithMessage($"Blocks.Deleted cannot contain more than {SyncLimits.PushMaxItemsPerEntity} items.");


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
                        cmd.Blocks.Deleted.Count;

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
                        Content = dto.Content,
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
                        Content = dto.Content,
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
                    var command = new DeleteNoteCommand(dto.Id);

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

                // Sync-specific: must have either ParentId OR ParentClientId
                // (ParentClientId is used when parent is also being created in this sync)
                RuleFor(x => x)
                    .Must(x => x.ParentId.HasValue && x.ParentId != Guid.Empty ||
                               x.ParentClientId.HasValue && x.ParentClientId != Guid.Empty)
                    .WithMessage("Either ParentId or ParentClientId must be provided.");

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
    }
}
