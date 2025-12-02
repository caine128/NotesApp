using NotesApp.Domain.Users;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Application.Devices.Models
{
    public sealed class UserDeviceDto
    {
        public Guid Id { get; init; }
        public string DeviceToken { get; init; } = string.Empty;
        public DevicePlatform Platform { get; init; }
        public string? DeviceName { get; init; }
        public DateTime LastSeenAtUtc { get; init; }
        public bool IsActive { get; init; }
    }
}
