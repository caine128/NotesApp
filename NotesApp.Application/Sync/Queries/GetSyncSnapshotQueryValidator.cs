using FluentValidation;
using System;

namespace NotesApp.Application.Sync.Queries
{
    public sealed class GetSyncSnapshotQueryValidator : AbstractValidator<GetSyncSnapshotQuery>
    {
        public GetSyncSnapshotQueryValidator()
        {
            RuleFor(x => x.DeviceId)
                .Must(id => !id.HasValue || id.Value != Guid.Empty)
                .WithMessage("DeviceId, if provided, must not be empty.");
        }
    }
}
