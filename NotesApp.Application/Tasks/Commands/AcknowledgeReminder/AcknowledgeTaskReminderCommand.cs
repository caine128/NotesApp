using FluentResults;
using MediatR;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Application.Tasks.Commands.AcknowledgeReminder
{
    /// <summary>
    /// Command to acknowledge a reminder for a task.
    /// TaskId is supplied by the API layer, DeviceId and AcknowledgedAtUtc by the client.
    /// </summary>
    public sealed class AcknowledgeTaskReminderCommand : IRequest<Result>
    {
        public Guid TaskId { get; init; }
        public Guid DeviceId { get; init; }
        public DateTime AcknowledgedAtUtc { get; init; }
    }
}
