using FluentResults;
using FluentValidation;
using MediatR;
using NotesApp.Application.Abstractions.Persistence;
using NotesApp.Application.Common;
using NotesApp.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Application.Tasks.Commands.CreateTask
{
    public sealed class CreateTaskCommandHandler: IRequestHandler<CreateTaskCommand, Result<TaskDto>>
    {
        private readonly ITaskRepository _taskRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly ISystemClock _clock;
        private readonly IValidator<CreateTaskCommand> _validator;

        public CreateTaskCommandHandler(ITaskRepository taskRepository,
                                        IUnitOfWork unitOfWork,
                                        ISystemClock clock,
                                        IValidator<CreateTaskCommand> validator)
        {
            _taskRepository = taskRepository;
            _unitOfWork = unitOfWork;
            _clock = clock;
            _validator = validator;
        }

        public async Task<Result<TaskDto>> Handle(CreateTaskCommand command,
                                                  CancellationToken cancellationToken)
        {
            // 1) Application-level validation (shape, basic rules)
            var validationResult = await _validator.ValidateAsync(command, cancellationToken);
            if (!validationResult.IsValid)
            {
                var errors = validationResult.Errors
                    .Select(e => new Error(e.ErrorMessage).WithMetadata("Property", e.PropertyName))
                    .ToList();

                return Result.Fail<TaskDto>(errors);
            }

            // 2) Domain creation (invariants, domain rules)
            var utcNow = _clock.UtcNow;

            var domainResult = TaskItem.Create(command.UserId,
                                               command.Date,
                                               command.Title,
                                               utcNow);

            if (domainResult.IsFailure || domainResult.Value is null)
            {
                // Convert DomainResult<TaskItem> -> Result<TaskDto>
                return domainResult.ToResult<TaskItem, TaskDto>(MapToDto);
            }

            var taskItem = domainResult.Value;

            // 3) Set reminder if provided
            if (command.ReminderAtUtc.HasValue)
            {
                var reminderResult = taskItem.SetReminder(command.ReminderAtUtc, utcNow);

                if (reminderResult.IsFailure)
                {
                    // DomainResult (no value) -> Result<TaskDto> using value factory
                    return reminderResult.ToResult(() => MapToDto(taskItem));
                }
            }

            // 4) Persist using repository + UoW
            await _taskRepository.AddAsync(taskItem, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            // 5) Map to DTO and return success
            var dto = MapToDto(taskItem);
            return Result.Ok(dto);
        }

        // Small helper to map Domain entity -> DTO
        private static TaskDto MapToDto(TaskItem task)
            => new()
            {
                TaskId = task.Id,
                UserId = task.UserId,
                Date = task.Date,
                Title = task.Title,
                IsCompleted = task.IsCompleted,
                ReminderAtUtc = task.ReminderAtUtc
            };
    }
}
