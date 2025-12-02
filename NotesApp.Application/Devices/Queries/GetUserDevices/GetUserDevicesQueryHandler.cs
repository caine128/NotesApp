using FluentResults;
using MediatR;
using NotesApp.Application.Abstractions.Persistence;
using NotesApp.Application.Common.Interfaces;
using NotesApp.Application.Devices.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Application.Devices.Queries.GetUserDevices
{
    public sealed class GetUserDevicesQueryHandler
        : IRequestHandler<GetUserDevicesQuery, Result<IReadOnlyList<UserDeviceDto>>>
    {
        private readonly IUserDeviceRepository _deviceRepository;
        private readonly ICurrentUserService _currentUserService;

        public GetUserDevicesQueryHandler(IUserDeviceRepository deviceRepository,
                                          ICurrentUserService currentUserService)
        {
            _deviceRepository = deviceRepository;
            _currentUserService = currentUserService;
        }

        public async Task<Result<IReadOnlyList<UserDeviceDto>>> Handle(GetUserDevicesQuery request,
                                                                       CancellationToken cancellationToken)
        {
            var userId = await _currentUserService.GetUserIdAsync(cancellationToken);

            var devices = await _deviceRepository.GetActiveDevicesForUserAsync(userId, cancellationToken);

            var dtos = devices.Select(d => new UserDeviceDto
            {
                Id = d.Id,
                DeviceToken = d.DeviceToken,
                Platform = d.Platform,
                DeviceName = d.DeviceName,
                LastSeenAtUtc = d.LastSeenAtUtc,
                IsActive = d.IsActive
            }).ToList();

            return Result.Ok<IReadOnlyList<UserDeviceDto>>(dtos);
        }
    }
}
