using FluentResults;
using MediatR;
using NotesApp.Application.Devices.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Application.Devices.Queries.GetUserDevices
{
    public sealed class GetUserDevicesQuery : IRequest<Result<IReadOnlyList<UserDeviceDto>>>
    {
    }
}
