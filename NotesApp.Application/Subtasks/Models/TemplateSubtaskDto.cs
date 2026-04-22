namespace NotesApp.Application.Subtasks.Models
{
    /// <summary>
    /// A single subtask template entry, used in both series-create
    /// (<see cref="NotesApp.Application.Tasks.Models.RecurrenceRuleDto"/>) and
    /// ThisAndFollowing-update operations
    /// (<see cref="NotesApp.Application.Tasks.Commands.UpdateRecurringTaskOccurrence.UpdateRecurringTaskOccurrenceCommand"/>).
    /// </summary>
    public sealed record TemplateSubtaskDto(string Text, string Position);
}
