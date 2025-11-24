using FluentValidation;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Application.Notes.Queries
{
    /// <summary>
    /// Validator for GetNotesForDayQuery.
    /// Guards against obviously invalid dates.
    /// </summary>
    public sealed class GetNotesForDayQueryValidator : AbstractValidator<GetNotesForDayQuery>
    {
        public GetNotesForDayQueryValidator()
        {
            RuleFor(x => x.Date)
                .Must(d => d != default)
                .WithMessage("Date is required.");
        }
    }
}
