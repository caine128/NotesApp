using FluentValidation;
using NotesApp.Application.Blocks.Commands.UpdateBlock;
using NotesApp.Application.Notes.Commands.UpdateNote;
using NotesApp.Application.Sync.Models;
using NotesApp.Application.Tasks.Commands.UpdateTask;
using NotesApp.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Application.Sync.Commands.ResolveConflicts
{
    /// <summary>
    /// Validator for <see cref="ResolveSyncConflictsCommand"/>.
    /// 
    /// - At least one resolution must be present.
    /// - EntityType must be Task, Note, or Block.
    /// - Choice must be KeepClient, KeepServer, or Merge.
    /// - ExpectedVersion >= 1.
    /// - For tasks/notes/blocks, required data must be present for keep_client/merge.
    /// - Reuses UpdateTaskCommandValidator / UpdateNoteCommandValidator / UpdateBlockCommandValidator
    ///   to validate the provided TaskData / NoteData / BlockData when applicable.
    /// </summary>
    public sealed class ResolveSyncConflictsCommandValidator
        : AbstractValidator<ResolveSyncConflictsCommand>
    {
        public ResolveSyncConflictsCommandValidator()
        {
            RuleFor(c => c.Request.Resolutions)
                .NotNull()
                .Must(r => r.Any())
                .WithMessage("At least one resolution is required.");

            RuleForEach(c => c.Request.Resolutions)
                .SetValidator(new SyncConflictResolutionDtoValidator());
        }

        private sealed class SyncConflictResolutionDtoValidator
            : AbstractValidator<SyncConflictResolutionDto>
        {
            public SyncConflictResolutionDtoValidator()
            {
                RuleFor(x => x.EntityId)
                    .NotEmpty();

                RuleFor(x => x.EntityType)
                    .IsInEnum()
                    .WithMessage("EntityType must be Task, Note, or Block.");

                RuleFor(x => x.Choice)
                    .IsInEnum()
                    .WithMessage("Choice must be KeepClient, KeepServer, or Merge.");

                RuleFor(x => x.ExpectedVersion)
                    .GreaterThanOrEqualTo(1);

                // ─────────────────────────────────────────────────────────────────
                // Task validation
                // ─────────────────────────────────────────────────────────────────

                When(x => x.EntityType == SyncEntityType.Task && x.Choice != SyncResolutionChoice.KeepServer, () =>
                {
                    RuleFor(x => x.TaskData)
                        .NotNull()
                        .WithMessage("TaskData must be provided for task resolutions when choice is not KeepServer.");

                    When(x => x.TaskData != null, () =>
                    {
                        var inner = new UpdateTaskCommandValidator();

                        RuleFor(x => x).Custom((dto, context) =>
                        {
                            var data = dto.TaskData!;
                            var command = new UpdateTaskCommand
                            {
                                TaskId = dto.EntityId,
                                Date = data.Date,
                                Title = data.Title,
                                Description = data.Description,
                                StartTime = data.StartTime,
                                EndTime = data.EndTime,
                                Location = data.Location,
                                TravelTime = data.TravelTime,
                                ReminderAtUtc = data.ReminderAtUtc
                            };

                            var result = inner.Validate(command);

                            foreach (var error in result.Errors)
                            {
                                context.AddFailure(error.PropertyName, error.ErrorMessage);
                            }
                        });
                    });
                });

                // ─────────────────────────────────────────────────────────────────
                // Note validation (Content removed - content is now in blocks)
                // ─────────────────────────────────────────────────────────────────

                When(x => x.EntityType == SyncEntityType.Note && x.Choice != SyncResolutionChoice.KeepServer, () =>
                {
                    RuleFor(x => x.NoteData)
                        .NotNull()
                        .WithMessage("NoteData must be provided for note resolutions when choice is not KeepServer.");

                    When(x => x.NoteData != null, () =>
                    {
                        var inner = new UpdateNoteCommandValidator();

                        RuleFor(x => x).Custom((dto, context) =>
                        {
                            var data = dto.NoteData!;
                            var command = new UpdateNoteCommand
                            {
                                NoteId = dto.EntityId,
                                Date = data.Date,
                                Title = data.Title,
                                Summary = data.Summary,
                                Tags = data.Tags
                            };

                            var result = inner.Validate(command);

                            foreach (var error in result.Errors)
                            {
                                context.AddFailure(error.PropertyName, error.ErrorMessage);
                            }
                        });
                    });
                });

                // ─────────────────────────────────────────────────────────────────
                // Block validation (NEW)
                // ─────────────────────────────────────────────────────────────────
                When(x => x.EntityType == SyncEntityType.Block && x.Choice != SyncResolutionChoice.KeepServer, () =>
                {
                    RuleFor(x => x.BlockData)
                        .NotNull()
                        .WithMessage("BlockData must be provided for block resolutions when choice is not KeepServer.");

                    When(x => x.BlockData != null, () =>
                    {
                        var inner = new UpdateBlockCommandValidator();

                        RuleFor(x => x).Custom((dto, context) =>
                        {
                            var data = dto.BlockData!;
                            var command = new UpdateBlockCommand
                            {
                                BlockId = dto.EntityId,
                                Position = data.Position,
                                TextContent = data.TextContent
                            };

                            var result = inner.Validate(command);

                            foreach (var error in result.Errors)
                            {
                                context.AddFailure(error.PropertyName, error.ErrorMessage);
                            }
                        });
                    });
                });
            }
        }
    }
}
