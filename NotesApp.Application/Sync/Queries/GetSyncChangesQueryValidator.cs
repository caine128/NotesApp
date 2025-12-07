using FluentValidation;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Application.Sync.Queries
{
    /// <summary>
    /// Validator for <see cref="GetSyncChangesQuery"/>.
    /// 
    /// We keep validation intentionally light:
    /// - DeviceId is optional, but if provided it must not be Guid.Empty.
    /// - SinceUtc is allowed to be null (initial sync) or any DateTime value;
    ///   further semantic checks (e.g. "not in the future") can be added later
    ///   if the client contract requires it.
    /// </summary>
    public sealed class GetSyncChangesQueryValidator : AbstractValidator<GetSyncChangesQuery>
    {

        public GetSyncChangesQueryValidator()
        {
            RuleFor(x => x.DeviceId)
                .Must(id => !id.HasValue || id.Value != Guid.Empty)
                .WithMessage("DeviceId, if provided, must not be empty.");

            RuleFor(x => x.MaxItemsPerEntity)
                .Must(m => m == null || m > 0)
                .WithMessage("MaxItemsPerEntity, if provided, must be greater than zero.")
                .Must(m => m == null || m <= SyncLimits.HardPullMaxItemsPerEntity)
                .WithMessage($"MaxItemsPerEntity, if provided, must not exceed {SyncLimits.HardPullMaxItemsPerEntity}.");

        }
    }
}
