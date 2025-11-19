using NotesApp.Domain.Entities;


namespace NotesApp.Application.Tasks
{
    public static class TaskMappings
    {
        public static TaskDto ToDto(this TaskItem task)
             => new()
             {
                 TaskId = task.Id,
                 UserId = task.UserId,
                 Date = task.Date,
                 Title = task.Title,
                 IsCompleted = task.IsCompleted,
                 ReminderAtUtc = task.ReminderAtUtc
             };

        public static IReadOnlyList<TaskDto> ToDtoList(this IEnumerable<TaskItem> tasks)
            => tasks
                .Select(t => t.ToDto())
                .ToList();
    }
}
