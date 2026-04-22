using FluentResults;
using MediatR;
using Microsoft.Extensions.Options;
using NotesApp.Application.Abstractions.Persistence;
using NotesApp.Application.Common;
using NotesApp.Application.Common.Interfaces;
using NotesApp.Application.Configuration;
using NotesApp.Application.Tasks.Models;
using NotesApp.Application.Tasks.Services;
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
        private readonly ISubtaskRepository _subtaskRepository;
        private readonly ICategoryRepository _categoryRepository; // REFACTORED: added for CategoryId ownership validation
        private readonly IOutboxRepository _outboxRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly ICurrentUserService _currentUserService;
        private readonly ISystemClock _clock;
        private readonly RecurringTaskOptions _recurringOptions;

        // REFACTORED: added recurring-task dependencies for recurring-tasks feature
        private readonly IRecurringTaskRootRepository _rootRepository;
        private readonly IRecurringTaskSeriesRepository _seriesRepository;
        private readonly IRecurringTaskSubtaskRepository _recurringSubtaskRepository;
        private readonly IRecurringTaskMaterializerService _materializerService;

        public CreateTaskCommandHandler(ITaskRepository taskRepository,
                                        ISubtaskRepository subtaskRepository,
                                        ICategoryRepository categoryRepository,
                                        IOutboxRepository outboxRepository,
                                        IUnitOfWork unitOfWork,
                                        ICurrentUserService currentUserService,
                                        ISystemClock clock,
                                        IOptions<RecurringTaskOptions> recurringOptions,
                                        IRecurringTaskRootRepository rootRepository,
                                        IRecurringTaskSeriesRepository seriesRepository,
                                        IRecurringTaskSubtaskRepository recurringSubtaskRepository,
                                        IRecurringTaskMaterializerService materializerService)
        {
            _taskRepository = taskRepository;
            _subtaskRepository = subtaskRepository;
            _categoryRepository = categoryRepository;
            _outboxRepository = outboxRepository;
            _unitOfWork = unitOfWork;
            _currentUserService = currentUserService;
            _clock = clock;
            _recurringOptions = recurringOptions.Value;
            _rootRepository = rootRepository;
            _seriesRepository = seriesRepository;
            _recurringSubtaskRepository = recurringSubtaskRepository;
            _materializerService = materializerService;
        }

        public async Task<Result<TaskDetailDto>> Handle(CreateTaskCommand command,
                                                        CancellationToken cancellationToken)
        {
            // 1) Resolve current internal user Id from token/claims.
            var userId = await _currentUserService.GetUserIdAsync(cancellationToken);

            // 2) Get the current UTC time via the clock abstraction.
            var utcNow = _clock.UtcNow;

            // REFACTORED: validate CategoryId ownership before creating any entity.
            if (command.CategoryId.HasValue)
            {
                var category = await _categoryRepository.GetByIdUntrackedAsync(
                    command.CategoryId.Value, cancellationToken);

                if (category is null || category.UserId != userId)
                {
                    return Result.Fail<TaskDetailDto>(
                        new Error("Category not found or does not belong to you.")
                            .WithMetadata("ErrorCode", "Categories.NotFound"));
                }
            }

            // 3) Branch: recurring vs. single-task creation.
            return command.RecurrenceRule is null
                ? await HandleSingleTaskAsync(command, userId, utcNow, cancellationToken)
                : await HandleRecurringTaskAsync(command, userId, utcNow, cancellationToken);
        }

        // -------------------------------------------------------------------------
        // Single-task path (existing behavior, unchanged)
        // -------------------------------------------------------------------------

        private async Task<Result<TaskDetailDto>> HandleSingleTaskAsync(CreateTaskCommand command,
                                                                        Guid userId,
                                                                        DateTime utcNow,
                                                                        CancellationToken cancellationToken)
        {
            var createResult = TaskItem.Create(
                userId: userId,
                date: command.Date,
                title: command.Title,
                description: command.Description,
                startTime: command.StartTime,
                endTime: command.EndTime,
                location: command.Location,
                travelTime: command.TravelTime,
                categoryId: command.CategoryId,
                priority: command.Priority, // REFACTORED: added Priority
                utcNow: utcNow,
                meetingLink: command.MeetingLink); // REFACTORED: added MeetingLink

            if (createResult.IsFailure)
            {
                return createResult.ToResult<TaskItem, TaskDetailDto>(task => task.ToDetailDto());
            }

            var taskItem = createResult.Value;

            if (command.ReminderAtUtc.HasValue)
            {
                var reminderResult = taskItem.SetReminder(command.ReminderAtUtc, utcNow);
                if (reminderResult.IsFailure)
                {
                    return reminderResult.ToResult(() => taskItem.ToDetailDto());
                }
            }

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
                taskItem.CategoryId,
                taskItem.Priority, // REFACTORED: added Priority
                taskItem.MeetingLink, // REFACTORED: added MeetingLink
                Event = TaskEventType.Created.ToString(),
                OccurredAtUtc = utcNow
            });

            var outboxResult = OutboxMessage.Create(
                aggregate: taskItem,
                eventType: TaskEventType.Created,
                payload: payload,
                utcNow: utcNow);

            if (outboxResult.IsFailure || outboxResult.Value is null)
            {
                return outboxResult.ToResult<OutboxMessage, TaskDetailDto>(_ => taskItem.ToDetailDto());
            }

            await _taskRepository.AddAsync(taskItem, cancellationToken);
            await _outboxRepository.AddAsync(outboxResult.Value, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result.Ok(taskItem.ToDetailDto());
        }

        // -------------------------------------------------------------------------
        // Recurring-task path (new)
        // REFACTORED: added for recurring-tasks feature
        // -------------------------------------------------------------------------

        private async Task<Result<TaskDetailDto>> HandleRecurringTaskAsync(CreateTaskCommand command,
                                                                           Guid userId,
                                                                           DateTime utcNow,
                                                                           CancellationToken cancellationToken)
        {
            var rule = command.RecurrenceRule!;
            var today = DateOnly.FromDateTime(utcNow);

            // 1. Create the logical root (stable identity anchor shared by all series segments).
            var rootResult = RecurringTaskRoot.Create(userId, utcNow);
            if (rootResult.IsFailure)
            {
                return rootResult.ToResult<RecurringTaskRoot, TaskDetailDto>(_ => default!);
            }
            var root = rootResult.Value;

            // 2. Create the first series segment.
            //    MaterializedUpToDate starts at StartsOnDate - 1 day so the materializer
            //    begins from StartsOnDate on its first run.
            var initialMaterializedUpTo = rule.StartsOnDate.AddDays(-1);

            var seriesResult = RecurringTaskSeries.Create(userId: userId,
                                                          rootId: root.Id,
                                                          rruleString: rule.RRuleString,
                                                          startsOnDate: rule.StartsOnDate,
                                                          endsBeforeDate: rule.EndsBeforeDate,
                                                          title: command.Title,
                                                          description: command.Description,
                                                          startTime: command.StartTime,
                                                          endTime: command.EndTime,
                                                          location: command.Location,
                                                          travelTime: command.TravelTime,
                                                          categoryId: command.CategoryId,
                                                          priority: command.Priority,
                                                          meetingLink: command.MeetingLink,
                                                          reminderOffsetMinutes: rule.ReminderOffsetMinutes,
                                                          materializedUpToDate: initialMaterializedUpTo,
                                                          utcNow: utcNow);

            if (seriesResult.IsFailure)
            {
                return seriesResult.ToResult<RecurringTaskSeries, TaskDetailDto>(_ => default!);
            }
            var series = seriesResult.Value;

            // 3. Create series template subtasks (keyed on SeriesId, not a TaskId).
            var templateSubtasks = new List<RecurringTaskSubtask>();
            if (rule.TemplateSubtasks is { Count: > 0 })
            {
                foreach (var stDto in rule.TemplateSubtasks)
                {
                    var stResult = RecurringTaskSubtask.CreateForSeries(userId: userId,
                                                                        seriesId: series.Id,
                                                                        text: stDto.Text,
                                                                        position: stDto.Position,
                                                                        utcNow: utcNow);

                    if (stResult.IsFailure)
                    {
                        return Result.Fail<TaskDetailDto>(
                            stResult.Errors.Select(e => new Error(e.Message)).ToList());
                    }

                    templateSubtasks.Add(stResult.Value);
                }
            }

            // 4. Materialize the initial batch (StartsOnDate → today inclusive).
            //    For a brand-new series there are no exceptions yet.
            var batchResult = _materializerService.MaterializeInitialBatch(
                series: series,
                templateSubtasks: templateSubtasks,
                exceptions: [],
                exceptionSubtasksById: new Dictionary<Guid, IReadOnlyList<RecurringTaskSubtask>>(),
                utcNow: utcNow,
                batchSize: _recurringOptions.InitialMaterializationBatchSize);

            if (batchResult.IsFailed)
                return batchResult.ToResult<TaskDetailDto>();

            var batch = batchResult.Value;

            // 5. Advance MaterializedUpToDate to today (no-op for future series because
            //    initialMaterializedUpTo == StartsOnDate - 1 day <= today - 1 day < today).
            //    AdvanceMaterializedHorizon is idempotent when the value doesn't move forward.
            var advanceResult = series.AdvanceMaterializedHorizon(today, utcNow);
            if (advanceResult.IsFailure)
            {
                return Result.Fail<TaskDetailDto>(
                    advanceResult.Errors.Select(e => new Error(e.Message)));
            }

            // 6. Build outbox messages before touching persistence.
            var outboxMessages = new List<OutboxMessage>();

            var rootOutboxResult = OutboxMessage.Create(
                aggregate: root,
                eventType: RecurringRootEventType.Created,
                payload: JsonSerializer.Serialize(new
                {
                    RootId = root.Id,
                    root.UserId,
                    Event = RecurringRootEventType.Created.ToString(),
                    OccurredAtUtc = utcNow
                }),
                utcNow: utcNow);
            if (rootOutboxResult.IsFailure || rootOutboxResult.Value is null)
            {
                return rootOutboxResult.ToResult<OutboxMessage, TaskDetailDto>(_ => default!);
            }
            outboxMessages.Add(rootOutboxResult.Value);

            var seriesOutboxResult = OutboxMessage.Create(
                aggregate: series,
                eventType: RecurringSeriesEventType.Created,
                payload: JsonSerializer.Serialize(new
                {
                    SeriesId = series.Id,
                    series.RootId,
                    series.UserId,
                    series.RRuleString,
                    series.StartsOnDate,
                    Event = RecurringSeriesEventType.Created.ToString(),
                    OccurredAtUtc = utcNow
                }),
                utcNow: utcNow);
            if (seriesOutboxResult.IsFailure || seriesOutboxResult.Value is null)
            {
                return seriesOutboxResult.ToResult<OutboxMessage, TaskDetailDto>(_ => default!);
            }
            outboxMessages.Add(seriesOutboxResult.Value);

            foreach (var task in batch.Tasks)
            {
                var taskOutboxResult = OutboxMessage.Create(
                    aggregate: task,
                    eventType: TaskEventType.Created,
                    payload: JsonSerializer.Serialize(new
                    {
                        TaskId = task.Id,
                        task.UserId,
                        task.Date,
                        task.Title,
                        task.RecurringSeriesId,
                        task.CanonicalOccurrenceDate,
                        Event = TaskEventType.Created.ToString(),
                        OccurredAtUtc = utcNow
                    }),
                    utcNow: utcNow);

                if (taskOutboxResult.IsFailure || taskOutboxResult.Value is null)
                {
                    continue; // skip outbox for this task — non-fatal
                }

                outboxMessages.Add(taskOutboxResult.Value);
            }

            // 7. Persist all entities in one SaveChangesAsync — fully atomic.
            await _rootRepository.AddAsync(root, cancellationToken);
            await _seriesRepository.AddAsync(series, cancellationToken);

            foreach (var st in templateSubtasks)
            {
                await _recurringSubtaskRepository.AddAsync(st, cancellationToken);
            }

            foreach (var task in batch.Tasks)
            {
                await _taskRepository.AddAsync(task, cancellationToken);
            }

            foreach (var subtask in batch.Subtasks)
            {
                await _subtaskRepository.AddAsync(subtask, cancellationToken);
            }

            foreach (var outbox in outboxMessages)
            {
                await _outboxRepository.AddAsync(outbox, cancellationToken);
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);

            // 8. Return the TaskDetailDto for the first materialized occurrence.
            //    If StartsOnDate is in the future the batch will be empty; in that case we
            //    synthesize a DTO from the series template to acknowledge the creation.
            if (batch.Tasks.Count > 0)
            {
                return Result.Ok(batch.Tasks[0].ToDetailDto());
            }

            // Future series: no occurrence materialized yet.
            return Result.Ok(new TaskDetailDto(
                TaskId: series.Id,          // placeholder — no TaskItem exists yet
                Title: series.Title,
                Description: series.Description,
                Date: series.StartsOnDate,
                StartTime: series.StartTime,
                EndTime: series.EndTime,
                IsCompleted: false,
                Location: series.Location,
                TravelTime: series.TravelTime,
                CreatedAtUtc: series.CreatedAtUtc,
                UpdatedAtUtc: series.UpdatedAtUtc,
                ReminderAtUtc: null,
                CategoryId: series.CategoryId,
                Priority: series.Priority,
                MeetingLink: series.MeetingLink,
                RowVersion: Array.Empty<byte>()));
        }
    }
}
