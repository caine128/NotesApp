using FluentResults;
using MediatR;
using NotesApp.Application.Abstractions.Persistence;
using NotesApp.Application.Common;
using NotesApp.Application.Common.Interfaces;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Application.Devices.Commands.UnregisterDevice
{
    /// <summary>
    /// Handles device unregistration (deactivation).
    /// 
    /// - Loads the device WITHOUT tracking to prevent auto-persistence on failure.
    /// - Verifies ownership and deactivates via domain method.
    /// - Persists only after domain operation succeeds.
    /// </summary>
    public sealed class UnregisterDeviceCommandHandler
        : IRequestHandler<UnregisterDeviceCommand, Result>
    {
        private readonly IUserDeviceRepository _deviceRepository;
        private readonly ICurrentUserService _currentUserService;
        private readonly IUnitOfWork _unitOfWork;
        private readonly ISystemClock _clock;

        public UnregisterDeviceCommandHandler(IUserDeviceRepository deviceRepository,
                                              ICurrentUserService currentUserService,
                                              IUnitOfWork unitOfWork,
                                              ISystemClock clock)
        {
            _deviceRepository = deviceRepository;
            _currentUserService = currentUserService;
            _unitOfWork = unitOfWork;
            _clock = clock;
        }

        public async Task<Result> Handle(UnregisterDeviceCommand request,
                                         CancellationToken cancellationToken)
        {
            var userId = await _currentUserService.GetUserIdAsync(cancellationToken);

            // Load WITHOUT tracking for consistency with other handlers
            var device = await _deviceRepository.GetByIdUntrackedAsync(request.DeviceId, cancellationToken);
            if (device is null || device.UserId != userId)
            {
                return Result.Fail(
                    new Error("Device not found.")
                        .WithMetadata("ErrorCode", "Device.NotFound"));
            }

            // Apply domain operation (entity is NOT tracked, modifications are in-memory only)
            var domainResult = device.Deactivate(_clock.UtcNow);
            if (domainResult.IsFailure)
            {
                // Entity modified but NOT tracked - won't persist
                return domainResult.ToResult();
            }

            // SUCCESS: Explicitly attach and persist
            _deviceRepository.Update(device);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result.Ok();
        }
    }
}
