using FluentResults;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Application.Common.Interfaces
{
    /// <summary>
    /// High-level abstraction for sending push notifications.
    /// 
    /// Phase 7: we only need "sync needed" pushes, but this can be
    /// extended later (task reminders, briefings, etc.).
    /// </summary>
    public interface IPushNotificationService
    {
        /// <summary>
        /// Sends a "sync needed" push to the user's other devices
        /// (excluding the origin device, if provided).
        /// </summary>
        /// <param name="userId">User whose other devices should sync.</param>
        /// <param name="originDeviceId">
        /// Device that originated the change (will be excluded from targets).
        /// May be null if change came from web or unknown device.
        /// </param>
        /// <param name="cancellationToken">Cancellation token.</param>
        Task<Result> SendSyncNeededAsync(Guid userId,
                                         Guid? originDeviceId,
                                         CancellationToken cancellationToken = default);


        /// <summary>
        /// Sends a task reminder notification for a specific task.
        /// Implementations decide which devices to target.
        /// </summary>
        Task<Result> SendTaskReminderAsync(
            Guid userId,
            Guid taskId,
            string title,
            string? body,
            CancellationToken cancellationToken = default);
    }
}
