using FluentResults;
using MediatR;
using NotesApp.Application.Abstractions.Persistence;
using NotesApp.Application.Common;
using NotesApp.Application.Common.Interfaces;
using NotesApp.Application.Devices.Models;
using NotesApp.Domain.Users;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Application.Devices.Commands.RegisterDevice
{
    /// <summary>
    /// Handles device registration logic:
    /// - If token exists for same user: reactivate + touch.
    /// - If token exists for another user: reassign to current user.
    /// - If token doesn't exist: create new device.
    /// </summary>
    public sealed class RegisterDeviceCommandHandler
        : IRequestHandler<RegisterDeviceCommand, Result<UserDeviceDto>>
    {
        private readonly IUserDeviceRepository _deviceRepository;
        private readonly ICurrentUserService _currentUserService;
        private readonly IUnitOfWork _unitOfWork;
        private readonly ISystemClock _clock;

        public RegisterDeviceCommandHandler(IUserDeviceRepository deviceRepository,
                                            ICurrentUserService currentUserService,
                                            IUnitOfWork unitOfWork,
                                            ISystemClock clock)
        {
            _deviceRepository = deviceRepository;
            _currentUserService = currentUserService;
            _unitOfWork = unitOfWork;
            _clock = clock;
        }

        public async Task<Result<UserDeviceDto>> Handle(RegisterDeviceCommand request,
                                                        CancellationToken cancellationToken)
        {
            var userId = await _currentUserService.GetUserIdAsync(cancellationToken);
            var utcNow = _clock.UtcNow;

            // Normalize token once
            var normalizedToken = request.DeviceToken?.Trim() ?? string.Empty;

            // 1) Try to find existing device by token (any user)
            var existing = await _deviceRepository.GetByTokenAsync(normalizedToken, cancellationToken);

            if (existing is null)
            {
                // 2a) No existing device -> create new
                var createResult = UserDevice.Create(userId,
                                                     normalizedToken,
                                                     request.Platform,
                                                     request.DeviceName,
                                                     utcNow);

                if (createResult.IsFailure)
                {
                    var errors = createResult.Errors
                        .Select(e => new Error(e.Code).WithMessage(e.Message))
                        .ToList();

                    return Result.Fail(errors);
                }

                var device = createResult.Value;
                await _deviceRepository.AddAsync(device, cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);

                return Result.Ok(MapToDto(device));
            }
            else
            {
                // 2b) Device exists -> reuse / reassign logic
                if (existing.UserId != userId)
                {
                    // Token migrated from another user -> reassign
                    var reassignResult = existing.ReassignToUser(userId, utcNow);
                    if (reassignResult.IsFailure)
                    {
                        var errors = reassignResult.Errors
                            .Select(e => new Error(e.Code).WithMessage(e.Message))
                            .ToList();

                        return Result.Fail(errors);
                    }
                }
                else
                {
                    // Same user:
                    // - make sure it's active
                    // - update name if changed
                    existing.Reactivate(utcNow);
                    if (!string.IsNullOrWhiteSpace(request.DeviceName))
                    {
                        existing.UpdateName(request.DeviceName, utcNow);
                    }
                    existing.TouchLastSeen(utcNow);
                }

                _deviceRepository.Update(existing);
                await _unitOfWork.SaveChangesAsync(cancellationToken);

                return Result.Ok(MapToDto(existing));
            }
        }

        private static UserDeviceDto MapToDto(UserDevice device)
        {
            return new UserDeviceDto
            {
                Id = device.Id,
                DeviceToken = device.DeviceToken,
                Platform = device.Platform,
                DeviceName = device.DeviceName,
                LastSeenAtUtc = device.LastSeenAtUtc,
                IsActive = device.IsActive
            };
        }
    }
}
