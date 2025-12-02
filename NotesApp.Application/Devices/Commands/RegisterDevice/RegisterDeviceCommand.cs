using FluentResults;
using MediatR;
using NotesApp.Application.Devices.Models;
using NotesApp.Domain.Users;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Application.Devices.Commands.RegisterDevice
{
    /// <summary>
    /// Registers or updates a device for the current user.
    /// </summary>
    public sealed class RegisterDeviceCommand : IRequest<Result<UserDeviceDto>>
    {
        public string DeviceToken { get; init; } = string.Empty;
        public DevicePlatform Platform { get; init; }
        public string? DeviceName { get; init; }
    }
}
