using FluentResults;
using FluentValidation;
using MediatR;
using NotesApp.Application.Abstractions.Persistence;
using NotesApp.Application.Common;
using NotesApp.Domain.Common;
using NotesApp.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Application.Tasks.Commands.CreateTask
{
    public sealed class CreateTaskCommandHandler : IRequestHandler<CreateTaskCommand, Result<TaskDto>>
    {
        private readonly ITaskRepository _taskRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly ISystemClock _clock;

        public CreateTaskCommandHandler(ITaskRepository taskRepository,
                                        IUnitOfWork unitOfWork,
                                        ISystemClock clock)
        {
            _taskRepository = taskRepository;
            _unitOfWork = unitOfWork;
            _clock = clock;
        }

        public async Task<Result<TaskDto>> Handle(CreateTaskCommand command,
                                                  CancellationToken cancellationToken)
        {
            // 📌 1) Get current time from our clock abstraction (testable, consistent)
            var utcNow = _clock.UtcNow;

            // 📌 2) Domain creation (enforces invariants: non-empty user, non-empty title, etc.)
            var createResult = TaskItem.Create(userId: command.UserId,
                                               date: command.Date,
                                               title: command.Title,
                                               utcNow: utcNow);


            if (createResult.IsFailure || createResult.Value is null)
            {
                // Convert DomainResult<TaskItem> -> Result<TaskDto>
                return createResult.ToResult<TaskItem, TaskDto>(task => task.ToDto());
            }

            var taskItem = createResult.Value;

            // 📌 3) Domain: set reminder if provided (also returns DomainResult)
            if (command.ReminderAtUtc.HasValue)
            {
                var reminderResult = taskItem.SetReminder(command.ReminderAtUtc, utcNow);

                if (reminderResult.IsFailure)
                {
                    // DomainResult (no value) -> Result<TaskDto> using value factory
                    return reminderResult.ToResult(() => taskItem.ToDto());
                }
            }

            // 📌 4) Persistence: repository + unit of work
            //     (Infra errors here are exceptional and bubble as exceptions)
            await _taskRepository.AddAsync(taskItem, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            // 📌 5) Map domain entity -> DTO using our mapping extension
            var dto = taskItem.ToDto();
            return Result.Ok(dto);

        }
    }
}
