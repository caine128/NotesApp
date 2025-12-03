using NotesApp.Application.Devices.Models;
using NotesApp.Domain.Users;


namespace NotesApp.Application.Devices
{
    public static class UserDeviceMappings
    {
        public static UserDeviceDto ToDto(this UserDevice entity)
        {
            return new UserDeviceDto
            {
                Id = entity.Id,
                DeviceToken = entity.DeviceToken,
                Platform = entity.Platform,
                DeviceName = entity.DeviceName,
                LastSeenAtUtc = entity.LastSeenAtUtc,
                IsActive = entity.IsActive
            };
        }

        public static List<UserDeviceDto> ToDtoList(this IEnumerable<UserDevice> entities)
            => entities.Select(e => e.ToDto()).ToList();
    }
}
