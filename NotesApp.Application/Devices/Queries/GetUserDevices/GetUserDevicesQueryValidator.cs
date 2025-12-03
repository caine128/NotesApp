using FluentValidation;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Application.Devices.Queries.GetUserDevices
{
    public sealed class GetUserDevicesQueryValidator : AbstractValidator<GetUserDevicesQuery>
    {
        public GetUserDevicesQueryValidator()
        {
            // Currently, no parameters to validate.
            // If later you add paging/filtering, add rules here.
        }
    }
}
