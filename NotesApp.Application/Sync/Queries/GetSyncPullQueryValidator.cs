using FluentValidation;
using System;

namespace NotesApp.Application.Sync.Queries
{
    /// <summary>
    /// Validator for <see cref="GetSyncPullQuery"/>.
    /// </summary>
    public sealed class GetSyncPullQueryValidator : AbstractValidator<GetSyncPullQuery>
    {
        public GetSyncPullQueryValidator()
        {
            RuleFor(x => x.AfterSequence)
                .GreaterThanOrEqualTo(0)
                .WithMessage("AfterSequence must be greater than or equal to zero.");

            RuleFor(x => x.DeviceId)
                .Must(id => !id.HasValue || id.Value != Guid.Empty)
                .WithMessage("DeviceId, if provided, must not be empty.");

            RuleFor(x => x.Limit)
                .Must(l => l == null || (l >= SyncPullLimits.MinPullLimit && l <= SyncPullLimits.MaxPullLimit))
                .WithMessage($"Limit, if provided, must be between {SyncPullLimits.MinPullLimit} and {SyncPullLimits.MaxPullLimit}.");
        }
    }
}
