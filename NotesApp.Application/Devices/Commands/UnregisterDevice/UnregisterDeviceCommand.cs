using FluentResults;
using MediatR;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Application.Devices.Commands.UnregisterDevice
{
    public sealed class UnregisterDeviceCommand : IRequest<Result>
    {
        public Guid DeviceId { get; init; }
    }
}
