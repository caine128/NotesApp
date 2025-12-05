using FluentValidation;
using NotesApp.Application.Notes.Commands.UpdateNote;
using NotesApp.Application.Sync.Models;
using NotesApp.Application.Tasks.Commands.UpdateTask;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Application.Sync.Commands.ResolveConflicts
{
    /// <summary>
    /// Validator for <see cref="ResolveSyncConflictsCommand"/>.
    /// 
    /// - At least one resolution must be present.
    /// - EntityType must be "task" or "note".
    /// - Choice must be "keep_client", "keep_server" or "merge".
    /// - ExpectedVersion >= 1.
    /// - For tasks/notes, required data must be present for keep_client/merge.
    /// - Reuses UpdateTaskCommandValidator / UpdateNoteCommandValidator
    ///   to validate the provided TaskData / NoteData when applicable.
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
            private static readonly string[] AllowedEntityTypes = { "task", "note" };
            private static readonly string[] AllowedChoices = { "keep_client", "keep_server", "merge" };

            public SyncConflictResolutionDtoValidator()
            {
                RuleFor(x => x.EntityId)
                    .NotEmpty();

                RuleFor(x => x.EntityType)
                    .Must(et => AllowedEntityTypes.Contains(et))
                    .WithMessage("EntityType must be 'task' or 'note'.");

                RuleFor(x => x.Choice)
                    .Must(c => AllowedChoices.Contains(c))
                    .WithMessage("Choice must be 'keep_client', 'keep_server' or 'merge'.");

                RuleFor(x => x.ExpectedVersion)
                    .GreaterThanOrEqualTo(1);

                When(x => x.EntityType == "task" && x.Choice != "keep_server", () =>
                {
                    RuleFor(x => x.TaskData)
                        .NotNull()
                        .WithMessage("TaskData must be provided for task resolutions when choice is not 'keep_server'.");

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

                When(x => x.EntityType == "note" && x.Choice != "keep_server", () =>
                {
                    RuleFor(x => x.NoteData)
                        .NotNull()
                        .WithMessage("NoteData must be provided for note resolutions when choice is not 'keep_server'.");

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
                                Content = data.Content,
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
            }
        }
    }
}
