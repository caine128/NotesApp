using FluentResults;
using MediatR;
using NotesApp.Application.Abstractions.Persistence;
using NotesApp.Application.Common;
using NotesApp.Application.Common.Interfaces;
using NotesApp.Application.Devices.Models;
using NotesApp.Application.Notes.Models;
using NotesApp.Domain.Entities;
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
    ///     
    /// Existing devices are loaded WITHOUT tracking to prevent auto-persistence on failure.
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

            // Load WITHOUT tracking - modifications won't auto-persist if we return early
            var existing = await _deviceRepository.GetByTokenUntrackedAsync(normalizedToken, cancellationToken);

            if (existing is null)
            {
                // ═══════════════════════════════════════════════════════════════
                // CREATE PATH: No existing device -> create new
                // ═══════════════════════════════════════════════════════════════
                var createResult = UserDevice.Create(userId,
                                                     normalizedToken,
                                                     request.Platform,
                                                     request.DeviceName,
                                                     utcNow);

                if (createResult.IsFailure)
                {
                    return createResult.ToResult<UserDevice, UserDeviceDto>(device => device.ToDto());
                }

                var device = createResult.Value;
                await _deviceRepository.AddAsync(device, cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);

                return Result.Ok(device.ToDto());
            }

            else
            {
                // ═══════════════════════════════════════════════════════════════
                // UPDATE PATH: Device exists -> reuse / reassign
                // Entity is NOT tracked, modifications are in-memory only
                // ═══════════════════════════════════════════════════════════════
                if (existing.UserId != userId)
                {
                    // Token migrated from another user -> reassign
                    var reassignResult = existing.ReassignToUser(userId, utcNow);
                    if (reassignResult.IsFailure)
                    {
                        // Entity modified but NOT tracked - won't persist
                        return reassignResult.ToResult();
                    }

                    if (!string.IsNullOrWhiteSpace(request.DeviceName))
                    {
                        var updateNameResult = existing.UpdateName(request.DeviceName, utcNow);
                        if (updateNameResult.IsFailure)
                        {
                            // Entity modified but NOT tracked - won't persist
                            return updateNameResult.ToResult();
                        }
                    }
                }
                else
                {
                    // Same user: reactivate and update name if changed
                    var reactivateResult = existing.Reactivate(utcNow);
                    if (reactivateResult.IsFailure)
                    {
                        // Entity modified but NOT tracked - won't persist
                        return reactivateResult.ToResult();
                    }

                    if (!string.IsNullOrWhiteSpace(request.DeviceName))
                    {
                        var updateNameResult = existing.UpdateName(request.DeviceName, utcNow);
                        if (updateNameResult.IsFailure)
                        {
                            // Entity modified but NOT tracked - won't persist
                            return updateNameResult.ToResult();
                        }
                    }

                    existing.TouchLastSeen(utcNow);
                }

                // SUCCESS: Explicitly attach and persist
                _deviceRepository.Update(existing);
                await _unitOfWork.SaveChangesAsync(cancellationToken);

                return Result.Ok(existing.ToDto());
            }
        }

       
    }
}
