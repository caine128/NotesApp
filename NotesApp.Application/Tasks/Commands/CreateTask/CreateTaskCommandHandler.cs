using FluentResults;
using MediatR;
using NotesApp.Application.Abstractions.Persistence;
using NotesApp.Application.Common;
using NotesApp.Application.Common.Interfaces;
using NotesApp.Application.Tasks.Models;
using NotesApp.Domain.Common;
using NotesApp.Domain.Entities;
using System.Text.Json;
using System.Threading.Tasks;


namespace NotesApp.Application.Tasks.Commands.CreateTask
{
    public sealed class CreateTaskCommandHandler 
        : IRequestHandler<CreateTaskCommand, Result<TaskDetailDto>>
    {
        private readonly ITaskRepository _taskRepository;
        private readonly IOutboxRepository _outboxRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly ICurrentUserService _currentUserService;
        private readonly ISystemClock _clock;

        public CreateTaskCommandHandler(ITaskRepository taskRepository,
            IOutboxRepository outboxRepository,
                                        IUnitOfWork unitOfWork,
                                        ICurrentUserService currentUserService,
                                        ISystemClock clock)
        {
            _taskRepository = taskRepository;
            _outboxRepository = outboxRepository;
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

            // 4) Domain: set reminder if provided
            if (command.ReminderAtUtc.HasValue)
            {
                var reminderResult = taskItem.SetReminder(command.ReminderAtUtc, utcNow);

                if (reminderResult.IsFailure)
                {
                    // DomainResult (no value) -> Result<TaskDto> using value factory
                    return reminderResult.ToResult(() => taskItem.ToDetailDto());
                }
            }


            // 5) Create outbox message BEFORE adding to repositories
            //    This ensures we don't touch persistence if outbox creation fails
            var payload = JsonSerializer.Serialize(new
            {
                TaskId = taskItem.Id,
                taskItem.UserId,
                taskItem.Date,
                taskItem.Title,
                taskItem.Description,
                taskItem.StartTime,
                taskItem.EndTime,
                taskItem.Location,
                taskItem.TravelTime,
                taskItem.IsCompleted,
                Event = TaskEventType.Created.ToString(),
                OccurredAtUtc = utcNow
            });

            var outboxResult = OutboxMessage.Create(aggregate: taskItem,
                                                    eventType: TaskEventType.Created,
                                                    payload: payload,
                                                    utcNow: utcNow);

            if (outboxResult.IsFailure || outboxResult.Value is null)
            {
                return outboxResult.ToResult<OutboxMessage, TaskDetailDto>(_ => taskItem.ToDetailDto());
            }

            // 6) Persist: only after all domain operations and outbox creation succeed
            await _taskRepository.AddAsync(taskItem, cancellationToken);
            await _outboxRepository.AddAsync(outboxResult.Value, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            // 7) Map domain entity -> DTO
            return Result.Ok(taskItem.ToDetailDto());

        }
    }
}
