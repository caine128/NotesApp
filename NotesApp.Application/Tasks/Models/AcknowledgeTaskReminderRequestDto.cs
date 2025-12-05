using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Application.Tasks.Models
{
    /// <summary>
    /// Request body for acknowledging a task reminder via the API.
    /// The TaskId comes from the route; the body supplies DeviceId and AcknowledgedAtUtc.
    /// </summary>
    public sealed record AcknowledgeTaskReminderRequestDto(
        Guid DeviceId,
        DateTime AcknowledgedAtUtc);
}
