using FluentResults;
using Microsoft.Extensions.Logging;
using NotesApp.Application.Abstractions.Persistence;
using NotesApp.Application.Common.Interfaces;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Infrastructure.Notifications
{
    /// <summary>
    /// Temporary implementation of IPushNotificationService that only logs
    /// which devices would receive a push. This gives us end-to-end plumbing
    /// for Phase 7 without committing to FCM/APNs yet.
    /// </summary>
    public sealed class LoggingPushNotificationService : IPushNotificationService
    {
        private readonly IUserDeviceRepository _deviceRepository;
        private readonly ILogger<LoggingPushNotificationService> _logger;

        public LoggingPushNotificationService(IUserDeviceRepository deviceRepository,
                                              ILogger<LoggingPushNotificationService> logger)
        {
            _deviceRepository = deviceRepository;
            _logger = logger;
        }

        public async Task<Result> SendSyncNeededAsync(Guid userId,
                                                      Guid? originDeviceId,
                                                      CancellationToken cancellationToken = default)
        {
            var devices = originDeviceId is { } originId && originId != Guid.Empty
                ? await _deviceRepository
                    .GetActiveDevicesForUserExceptAsync(userId, originId, cancellationToken)
                : await _deviceRepository
                    .GetActiveDevicesForUserAsync(userId, cancellationToken);

            if (devices.Count == 0)
            {
                _logger.LogInformation(
                    "SyncNeeded: no target devices for user {UserId} (OriginDeviceId: {OriginDeviceId})",
                    userId,
                    originDeviceId);

                return Result.Ok();
            }

            var tokens = devices
                .Select(d => d.DeviceToken)
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .ToArray();

            _logger.LogInformation(
                "SyncNeeded: would send push to {DeviceCount} device(s) for user {UserId}. " +
                "OriginDeviceId: {OriginDeviceId}. Tokens: {Tokens}",
                tokens.Length,
                userId,
                originDeviceId,
                tokens);

            // Later: this is where we'll call real NotificationSender / FCM / APNs.
            return Result.Ok();
        }


        public async Task<Result> SendTaskReminderAsync(Guid userId,
                                                        Guid taskId,
                                                        string title,
                                                        string? body,
                                                        CancellationToken cancellationToken = default)
        {
            var devices = await _deviceRepository
                .GetActiveDevicesForUserAsync(userId, cancellationToken);

            if (devices.Count == 0)
            {
                _logger.LogInformation(
                    "TaskReminder: no target devices for user {UserId}, task {TaskId}.",
                    userId,
                    taskId);

                return Result.Ok();
            }

            var tokens = devices
                .Select(d => d.DeviceToken)
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .ToArray();

            _logger.LogInformation(
                "TaskReminder: would send reminder for task {TaskId} to {DeviceCount} device(s) " +
                "for user {UserId}. Title='{Title}', Body='{Body}', Tokens={Tokens}",
                taskId,
                tokens.Length,
                userId,
                title,
                body ?? string.Empty,
                tokens);

            return Result.Ok();
        }
    }
}
