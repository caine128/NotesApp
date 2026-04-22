using FluentValidation;
using System;

namespace NotesApp.Application.Tasks.Queries
{
    public sealed class GetVirtualTaskOccurrenceDetailQueryValidator
        : AbstractValidator<GetVirtualTaskOccurrenceDetailQuery>
    {
        public GetVirtualTaskOccurrenceDetailQueryValidator()
        {
            RuleFor(x => x.SeriesId)
                .NotEqual(Guid.Empty)
                .WithMessage("SeriesId must be a valid non-empty GUID.");

            RuleFor(x => x.OccurrenceDate)
                .NotEqual(default(DateOnly))
                .WithMessage("OccurrenceDate must be a valid date.");
        }
    }
}
