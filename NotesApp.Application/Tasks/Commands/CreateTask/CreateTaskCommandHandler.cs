using FluentResults;
using MediatR;
using NotesApp.Application.Abstractions.Persistence;
using NotesApp.Application.Common;
using NotesApp.Application.Common.Interfaces;
using NotesApp.Application.Tasks.Models;
using NotesApp.Domain.Entities;


namespace NotesApp.Application.Tasks.Commands.CreateTask
{
    public sealed class CreateTaskCommandHandler 
        : IRequestHandler<CreateTaskCommand, Result<TaskDetailDto>>
    {
        private readonly ITaskRepository _taskRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly ICurrentUserService _currentUserService;
        private readonly ISystemClock _clock;

        public CreateTaskCommandHandler(ITaskRepository taskRepository,
                                        IUnitOfWork unitOfWork,
                                        ICurrentUserService currentUserService,
                                        ISystemClock clock)
        {
            _taskRepository = taskRepository;
            _unitOfWork = unitOfWork;
            _currentUserService = currentUserService;
            _clock = clock;
        }

        public async Task<Result<TaskDetailDto>> Handle(CreateTaskCommand command,
                                                  CancellationToken cancellationToken)
        {
            // 1) Resolve current internal user Id from token/claims.
            var userId = await _currentUserService.GetUserIdAsync(cancellationToken);

            // 2) Get the current UTC time via your clock abstraction (or DateTime.UtcNow).
            var utcNow = _clock.UtcNow;

            // 3) Create domain object using the internal user Id.
            var createResult = TaskItem.Create(userId: userId,
                                               date: command.Date,
                                               title: command.Title,
                                               description: command.Description,
                                               startTime: command.StartTime,
                                               endTime: command.EndTime,
                                               location: command.Location,
                                               travelTime: command.TravelTime,
                                               utcNow: utcNow);


            if (createResult.IsFailure)
            {
                // Convert DomainResult<TaskItem> -> Result<TaskDto>
                return createResult.ToResult<TaskItem, TaskDetailDto>(task => task.ToDetailDto());
            }

            var taskItem = createResult.Value;

            // 📌 3) Domain: set reminder if provided (also returns DomainResult)
            if (command.ReminderAtUtc.HasValue)
            {
                var reminderResult = taskItem.SetReminder(command.ReminderAtUtc, utcNow);

                if (reminderResult.IsFailure)
                {
                    // DomainResult (no value) -> Result<TaskDto> using value factory
                    return reminderResult.ToResult(() => taskItem.ToDetailDto());
                }
            }

            // 📌 4) Persistence: repository + unit of work
            //     (Infra errors here are exceptional and bubble as exceptions)
            await _taskRepository.AddAsync(taskItem, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            // 📌 5) Map domain entity -> DTO using our mapping extension
            var dto = taskItem.ToDetailDto();
            return Result.Ok(dto);

        }
    }
}
