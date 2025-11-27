using FluentValidation;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Application.Calendar.Queries
{
    public sealed class CalendarOverviewForRangeQueryValidator
    : AbstractValidator<CalendarOverviewForRangeQuery>
    {
        public CalendarOverviewForRangeQueryValidator()
        {
            RuleFor(x => x.Start)
                .NotEqual(default(DateOnly))
                .WithMessage("Start date is required.");

            RuleFor(x => x.EndExclusive)
                .NotEqual(default(DateOnly))
                .WithMessage("End date is required.");

            RuleFor(x => x)
                .Must(x => x.EndExclusive > x.Start)
                .WithMessage("EndExclusive must be greater than Start.");
        }
    }
}
