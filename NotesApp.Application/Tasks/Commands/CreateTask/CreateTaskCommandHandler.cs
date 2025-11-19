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

        public CreateTaskCommandHandler(ITaskRepository taskRepository,
                                        IUnitOfWork unitOfWork)
        {
            _taskRepository = taskRepository;
            _unitOfWork = unitOfWork;
        }

        public async Task<Result<TaskDto>> Handle(CreateTaskCommand request,
                                                  CancellationToken cancellationToken)
        {
            // domain factory does domain-level validation
            DomainResult<TaskItem> createResult = TaskItem.Create(tenantId: request.TenantId,
                                                                  date: request.Date,
                                                                  title: request.Title,
                                                                  content: request.Content,
                                                                  reminderAtUtc: request.ReminderAtUtc);

            if (createResult.IsFailure)
            {
                // map domain errors to Result<TaskDto>
                return Result.Fail<TaskDto>(createResult.Errors
                    .Select(e => new Error(e.Code).WithMessage(e.Message)));
            }

            var task = createResult.Value;

            _taskRepository.Add(task);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result.Ok(task.ToDto());
        }
    }
}
