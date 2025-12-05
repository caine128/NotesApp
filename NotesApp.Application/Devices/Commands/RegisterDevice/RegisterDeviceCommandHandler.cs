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
                    // Uses your DomainResult<T> → Result<TDto> extension
                    return createResult.ToResult<UserDevice, UserDeviceDto>(device => device.ToDto());
                }

                var device = createResult.Value;
                await _deviceRepository.AddAsync(device, cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);

                var dto = device.ToDto();
                return Result.Ok(dto);
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
                        return reassignResult.ToResult();
                    }
                    if (!string.IsNullOrWhiteSpace(request.DeviceName))
                    {
                        var updateNameResult = existing.UpdateName(request.DeviceName, utcNow);
                        if (updateNameResult.IsFailure)
                        {
                            return updateNameResult.ToResult();
                        }
                    }
                }
                else
                {
                    // Same user:
                    // - make sure it's active
                    // - update name if changed
                    var reactivateResult = existing.Reactivate(utcNow);
                    if (reactivateResult.IsFailure)
                    {
                        return reactivateResult.ToResult();
                    }

                    if (!string.IsNullOrWhiteSpace(request.DeviceName))
                    {
                        var updateNameDomainResult = existing.UpdateName(request.DeviceName, utcNow);
                        if (updateNameDomainResult.IsFailure)
                        {
                            return updateNameDomainResult.ToResult();
                        }
                    }

                    existing.TouchLastSeen(utcNow);
                }

                _deviceRepository.Update(existing);
                await _unitOfWork.SaveChangesAsync(cancellationToken);

                var dto=existing.ToDto();   
                return Result.Ok(dto);
            }
        }

       
    }
}
