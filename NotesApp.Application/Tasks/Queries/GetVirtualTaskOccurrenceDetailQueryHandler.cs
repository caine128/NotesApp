using FluentResults;
using MediatR;
using NotesApp.Application.Abstractions.Persistence;
using NotesApp.Application.Common.Interfaces;
using NotesApp.Application.RecurringAttachments;
using NotesApp.Application.Subtasks;
using NotesApp.Application.Subtasks.Models;
using NotesApp.Application.Tasks.Models;
using NotesApp.Application.Tasks.Services;
using NotesApp.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NotesApp.Application.Tasks.Queries
{
    /// <summary>
    /// Handles <see cref="GetVirtualTaskOccurrenceDetailQuery"/>.
    ///
    /// Projects a <see cref="TaskDetailDto"/> from the series template + exception overrides:
    /// 1. Load series (tenant guard).
    /// 2. Load exception for (SeriesId, OccurrenceDate) — null = no individual override.
    /// 3. Resolve display fields: exception overrides take priority over series template.
    ///    ReminderAtUtc is computed from series.ReminderOffsetMinutes + resolved date + startTime
    ///    (or taken from exception.OverrideReminderAtUtc when set).
    /// 4. Resolve subtasks: exception existence (not row count) determines the source.
    ///    Exception present → use its subtask rows (even if empty — empty = explicitly no subtasks).
    ///    No exception → fall back to series template subtasks.
    /// 5. Return composed TaskDetailDto.
    ///    TaskId = Guid.Empty, RowVersion = empty.
    ///    IsCompleted = exception.IsCompleted when an exception exists; false otherwise.
    ///
    /// No EF navigation properties — all related data loaded via separate repository calls
    /// (consistent with existing conventions in this codebase).
    /// </summary>
    public sealed class GetVirtualTaskOccurrenceDetailQueryHandler
        : IRequestHandler<GetVirtualTaskOccurrenceDetailQuery, Result<TaskDetailDto>>
    {
        private readonly IRecurringTaskSeriesRepository _seriesRepository;
        private readonly IRecurringTaskExceptionRepository _exceptionRepository;
        private readonly IRecurringTaskSubtaskRepository _recurringSubtaskRepository;
        // REFACTORED: added for recurring-task-attachments feature
        private readonly IRecurringTaskAttachmentRepository _recurringAttachmentRepository;
        private readonly ICurrentUserService _currentUserService;

        public GetVirtualTaskOccurrenceDetailQueryHandler(IRecurringTaskSeriesRepository seriesRepository,
                                                          IRecurringTaskExceptionRepository exceptionRepository,
                                                          IRecurringTaskSubtaskRepository recurringSubtaskRepository,
                                                          IRecurringTaskAttachmentRepository recurringAttachmentRepository,
                                                          ICurrentUserService currentUserService)
        {
            _seriesRepository = seriesRepository;
            _exceptionRepository = exceptionRepository;
            _recurringSubtaskRepository = recurringSubtaskRepository;
            _recurringAttachmentRepository = recurringAttachmentRepository;
            _currentUserService = currentUserService;
        }

        public async Task<Result<TaskDetailDto>> Handle(GetVirtualTaskOccurrenceDetailQuery request,
                                                        CancellationToken cancellationToken)
        {
            var userId = await _currentUserService.GetUserIdAsync(cancellationToken);

            // 1. Load the series (tenant guard).
            var series = await _seriesRepository.GetByIdUntrackedAsync(request.SeriesId, cancellationToken);

            if (series is null || series.UserId != userId)
            {
                return Result.Fail<TaskDetailDto>(
                    new Error("Recurring series not found or does not belong to you.")
                        .WithMetadata("ErrorCode", "RecurringSeries.NotFound"));
            }

            // 2. Load the exception for this specific occurrence (may be null).
            var exception = await _exceptionRepository.GetByOccurrenceAsync(request.SeriesId,
                                                                            request.OccurrenceDate,
                                                                            cancellationToken);

            // If the exception is a deletion, the occurrence is suppressed — treat as not found.
            if (exception is not null && exception.IsDeletion)
            {
                return Result.Fail<TaskDetailDto>(
                    new Error("This occurrence has been deleted.")
                        .WithMetadata("ErrorCode", "RecurringOccurrence.Deleted"));
            }

            // 3. Resolve display fields (exception overrides take priority over series template).
            var title       = exception?.OverrideTitle       ?? series.Title;
            var description = exception?.OverrideDescription ?? series.Description;
            var date        = exception?.OverrideDate        ?? request.OccurrenceDate;
            var startTime   = exception?.OverrideStartTime   ?? series.StartTime;
            var endTime     = exception?.OverrideEndTime     ?? series.EndTime;
            var location    = exception?.OverrideLocation    ?? series.Location;
            var travelTime  = exception?.OverrideTravelTime  ?? series.TravelTime;
            var categoryId  = exception?.OverrideCategoryId  ?? series.CategoryId;
            var priority    = exception?.OverridePriority    ?? series.Priority;
            var meetingLink = exception?.OverrideMeetingLink ?? series.MeetingLink;

            // Reminder: if the exception carries an explicit absolute UTC override, use it.
            // Otherwise fall back to series.ReminderOffsetMinutes computed against the
            // resolved date + startTime (same logic as RecurringTaskMaterializerService).
            var reminderAtUtc = RecurringReminderHelper.ComputeReminderUtc(
                overrideReminderAtUtc: exception?.OverrideReminderAtUtc,
                reminderOffsetMinutes: series.ReminderOffsetMinutes,
                occurrenceDate: date,
                startTime: startTime);

            // 4. Resolve subtasks:
            //    If an exception exists → use its subtask rows, even if empty.
            //      Empty exception subtasks = occurrence was explicitly cleared to have no subtasks.
            //    No exception → fall back to series template subtasks.
            //
            //    The key distinction is exception existence, not row count.
            //    Using Count > 0 as the branch would cause an empty-cleared occurrence to
            //    incorrectly show the series template subtasks instead of none.
            List<SubtaskDto> subtaskDtos;

            if (exception is not null)
            {
                var exSubtasks = await _recurringSubtaskRepository.GetByExceptionIdAsync(exception.Id,
                                                                                         cancellationToken);

                // Use exception subtask rows regardless of count.
                // Empty list → occurrence explicitly has no subtasks.
                subtaskDtos = exSubtasks
                    .Select(s => s.ToSubtaskDto())
                    .ToList();
            }
            else
            {
                subtaskDtos = await LoadTemplateSubtasksAsync(request.SeriesId, cancellationToken);
            }

            // 4b. Resolve recurring attachments using the same HasAttachmentOverride semantics:
            //     exception exists AND HasAttachmentOverride=true → exception-scoped attachments
            //     (even if empty — empty = occurrence was explicitly cleared of all attachments)
            //     otherwise → series template attachments.
            var recurringAttachmentDtos = exception is not null && exception.HasAttachmentOverride
                ? (await _recurringAttachmentRepository.GetByExceptionIdAsync(exception.Id, cancellationToken))
                    .Select(a => a.ToRecurringAttachmentDto())
                    .ToList()
                : (await _recurringAttachmentRepository.GetBySeriesIdAsync(request.SeriesId, cancellationToken))
                    .Select(a => a.ToRecurringAttachmentDto())
                    .ToList();

            // 5. Compose the DTO.
            //    TaskId = Guid.Empty  -> client uses this to identify a virtual occurrence.
            //    RowVersion = empty   -> no EF row to version-stamp.
            //    IsCompleted          -> from the exception when one exists; false otherwise.
            var isCompleted = exception?.IsCompleted ?? false;

            var dto = new TaskDetailDto(TaskId: Guid.Empty,
                                        Title: title,
                                        Description: description,
                                        Date: date,
                                        StartTime: startTime,
                                        EndTime: endTime,
                                        IsCompleted: isCompleted,
                                        Location: location,
                                        TravelTime: travelTime,
                                        CreatedAtUtc: series.CreatedAtUtc,
                                        UpdatedAtUtc: series.UpdatedAtUtc,
                                        ReminderAtUtc: reminderAtUtc,
                                        CategoryId: categoryId,
                                        Priority: priority,
                                        MeetingLink: meetingLink,
                                        RowVersion: Array.Empty<byte>())
            {
                Subtasks = subtaskDtos,
                RecurringAttachments = recurringAttachmentDtos
            };

            return Result.Ok(dto);
        }

        // -------------------------------------------------------------------------
        // Private helpers
        // -------------------------------------------------------------------------

        private async System.Threading.Tasks.Task<List<SubtaskDto>> LoadTemplateSubtasksAsync(
            Guid seriesId, CancellationToken cancellationToken)
        {
            var templateSubtasks = await _recurringSubtaskRepository.GetBySeriesIdAsync(
                seriesId, cancellationToken);

            return templateSubtasks.Select(s => s.ToSubtaskDto()).ToList();
        }
    }
}
