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

            var device = await _deviceRepository.GetByIdAsync(request.DeviceId, cancellationToken);
            if (device is null || device.UserId != userId)
            {
                return Result.Fail(new Error("Device.NotFound")
                    .WithMessage("Device could not be found for the current user."));
            }

            var result = device.Deactivate(_clock.UtcNow);
            if (result.IsFailure)
            {
                var errors = result.Errors
                    .Select(e => new Error(e.Code).WithMessage(e.Message))
                    .ToList();

                return Result.Fail(errors);
            }

            _deviceRepository.Update(device);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result.Ok();
        }
    }
}
