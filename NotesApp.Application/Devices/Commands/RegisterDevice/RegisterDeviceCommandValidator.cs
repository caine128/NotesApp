using FluentValidation;
using NotesApp.Domain.Users;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Application.Devices.Commands.RegisterDevice
{
    public sealed class RegisterDeviceCommandValidator : AbstractValidator<RegisterDeviceCommand>
    {
        public RegisterDeviceCommandValidator()
        {
            RuleFor(x => x.DeviceToken)
                .NotEmpty()
                .MaximumLength(512);

            RuleFor(x => x.Platform)
                .IsInEnum()
                .Must(p => p != DevicePlatform.Unknown)
                .WithMessage("Device platform must be specified.");
        }
    }
}
