using FluentValidation;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Application.Devices.Commands.UnregisterDevice
{
    public sealed class UnregisterDeviceCommandValidator : AbstractValidator<UnregisterDeviceCommand>
    {
        public UnregisterDeviceCommandValidator()
        {
            RuleFor(x => x.DeviceId)
                .NotEmpty();
        }
    }
}
