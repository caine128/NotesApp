using NotesApp.Application.Tasks.Models;
using NotesApp.Domain.Entities;


namespace NotesApp.Application.Tasks
{
    public static class TaskMappings
    {

        public static TaskDetailDto ToDetailDto(this TaskItem task) =>
          new(task.Id,
              task.Title,
              task.Description,
              task.Date,
              task.StartTime,
              task.EndTime,
              task.IsCompleted,
              task.Location,
              task.TravelTime,
              task.CreatedAtUtc,
              task.UpdatedAtUtc,
              task.ReminderAtUtc);

        public static TaskSummaryDto ToSummaryDto(this TaskItem task) =>
            new(
                task.Id,
                task.Title,
                task.Date,
                task.StartTime,
                task.EndTime,
                task.IsCompleted,
                task.Location,
                task.TravelTime
            );

        public static TaskOverviewDto ToOverviewDto(this TaskItem task) =>
            new(
                task.Title,
                task.Date
            );


        public static IReadOnlyList<TaskSummaryDto> ToSummaryDtoList(this IEnumerable<TaskItem> tasks) =>
            tasks.Select(t => t.ToSummaryDto())
                 .ToList();

        public static IReadOnlyList<TaskOverviewDto> ToOverviewDtoList(this IEnumerable<TaskItem> tasks) =>
                    tasks.Select(t => t.ToOverviewDto())
                         .ToList();
    }
}
