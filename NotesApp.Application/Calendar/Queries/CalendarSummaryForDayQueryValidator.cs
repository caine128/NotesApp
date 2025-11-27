using FluentValidation;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Application.Calendar.Queries
{
    public sealed class CalendarSummaryForDayQueryValidator
    : AbstractValidator<CalendarSummaryForDayQuery>
    {
        public CalendarSummaryForDayQueryValidator()
        {
            RuleFor(x => x.Date)
                .NotEqual(default(DateOnly))
                .WithMessage("Date is required.");
        }
    }
}
