using FluentValidation;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Application.Tasks.Queries
{
    public sealed class GetTaskSummariesForDayQueryValidator
                : AbstractValidator<GetTaskSummariesForDayQuery>
    {
        public GetTaskSummariesForDayQueryValidator()
        {
            RuleFor(x => x.Date)
                .Must(d => d != default)
                .WithMessage("Date is required.");
        }
    }
}
